using System.Runtime.InteropServices;
using QuickParrot.Core.Keyboard;
using QuickParrot.Core.Playback;
using static QuickParrot.Input.NativeMethods;

namespace QuickParrot.Input;

/// <summary>
/// Holds the game's push-to-talk key or mouse button with SendInput, merged with the user's own presses by
/// <see cref="PushToTalkMerger"/>. Its state lives on the keyboard hook's thread; Press and Release only queue.
/// </summary>
public sealed unsafe class SendInputPushToTalk : IPushToTalk
{
    private const string UnreachableMessage =
        "Push-to-talk couldn't reach the focused app. If the game runs as administrator, QuickParrot must too.";

    // Just after our own release, the system's view may still show it down.
    private const long SettleMilliseconds = 50;
    private const int EmergencyWaitMilliseconds = 250;

    [ThreadStatic]
    private static SendInputPushToTalk? t_current;

    private readonly HookThread _thread;

    // Held around each command, so an emergency release sees what it left held and stops any later press.
    private readonly Lock _commandLock = new();
    private volatile PushToTalkBinding[] _heldByUs = [];
    private volatile bool _emergencyReleased;

    // Hook thread only, or the caller's thread while the hook isn't running.
    private readonly PushToTalkMerger _merger;
    private nint _mouseHook;
    private PushToTalkBinding _lastUpBinding;
    private long _lastUpTicks;
    private bool _sendFailing;

    internal SendInputPushToTalk(HookThread thread, PushToTalkBinding binding)
    {
        ThrowIfMalformed(binding);
        _thread = thread;
        _merger = new PushToTalkMerger(binding);
    }

    /// <summary>Raised on the hook thread when push-to-talk can't be simulated or watched.</summary>
    public event Action<string>? ErrorOccurred;

    /// <summary>Queues the press and returns; ignored while the keyboard hook isn't running.</summary>
    public void Press() => _thread.Post(() => RunCommand(OnPress), dropIfStopped: true);

    /// <summary>Queues the release and returns; stopping the keyboard hook releases anyway.</summary>
    public void Release() => _thread.Post(() => RunCommand(OnRelease), dropIfStopped: true);

    /// <summary>
    /// For a crash: from any thread, without waiting long on the hook thread, sends an up for everything held and
    /// ignores any later press. May duplicate an up. Never throws.
    /// </summary>
    public void EmergencyRelease()
    {
        var entered = _commandLock.TryEnter(EmergencyWaitMilliseconds);
        try
        {
            _emergencyReleased = true;
            foreach (var binding in _heldByUs)
            {
                var input = ToInput(binding, down: false);
                SendInput(1, &input, sizeof(INPUT));
            }
        }
        catch (Exception)
        {
            // Best effort: the process is going down anyway.
        }
        finally
        {
            if (entered)
                _commandLock.Exit();
        }
    }

    internal void Attach() => t_current = this;

    /// <summary>Moves any hold over to the new binding. Hook thread only.</summary>
    internal void SetBinding(PushToTalkBinding binding) => RunCommand(() => ApplyBinding(binding));

    /// <summary>Returns true to hide the physical key event. After an emergency release everything passes.</summary>
    internal bool HandleKey(int scanCode, bool isExtended, bool isKeyDown, bool isInjected) =>
        !_emergencyReleased && _merger.HandleKey(new ScanKey(scanCode, isExtended), isKeyDown, isInjected);

    internal void Resync() => _merger.SyncPhysical(SystemSeesDown(_merger.Binding));

    internal bool PhysicallyHeld => _merger.PhysicallyHeld;

    /// <summary>A failed up may get through to the newly focused window.</summary>
    internal void OnForegroundChanged()
    {
        if (_merger.PendingUpCount != 0)
            RunCommand(RetryPendingUps);
    }

    internal void Detach()
    {
        RunCommand(OnRelease);
        t_current = null;
    }

    internal static void ThrowIfMalformed(PushToTalkBinding binding)
    {
        if (!binding.IsWellFormed)
            throw new ArgumentException($"{binding} can't be push-to-talk.", nameof(binding));
    }

    private void RunCommand(Action command)
    {
        lock (_commandLock)
        {
            if (_emergencyReleased)
                return;

            command();
            UpdateMouseHook();
            _heldByUs = _merger.HeldByUs();
        }
    }

    private void OnPress()
    {
        RetryPendingUps();
        if (!_merger.Holding)
            _merger.SyncBeforePress(SystemSeesDown(_merger.Binding));

        Send(_merger.Press(), _merger.Binding);
    }

    private void OnRelease()
    {
        RetryPendingUps();
        Send(_merger.Release(), _merger.Binding);
    }

