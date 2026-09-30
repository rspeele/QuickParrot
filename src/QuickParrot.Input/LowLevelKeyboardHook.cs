using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using QuickParrot.Core.Keyboard;
using QuickParrot.Core.Navigation;
using static QuickParrot.Input.NativeMethods;

namespace QuickParrot.Input;

/// <summary>
/// A global low-level keyboard hook running <see cref="ChordKeyFilter"/> on its own thread; the chord event
/// handler runs on that thread and must never block. It can't see keys aimed at more-elevated windows.
/// </summary>
public sealed unsafe class LowLevelKeyboardHook : IDisposable, IChordKeyHook
{
    private const string WindowClassName = "QuickParrot.KeyboardHook";
    private const uint WM_RUN_COMMANDS = WM_APP + 1;

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
    private nint _hook;
    private nint _window;
    private TaskCompletionSource<ScanKey?>? _capture;

    private long _keyEventsSeen;

    public LowLevelKeyboardHook(ScanKey chordKey, Action<ChordEvent> onChordEvent)
    {
        _filter = new ChordKeyFilter(chordKey);
        _chordKey = chordKey;
        _onChordEvent = onChordEvent;
    }

    /// <summary>Total key events the hook has received, injected ones included; handy to check it's alive.</summary>
    public long KeyEventsSeen => Volatile.Read(ref _keyEventsSeen);

    public bool IsRunning
    {
        get
        {
            lock (_lock)
                return _thread is not null;
        }
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

    /// <summary>When false every key passes through, except the ups of keys already hidden.</summary>
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
                _enabled = value;
                Execute(() => Emit(_filter.SetEnabled(value)));
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

    /// <summary>Removes the hook and ends any active chord. Safe to call repeatedly.</summary>
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

    /// <summary>Forgets held keys, e.g. after events may have gone missing.</summary>
    public void Reset()
    {
        lock (_lock)
            Execute(() => Emit(_filter.Reset()));
    }

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
        try
        {
            _threadId = GetCurrentThreadId();
            var module = GetModuleHandleW(null);
            _window = CreateMessageWindow(module);
            WTSRegisterSessionNotification(_window, NOTIFY_FOR_THIS_SESSION);

            _hook = SetWindowsHookExW(WH_KEYBOARD_LL, &HookProc, module, 0);
            if (_hook == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Couldn't install the keyboard hook.");
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

        if (_window != 0)
        {
            WTSUnRegisterSessionNotification(_window);
            DestroyWindow(_window);
        }

        _hook = 0;
        _window = 0;
        RunCommands();
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
        _keyEventsSeen++;
        var flags = key->flags;
        var result = _filter.Process(
            (int)key->scanCode,
            isExtended: (flags & LLKHF_EXTENDED) != 0,
            isKeyDown: (flags & LLKHF_UP) == 0,
            isInjected: (flags & (LLKHF_INJECTED | LLKHF_LOWER_IL_INJECTED)) != 0);

        Emit(result.Event);
        if (result.CaptureEnded)
        {
            _capture?.TrySetResult(result.CapturedKey);
            _capture = null;
        }

        return result.Swallow;
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WM_WTSSESSION_CHANGE && t_current is { } hook)
            hook.OnSessionChange((int)wParam);

        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    // The secure desktop swallows key-ups, so anything held across a lock or user switch is stale.
    private void OnSessionChange(int reason)
    {
        if (reason is WTS_SESSION_LOCK or WTS_SESSION_UNLOCK or WTS_CONSOLE_CONNECT or WTS_CONSOLE_DISCONNECT
            or WTS_REMOTE_CONNECT or WTS_REMOTE_DISCONNECT)
        {
            Emit(_filter.Reset());
        }
    }

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
}
