using QuickParrot.Core.Navigation;

namespace QuickParrot.Core.Keyboard;

/// <summary>
/// Turns raw key events into <see cref="ChordEvent"/>s and hides keys pressed while the chord is held (the chord key
/// itself passes through). Runs inside the low-level hook, so it never allocates per event. Not thread-safe.
/// </summary>
public sealed class ChordKeyFilter
{
    private const int SlotCount = 512; // scan codes 0-255, doubled for the extended flag

    private static readonly ChordEvent Pressed = new ChordPressed();
    private static readonly ChordEvent Released = new ChordReleased();
    private static readonly ChordEvent Cancelled = new ChordCancelled();
    private static readonly ChordEvent Grab = new GrabPressed();
    private static readonly ChordEvent Clear = new FavoriteClearPressed();
    private static readonly ChordEvent[] Digits = BuildDigitEvents(shift: false);
    private static readonly ChordEvent[] ShiftedDigits = BuildDigitEvents(shift: true);
    private static readonly ChordEvent[] Favorites = BuildFavoriteEvents(slot => new FavoritePressed(slot, false));
    private static readonly ChordEvent[] ShiftedFavorites = BuildFavoriteEvents(slot => new FavoritePressed(slot, true));
    private static readonly ChordEvent[] ChordlessFavorites = BuildFavoriteEvents(slot => new ChordlessFavoritePressed(slot));

    private readonly Func<bool> _isGameFocused;

    // Held tracks every physical key so auto-repeat can be told apart from a fresh press; swallowed marks
    // keys whose down was hidden, so their up is hidden too and other apps never see an unmatched event.
    private readonly bool[] _held = new bool[SlotCount];
    private readonly bool[] _swallowed = new bool[SlotCount];
    private bool _leftShift;
    private bool _rightShift;
    private bool _chordActive;
    private bool _capturing;

    /// <param name="isGameFocused">
    /// Whether a fullscreen game has focus; asked only when a plain F-key with a chordless favorite goes down.
    /// </param>
    public ChordKeyFilter(ScanKey chordKey, Func<bool>? isGameFocused = null)
    {
        ThrowIfInvalid(chordKey);
        ChordKey = chordKey;
        _isGameFocused = isGameFocused ?? (() => false);
    }

    public ScanKey ChordKey { get; private set; }

    /// <summary>Left alone even while the chord is held, so the game always sees push-to-talk.</summary>
    public ScanKey? PushToTalkKey { get; set; }

    /// <summary>Bit (n - 1) set: plain Fn, without the chord, plays favorite n while a game is focused.</summary>
    public int ChordlessFavoriteSlots { get; set; }

    public bool Enabled { get; private set; } = true;

    public bool ChordActive => _chordActive;

    public bool ShiftHeld => _leftShift || _rightShift;

    public bool Capturing => _capturing;

    // Left/right Ctrl and Alt, and both Windows keys, by slot.
    private bool SystemModifierHeld =>
        _held[0x1D] || _held[0x11D] || _held[0x38] || _held[0x138] || _held[0x15B] || _held[0x15C];

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
        var key = new ScanKey(scanCode, isExtended);
        return isKeyDown ? KeyDown(slot, key, captureAllowed) : KeyUp(slot, key);
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
    /// Forgets key state after events may have been missed; if <paramref name="chordKeyDown"/>, it's still
    /// physically held, so its repeats don't start a chord. Returns <see cref="ChordCancelled"/> if a chord was active.
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
            _chordActive = true;
            return new KeyFilterResult(false, Pressed);
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

        if (key == PushToTalkKey)
            return KeyFilterResult.PassThrough;

        // Delete and Backspace only mean something in assign mode, but every chord-held key belongs to QuickParrot.
        if (_chordActive && key.IsClearKey)
        {
            _swallowed[slot] = true;
            return new KeyFilterResult(true, Clear);
        }

        var favorite = key.FunctionKey;
        if (favorite == 0)
            return KeyFilterResult.PassThrough;

        if (_chordActive)
        {
            _swallowed[slot] = true;
            return new KeyFilterResult(true, (ShiftHeld ? ShiftedFavorites : Favorites)[favorite - 1]);
        }

        // Only plain F-keys: Alt+F4 and other system shortcuts must keep working in the game.
        if ((ChordlessFavoriteSlots & (1 << (favorite - 1))) == 0 || SystemModifierHeld || !_isGameFocused())
            return KeyFilterResult.PassThrough;

        _swallowed[slot] = true;
        return new KeyFilterResult(true, ChordlessFavorites[favorite - 1]);
    }

    private KeyFilterResult KeyUp(int slot, ScanKey key)
    {
        _held[slot] = false;
        var swallow = _swallowed[slot];
        _swallowed[slot] = false;
        if (_chordActive && key == ChordKey)
        {
            _chordActive = false;
            return new KeyFilterResult(swallow, Released);
        }

        return swallow ? KeyFilterResult.SwallowSilently : KeyFilterResult.PassThrough;
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

    private static ChordEvent[] BuildFavoriteEvents(Func<int, ChordEvent> create) =>
        Enumerable.Range(1, 12).Select(create).ToArray();
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
