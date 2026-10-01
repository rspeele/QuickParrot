using QuickParrot.Core.Navigation;

namespace QuickParrot.Core.Keyboard;

/// <summary>
/// Turns raw physical key events into <see cref="ChordEvent"/>s and decides which to hide from other apps.
/// Runs inside the low-level keyboard hook, so it never allocates per event. Not thread-safe.
/// </summary>
public sealed class ChordKeyFilter
{
    private const int SlotCount = 512; // scan codes 0-255, doubled for the extended flag

    private static readonly ChordEvent Pressed = new ChordPressed();
    private static readonly ChordEvent Released = new ChordReleased();
    private static readonly ChordEvent Cancelled = new ChordCancelled();
    private static readonly ChordEvent Grab = new GrabPressed();
    private static readonly ChordEvent[] Digits = BuildDigitEvents(shift: false);
    private static readonly ChordEvent[] ShiftedDigits = BuildDigitEvents(shift: true);

    // Held tracks every physical key so auto-repeat can be told apart from a fresh press; swallowed marks
    // keys whose down was hidden, so their up is hidden too and other apps never see an unmatched event.
    private readonly bool[] _held = new bool[SlotCount];
    private readonly bool[] _swallowed = new bool[SlotCount];
    private bool _leftShift;
    private bool _rightShift;
    private bool _chordActive;
    private bool _capturing;

    public ChordKeyFilter(ScanKey chordKey)
    {
        ThrowIfInvalid(chordKey);
        ChordKey = chordKey;
    }

    public ScanKey ChordKey { get; private set; }

    public bool Enabled { get; private set; } = true;

    public bool ChordActive => _chordActive;

    public bool ShiftHeld => _leftShift || _rightShift;

    public bool Capturing => _capturing;

    /// <param name="captureAllowed">
    /// If false, a key that would be captured instead cancels the capture and is processed normally.
    /// </param>
    public KeyFilterResult Process(
        int scanCode, bool isExtended, bool isKeyDown, bool isInjected, bool captureAllowed = true)
    {
        if (isInjected)
            return KeyFilterResult.PassThrough;

        if (scanCode is 0x2A or 0x36)
        {
            if (!isExtended) // keyboards send extended "fake" shifts around nav keys; they aren't real presses
            {
                if (scanCode == 0x2A)
                    _leftShift = isKeyDown;
                else
                    _rightShift = isKeyDown;
            }

            return KeyFilterResult.PassThrough;
        }

        // Covers Windows' own simulated shifts around Shift+numpad, reported as e.g. scan code 0x22A.
        if (scanCode is <= 0 or > 0xFF)
            return KeyFilterResult.PassThrough;

        var slot = SlotOf(scanCode, isExtended);
        return isKeyDown
            ? KeyDown(slot, new ScanKey(scanCode, isExtended), captureAllowed)
            : KeyUp(slot, scanCode, isExtended);
    }

    /// <summary>The next fresh, non-injected key down (other than shift) is reported and hidden, with its up.</summary>
    public void BeginCapture() => _capturing = true;

    public void CancelCapture() => _capturing = false;

    /// <summary>Returns <see cref="ChordCancelled"/> if this ends an active chord.</summary>
    public ChordEvent? SetChordKey(ScanKey chordKey)
    {
        ThrowIfInvalid(chordKey);
        if (chordKey == ChordKey)
            return null;

        ChordKey = chordKey;
        return EndChord();
    }

    /// <summary>
    /// While disabled, keys pass through (though keys already hidden stay hidden through their up).
    /// Returns <see cref="ChordCancelled"/> if this ends an active chord.
    /// </summary>
    public ChordEvent? SetEnabled(bool enabled)
    {
        Enabled = enabled;
        return enabled ? null : EndChord();
    }

