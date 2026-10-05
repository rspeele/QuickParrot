using System.ComponentModel;
using System.Runtime.InteropServices;
using QuickParrot.Core.Keyboard;
using static QuickParrot.Input.NativeMethods;

namespace QuickParrot.Input;

/// <summary>
/// A global keyboard hook thread running <see cref="ChordKeyFilter"/> and <see cref="PushToTalk"/> (even with hotkeys
/// off). The chord event handler runs there and must never block. Keys aimed at more-elevated windows go unseen.
/// </summary>
public sealed unsafe class LowLevelKeyboardHook : IDisposable
{
    private const string WindowClassName = "QuickParrot.KeyboardHook";

    private static readonly uint s_processId = (uint)Environment.ProcessId;

    [ThreadStatic]
    private static LowLevelKeyboardHook? t_current;

    private readonly ChordKeyFilter _filter;
    private readonly Action<ChordEvent> _onChordEvent;
    private readonly HookThread _thread = new("QuickParrot keyboard hook");
    private readonly Lock _lock = new();
    private readonly KeyboardTextTranslator _text = new();

    // Guarded by _lock; the filter itself is only touched on the hook thread while it runs.
    private ScanKey _chordKey;
    private ScanKey _saveNavigationKey = ScanKey.DefaultSaveNavigationKey;
    private ScanKey _searchKey = ScanKey.DefaultSearchKey;
    private ScanKey _fragmentsKey = ScanKey.DefaultFragmentsKey;
    private string? _searchLibrary;
    private PushToTalkBinding _pushToTalkBinding = PushToTalkBinding.Default;
    private bool _enabled = true;

    // Hook thread only.
    private nint _module;
    private nint _hook;
    private nint _window;
    private nint _foregroundEvents;
    private nint _desktopEvents;
    private TaskCompletionSource<ScanKey?>? _capture;

    public LowLevelKeyboardHook(ScanKey chordKey, Action<ChordEvent> onChordEvent)
    {
        _filter = new ChordKeyFilter(chordKey, ForegroundWindow.IsFullscreenGame)
        {
            PushToTalkKey = PushToTalkBinding.Default.Key,
            SearchEnabled = false,
        };
        _chordKey = chordKey;
        _onChordEvent = onChordEvent;
        PushToTalk = new SendInputPushToTalk(_thread, PushToTalkBinding.Default);
        PushToTalk.ErrorOccurred += RaiseError;
    }

    /// <summary>Hand this to the engine. Stopping the hook releases anything it holds.</summary>
    public SendInputPushToTalk PushToTalk { get; }

    /// <summary>Settable from any thread; moves an in-progress hold across to the new binding.</summary>
    public PushToTalkBinding PushToTalkBinding
    {
        get
        {
            lock (_lock)
                return _pushToTalkBinding;
        }
        set
        {
            SendInputPushToTalk.ThrowIfMalformed(value);
            lock (_lock)
            {
                _pushToTalkBinding = value;
                _thread.Post(() =>
                {
                    PushToTalk.SetBinding(value);
                    _filter.PushToTalkKey = value.Key;
                });
            }
        }
    }

    /// <summary>
    /// Callable from any thread. Bit (n - 1) set: plain Fn plays favorite n while a fullscreen game is focused.
    /// See <see cref="QuickParrot.Core.Favorites.FavoriteStatus.ChordlessMask"/>.
    /// </summary>
    public void SetChordlessFavoriteSlots(int mask) => _thread.Post(() => _filter.ChordlessFavoriteSlots = mask);

    public ScanKey ChordKey
    {
        get
        {
            lock (_lock)
                return _chordKey;
        }
        set
        {
            ChordKeyFilter.ThrowIfInvalid(value);
            lock (_lock)
            {
                _chordKey = value;
                _thread.Post(() => Emit(_filter.SetChordKey(value)));
            }
        }
    }

    public ScanKey SaveNavigationKey
    {
        get
        {
            lock (_lock)
                return _saveNavigationKey;
        }
        set
        {
            if (!value.IsValidSaveNavigationKey)
                throw new ArgumentException($"{value} can't save navigation.", nameof(value));

            lock (_lock)
            {
                _saveNavigationKey = value;
                _thread.Post(() => _filter.SaveNavigationKey = value);
            }
        }
    }

    public ScanKey SearchKey
    {
        get { lock (_lock) return _searchKey; }
        set
        {
            if (!value.IsValidSearchKey)
                throw new ArgumentException($"{value} can't search.", nameof(value));
            lock (_lock)
            {
                _searchKey = value;
                _thread.Post(() =>
                {
                    Emit(_filter.CancelSession());
                    _filter.SearchKey = value;
                });
            }
        }
    }

    public void SetSearchLibrary(string? root)
    {
        lock (_lock)
        {
            if (_searchLibrary == root)
                return;
            _searchLibrary = root;
            _thread.Post(() =>
            {
                Emit(_filter.CancelSession());
                _filter.SearchEnabled = !string.IsNullOrEmpty(root);
            });
        }
    }