    private void ApplyBinding(PushToTalkBinding binding)
    {
        RetryPendingUps();
        var old = _merger.Binding;
        var (releaseOld, pressNew) = _merger.SetBinding(binding, SystemSeesDown(binding));
        Send(releaseOld, old);
        Send(pressNew, binding);
    }

    // Bounded by the starting count, since each failure goes back on the end.
    private void RetryPendingUps()
    {
        var remaining = _merger.PendingUpCount;
        while (remaining-- > 0 && _merger.TryTakePendingUp(out var binding))
            Send(PushToTalkSend.Up, binding);
    }

    private bool SystemSeesDown(PushToTalkBinding binding) =>
        (binding != _lastUpBinding || Environment.TickCount64 - _lastUpTicks >= SettleMilliseconds)
        && KeyState.IsDown(binding);

    // Consecutive failures are one problem, so the user hears about it once.
    private void Send(PushToTalkSend send, PushToTalkBinding binding)
    {
        if (send == PushToTalkSend.Nothing)
            return;

        var input = ToInput(binding, send == PushToTalkSend.Down);
        var sent = SendInput(1, &input, sizeof(INPUT)) == 1;
        if (send == PushToTalkSend.Up)
        {
            _lastUpBinding = binding;
            _lastUpTicks = Environment.TickCount64;
            _merger.UpSent(binding, sent);
        }

        if (sent)
        {
            _sendFailing = false;
        }
        else if (!_sendFailing)
        {
            _sendFailing = true;
            RaiseError(UnreachableMessage);
        }
    }

    // Scan codes rather than virtual keys, so games reading raw input or DirectInput see the key too.
    private static INPUT ToInput(PushToTalkBinding binding, bool down)
    {
        if (binding.Key is { } key)
        {
            var keyFlags = KEYEVENTF_SCANCODE
                | (key.IsExtended ? KEYEVENTF_EXTENDEDKEY : 0)
                | (down ? 0 : KEYEVENTF_KEYUP);
            var keyInput = new KEYBDINPUT { wScan = (ushort)key.ScanCode, dwFlags = keyFlags };
            return new INPUT { type = INPUT_KEYBOARD, u = new INPUTUNION { ki = keyInput } };
        }

        var (flags, data) = binding.MouseButton switch
        {
            PushToTalkMouseButton.Middle => (down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP, 0u),
            PushToTalkMouseButton.X1 => (down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP, XBUTTON1),
            _ => (down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP, XBUTTON2),
        };
        var mouseInput = new MOUSEINPUT { mouseData = data, dwFlags = flags };
        return new INPUT { type = INPUT_MOUSE, u = new INPUTUNION { mi = mouseInput } };
    }

    // The mouse hook only exists while holding a mouse binding, so mouse movement costs nothing otherwise.
    private void UpdateMouseHook()
    {
        var wanted = _merger.Holding && _merger.Binding.MouseButton is not null;
        if (wanted == (_mouseHook != 0))
            return;

        if (!wanted)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = 0;
            return;
        }

        _mouseHook = SetWindowsHookExW(WH_MOUSE_LL, &MouseProc, GetModuleHandleW(null), 0);
        if (_mouseHook == 0)
            RaiseError("Couldn't watch the mouse, so letting go of push-to-talk mid-clip may cut it off.");
    }

    // An exception escaping into the native hook chain would take down the process.
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

    [UnmanagedCallersOnly]
    private static nint MouseProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode == HC_ACTION && t_current is { } current && current.HandleMouse((uint)wParam, (MSLLHOOKSTRUCT*)lParam))
            return 1;

        return CallNextHookEx(0, nCode, wParam, lParam);
    }

    private bool HandleMouse(uint message, MSLLHOOKSTRUCT* info)
    {
        if (_emergencyReleased)
            return false;

        PushToTalkMouseButton button;
        if (message is WM_MBUTTONDOWN or WM_MBUTTONUP)
        {
            button = PushToTalkMouseButton.Middle;
        }
        else if (message is WM_XBUTTONDOWN or WM_XBUTTONUP)
        {
            var xButton = info->mouseData >> 16;
            if (xButton is not (XBUTTON1 or XBUTTON2))
                return false;

            button = xButton == XBUTTON1 ? PushToTalkMouseButton.X1 : PushToTalkMouseButton.X2;
        }
        else
        {
            return false;
        }

        var isInjected = (info->flags & (LLMHF_INJECTED | LLMHF_LOWER_IL_INJECTED)) != 0;
        return _merger.HandleMouse(button, message is WM_MBUTTONDOWN or WM_XBUTTONDOWN, isInjected);
    }
}
