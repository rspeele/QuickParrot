using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using QuickParrot.Core.Keyboard;
using QuickParrot.Core.Navigation;
using QuickParrot.Core.Playback;
using static QuickParrot.Input.NativeMethods;

namespace QuickParrot.Input;

/// <summary>
/// A global keyboard hook thread running <see cref="ChordKeyFilter"/> and <see cref="PushToTalk"/> (even with hotkeys
/// off). The chord event handler runs there and must never block. Keys aimed at more-elevated windows go unseen.
/// </summary>
public sealed unsafe class LowLevelKeyboardHook : IDisposable, IChordKeyHook
{
    private const string WindowClassName = "QuickParrot.KeyboardHook";
    private const uint WM_RUN_COMMANDS = WM_APP + 1;

    private static readonly uint s_processId = (uint)Environment.ProcessId;

    [ThreadStatic]
    private static LowLevelKeyboardHook? t_current;

    private readonly ChordKeyFilter _filter;
    private readonly Action<ChordEvent> _onChordEvent;
    private readonly ConcurrentQueue<Action> _commands = new();
    private readonly Lock _lock = new();

    // Guarded by _lock; the filter itself is only touched on the hook thread while it runs.
    private Thread? _thread;
    private uint _threadId;
    private ScanKey _chordKey;
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
        };
        _chordKey = chordKey;
        _onChordEvent = onChordEvent;
        PushToTalk = new SendInputPushToTalk(this, PushToTalkBinding.Default);
    }

    /// <summary>Hand this to the engine. Stopping the hook releases anything it holds.</summary>
    public SendInputPushToTalk PushToTalk { get; }

    /// <summary>Settable from any thread; moves an in-progress hold across to the new binding.</summary>
    public PushToTalkBinding PushToTalkBinding
    {
        get => PushToTalk.Binding;
        set
        {
            PushToTalk.Binding = value;
            lock (_lock)
                Execute(() => _filter.PushToTalkKey = value.Key);
        }
    }

    /// <summary>
    /// Callable from any thread. Bit (n - 1) set: plain Fn plays favorite n while a fullscreen game is focused.
    /// See <see cref="QuickParrot.Core.Favorites.FavoriteStatus.ChordlessMask"/>.
    /// </summary>
    public void SetChordlessFavoriteSlots(int mask)
    {
        lock (_lock)
            Execute(() => _filter.ChordlessFavoriteSlots = mask);
    }

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
                Execute(() => Emit(_filter.SetChordKey(value)));
            }
        }
    }

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
                var reinstall = value && !_enabled && _thread is not null;
                _enabled = value;
                Execute(() =>
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
        {
            if (_thread is not null)
                return;

            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() => Run(started))
            {
                IsBackground = true,
                Name = "QuickParrot keyboard hook",
                Priority = ThreadPriority.Highest, // every keystroke system-wide waits on this thread
            };
            thread.Start();
            try
            {
                started.Task.GetAwaiter().GetResult();
            }
            catch
            {
                thread.Join();
                throw;
            }

            _thread = thread;
        }
    }

    /// <summary>Removes the hook, ends any active chord and releases push-to-talk. Safe to call repeatedly.</summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (_thread is null)
                return;

            PostThreadMessageW(_threadId, WM_QUIT, 0, 0);
            _thread.Join();
            _thread = null;
        }
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
            if (_thread is null)
                throw new InvalidOperationException("The keyboard hook isn't running.");

            Execute(() =>
            {
                _capture?.TrySetResult(null);
                _capture = capture;
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
        lock (_lock)
        {
            Execute(() =>
            {
                if (_capture == capture)
                {
                    _capture = null;
                    _filter.CancelCapture();
                }
            });
        }

        capture.TrySetCanceled(cancellationToken);
    }

    /// <summary>Queues a command for the hook thread; if it isn't running, runs it here unless told to drop it.</summary>
    internal void Post(Action command, bool dropIfStopped)
    {
        lock (_lock)
        {
            if (_thread is not null || !dropIfStopped)
                Execute(command);
        }
    }

    // Caller holds _lock. Runs on the hook thread if it's running, else right here.
    private void Execute(Action command)
    {
        if (_thread is null)
        {
            command();
            return;
        }

        _commands.Enqueue(command);
        PostThreadMessageW(_threadId, WM_RUN_COMMANDS, 0, 0);
    }

    private void Run(TaskCompletionSource started)
    {
        t_current = this;
        PushToTalk.Attach();
        try
        {
            _threadId = GetCurrentThreadId();
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
        catch (Exception e)
        {
            Cleanup();
            started.SetException(e);
            return;
        }

        started.SetResult();
        while (GetMessageW(out var msg, 0, 0, 0) > 0)
        {
            if (msg.hwnd == 0 && msg.message == WM_RUN_COMMANDS)
                RunCommands();
            else
                DispatchMessageW(in msg);
        }

        Cleanup();
    }

    private void Cleanup()
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
        RunCommands();
        PushToTalk.Detach();
        Emit(_filter.Reset());
        _filter.CancelCapture();
        _capture?.TrySetResult(null);
        _capture = null;
        t_current = null;
    }

    private void RunCommands()
    {
        while (_commands.TryDequeue(out var command))
            command();
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
        if (PushToTalk.HandleKey(scanCode, isExtended, isKeyDown, isInjected))
            return true;

        var result = _filter.Process(
            scanCode, isExtended, isKeyDown, isInjected, captureAllowed: !_filter.Capturing || OwnsForeground());

        Emit(result.Event);
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
            current.Emit(current._filter.ResetIfChordActive(current.IsChordKeyDown()));
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
        PushToTalk.Resync();
        Emit(_filter.Reset(IsChordKeyDown()));
    }

    // The hook never hides the chord key, so the async key state tracks it even when the hook missed its events.
    private bool IsChordKeyDown() => KeyState.IsDown(_filter.ChordKey);

    // An exception escaping into the native hook chain would take down the process.
    private void Emit(ChordEvent? chordEvent)
    {
        if (chordEvent is null)
            return;

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

    internal void RaiseError(string message)
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