    public ScanKey FragmentsKey
    {
        get { lock (_lock) return _fragmentsKey; }
        set
        {
            if (!value.IsValidSearchKey)
                throw new ArgumentException($"{value} can't search fragments.", nameof(value));
            lock (_lock)
            {
                _fragmentsKey = value;
                _thread.Post(() =>
                {
                    Emit(_filter.CancelSession());
                    _filter.FragmentsKey = value;
                });
            }
        }
    }

    public void CompleteFragmentsSession(long sessionId) => _thread.Post(() =>
    {
        if (_filter.CompleteFragmentsSession(sessionId))
            _text.ClearDeadKey();
    });

    /// <summary>
    /// When false every key passes through, except the ups of keys already hidden and push-to-talk merging.
    /// Re-enabling reinstalls the keyboard hook, as manual recovery if Windows silently dropped it.
    /// </summary>
    public bool Enabled
    {
        get
        {
            lock (_lock)
                return _enabled;
        }
        set
        {
            lock (_lock)
            {
                var reinstall = value && !_enabled && _thread.IsRunning;
                _enabled = value;
                _thread.Post(() =>
                {
                    Emit(_filter.SetEnabled(value));
                    if (reinstall)
                        ReinstallHook();
                });
            }
        }
    }

    /// <summary>Installs the hook. Throws <see cref="Win32Exception"/> if Windows refuses.</summary>
    public void Start()
    {
        lock (_lock)
            _thread.Start(Install, Uninstall, Release);
    }

    /// <summary>Removes the hook, ends any active chord and releases push-to-talk. Safe to call repeatedly.</summary>
    public void Stop()
    {
        lock (_lock)
            _thread.Stop();
    }

    public void Dispose() => Stop();

    /// <summary>
    /// Hides and reports the next physical key pressed, instead of processing it. Completes with null if
    /// Escape is pressed, the hook stops, or a newer capture replaces this one.
    /// </summary>
    public Task<ScanKey?> CaptureNextKeyAsync(CancellationToken cancellationToken = default)
    {
        var capture = new TaskCompletionSource<ScanKey?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            if (!_thread.IsRunning)
                throw new InvalidOperationException("The keyboard hook isn't running.");

            _thread.Post(() =>
            {
                _capture?.TrySetResult(null);
                _capture = capture;
                Emit(_filter.CancelSession());
                _filter.BeginCapture();
            });
        }

        if (cancellationToken.CanBeCanceled)
        {
            var registration = cancellationToken.Register(() => CancelCapture(capture, cancellationToken));
            capture.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
        }