    /// <summary>
    /// Forgets key state after events may have been missed; if <paramref name="chordKeyDown"/>, other apps saw
    /// it go down, so its repeats and up pass through. Returns <see cref="ChordCancelled"/> if a chord was active.
    /// </summary>
    public ChordEvent? Reset(bool chordKeyDown = false)
    {
        Array.Clear(_held);
        Array.Clear(_swallowed);
        _leftShift = false;
        _rightShift = false;
        if (chordKeyDown)
            _held[SlotOf(ChordKey.ScanCode, ChordKey.IsExtended)] = true;

        return EndChord();
    }

    /// <summary>
    /// <see cref="Reset"/>s only if a chord is active, for when its release may have gone missing (focus
    /// switch); otherwise hidden keys stay hidden through their up.
    /// </summary>
    public ChordEvent? ResetIfChordActive(bool chordKeyDown) => _chordActive ? Reset(chordKeyDown) : null;

    public static void ThrowIfInvalid(ScanKey chordKey)
    {
        if (!chordKey.IsValidChordKey)
            throw new ArgumentException($"{chordKey} can't be the chord key.", nameof(chordKey));
    }

    private KeyFilterResult KeyDown(int slot, ScanKey key, bool captureAllowed)
    {
        if (_held[slot]) // auto-repeat: treat it like the original press
            return _swallowed[slot] ? KeyFilterResult.SwallowSilently : KeyFilterResult.PassThrough;

        _held[slot] = true;
        if (!_capturing)
            return FreshKeyDown(slot, key);

        _capturing = false;
        if (!captureAllowed)
            return FreshKeyDown(slot, key) with { CaptureEnded = true };

        _swallowed[slot] = true;
        return KeyFilterResult.Captured(key == ScanKey.Escape ? null : key);
    }

    private KeyFilterResult FreshKeyDown(int slot, ScanKey key)
    {
        if (!Enabled)
            return KeyFilterResult.PassThrough;

        if (key == ChordKey)
        {
            _swallowed[slot] = true;
            _chordActive = true;
            return new KeyFilterResult(true, Pressed);
        }

        var digit = key.Digit;
        if (_chordActive && digit >= 0)
        {
            _swallowed[slot] = true;
            return new KeyFilterResult(true, (ShiftHeld ? ShiftedDigits : Digits)[digit]);
        }

        if (_chordActive && key.IsEnter)
        {
            _swallowed[slot] = true;
            return new KeyFilterResult(true, Grab);
        }

        return KeyFilterResult.PassThrough;
    }

    private KeyFilterResult KeyUp(int slot, int scanCode, bool isExtended)
    {
        _held[slot] = false;
        if (!_swallowed[slot])
            return KeyFilterResult.PassThrough;

        _swallowed[slot] = false;
        if (_chordActive && scanCode == ChordKey.ScanCode && isExtended == ChordKey.IsExtended)
        {
            _chordActive = false;
            return new KeyFilterResult(true, Released);
        }

        return KeyFilterResult.SwallowSilently;
    }

    private ChordEvent? EndChord()
    {
        if (!_chordActive)
            return null;

        _chordActive = false;
        return Cancelled;
    }

    private static int SlotOf(int scanCode, bool isExtended) => scanCode | (isExtended ? 0x100 : 0);

    private static ChordEvent[] BuildDigitEvents(bool shift) =>
        Enumerable.Range(0, 10).Select(ChordEvent (d) => new DigitPressed(d, shift)).ToArray();
}

/// <summary>What the hook should do with one key event.</summary>
/// <param name="Swallow">Hide the event from every other application.</param>
/// <param name="Event">Chord input to forward to the navigator, if any.</param>
/// <param name="CaptureEnded">A pending capture finished with this event.</param>
/// <param name="CapturedKey">The captured key, or null if the capture was cancelled with Escape.</param>
public readonly record struct KeyFilterResult(
    bool Swallow, ChordEvent? Event = null, bool CaptureEnded = false, ScanKey? CapturedKey = null)
{
    public static readonly KeyFilterResult PassThrough = new(false);
    public static readonly KeyFilterResult SwallowSilently = new(true);

    public static KeyFilterResult Captured(ScanKey? key) => new(true, null, true, key);
}