        return capture.Task;
    }

    private void CancelCapture(TaskCompletionSource<ScanKey?> capture, CancellationToken cancellationToken)
    {
        _thread.Post(() =>
        {
            if (_capture == capture)
            {
                _capture = null;
                _filter.CancelCapture();
            }
        });

        capture.TrySetCanceled(cancellationToken);
    }

    private void Install()
    {
        t_current = this;
        PushToTalk.Attach();
        _module = GetModuleHandleW(null);
        _window = CreateMessageWindow(_module);
        WTSRegisterSessionNotification(_window, NOTIFY_FOR_THIS_SESSION);

        _hook = SetWindowsHookExW(WH_KEYBOARD_LL, &HookProc, _module, 0);
        if (_hook == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Couldn't install the keyboard hook.");

        // Best effort: without these a lost chord-key up, or a dead hook, goes unnoticed as before.
        _foregroundEvents = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, 0, &WinEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);
        _desktopEvents = SetWinEventHook(
            EVENT_SYSTEM_DESKTOPSWITCH, EVENT_SYSTEM_DESKTOPSWITCH, 0, &WinEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);
        ResetFilter(); // a chord key held since before the hook existed mustn't chord on its repeats
    }

    private void Uninstall()
    {
        if (_hook != 0)
            UnhookWindowsHookEx(_hook);

        if (_foregroundEvents != 0)
            UnhookWinEvent(_foregroundEvents);

        if (_desktopEvents != 0)
            UnhookWinEvent(_desktopEvents);

        if (_window != 0)
        {
            WTSUnRegisterSessionNotification(_window);
            DestroyWindow(_window);
        }

        _hook = 0;
        _window = 0;
        _foregroundEvents = 0;
        _desktopEvents = 0;
    }

    private void Release()
    {
        PushToTalk.Detach();
        Emit(_filter.Reset());
        _filter.CancelCapture();
        _capture?.TrySetResult(null);
        _capture = null;
        t_current = null;
    }

    private static nint CreateMessageWindow(nint module)
    {
        fixed (char* className = WindowClassName)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WndProc,
                hInstance = module,
                lpszClassName = className,
            };
            if (RegisterClassExW(in windowClass) == 0 && Marshal.GetLastPInvokeError() != ERROR_CLASS_ALREADY_EXISTS)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Couldn't register the hook window class.");
        }

        var window = CreateWindowExW(0, WindowClassName, null, 0, 0, 0, 0, 0, HWND_MESSAGE, 0, module, 0);
        if (window == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Couldn't create the hook window.");

        return window;
    }

    [UnmanagedCallersOnly]
    private static nint HookProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode == HC_ACTION && t_current is { } hook && hook.HandleKey((KBDLLHOOKSTRUCT*)lParam))
            return 1;

        return CallNextHookEx(0, nCode, wParam, lParam);
    }

    private bool HandleKey(KBDLLHOOKSTRUCT* key)
    {
        var flags = key->flags;
        var scanCode = (int)key->scanCode;
        var isExtended = (flags & LLKHF_EXTENDED) != 0;
        var isKeyDown = (flags & LLKHF_UP) == 0;
        var isInjected = (flags & (LLKHF_INJECTED | LLKHF_LOWER_IL_INJECTED)) != 0;
        if (!isInjected)
            _text.Update(key->vkCode, isKeyDown);

        var result = _filter.Process(
            scanCode, isExtended, isKeyDown, isInjected, captureAllowed: !_filter.Capturing || OwnsForeground());

        if (result.Event is SearchPressed or FragmentsPressed)
        {
            _text.Reset();
            _text.Update(key->vkCode, isKeyDown);
        }

        if (!result.Swallow && PushToTalk.HandleKey(scanCode, isExtended, isKeyDown, isInjected))
            return true;

        Emit(result.Event is SearchKeyPressed
            ? new SearchTextEntered(_text.Translate(key->vkCode, key->scanCode)) : result.Event);
        if (result.CaptureEnded)
        {
            _capture?.TrySetResult(result.CapturedKey);
            _capture = null;
        }

        return result.Swallow;
    }

    private static bool OwnsForeground()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var processId);
        return processId == s_processId;
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (t_current is { } hook && msg == WM_WTSSESSION_CHANGE)
            hook.OnSessionChange((int)wParam);

        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    // Key-ups are lost if they go to a more-elevated window, and always on the secure desktop (UAC).
    [UnmanagedCallersOnly]
    private static void WinEventProc(
        nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
    {
        if (t_current is not { } current)
            return;

        if (eventType == EVENT_SYSTEM_DESKTOPSWITCH)
        {
            current.ResetFilter();
        }
        else
        {
            current.Emit(current._filter.ResetIfChordActive(current.IsChordKeyDown(), current.PushToTalk.PhysicallyHeld));
            current.PushToTalk.OnForegroundChanged();
        }
    }

    // The secure desktop swallows key-ups, so anything held across a lock or user switch is stale.
    private void OnSessionChange(int reason)
    {
        if (reason is WTS_SESSION_LOCK or WTS_SESSION_UNLOCK or WTS_CONSOLE_CONNECT or WTS_CONSOLE_DISCONNECT
            or WTS_REMOTE_CONNECT or WTS_REMOTE_DISCONNECT)
        {
            ResetFilter();
        }
    }

    // Manual recovery for a hook Windows silently dropped: a fresh one goes in before the old comes out, so no
    // key slips past if the old one was still alive.
    private void ReinstallHook()
    {
        if (_hook == 0)
            return; // already uninstalled: this is a re-enable drained during shutdown

        var fresh = SetWindowsHookExW(WH_KEYBOARD_LL, &HookProc, _module, 0);
        if (fresh == 0)
        {
            RaiseError(new Win32Exception(Marshal.GetLastPInvokeError(), "Couldn't reinstall the keyboard hook.").Message);
            return;
        }

        if (_hook != 0)
            UnhookWindowsHookEx(_hook);

        _hook = fresh;
        ResetFilter();
    }

    private void ResetFilter()
    {
        _text.Reset();
        PushToTalk.Resync();
        Emit(_filter.Reset(IsChordKeyDown(), PushToTalk.PhysicallyHeld));
    }

    // The hook never hides the chord key, so the async key state tracks it even when the hook missed its events.
    private bool IsChordKeyDown() => KeyState.IsDown(_filter.ChordKey);

    // An exception escaping into the native hook chain would take down the process.
    private void Emit(ChordEvent? chordEvent)
    {
        if (chordEvent is null)
            return;

        if (chordEvent is ChordCancelled or SearchSelectionPressed or SearchBackspacePressed
            or FragmentSelectionPressed or FragmentEnterReleased or FragmentBackspacePressed)
            _text.ClearDeadKey();

        try
        {
            _onChordEvent(chordEvent);
        }
        catch
        {
            // Dropped: nothing useful can be done from inside the hook.
        }
    }

    /// <summary>
    /// Raised on the hook thread when a recovery action, e.g. reinstalling the hook, fails, or push-to-talk
    /// can't be simulated.
    /// </summary>
    public event Action<string>? ErrorOccurred;

    private void RaiseError(string message)
    {
        try
        {
            ErrorOccurred?.Invoke(message);
        }
        catch
        {
            // Dropped: nothing useful can be done from inside the hook.
        }
    }
}
