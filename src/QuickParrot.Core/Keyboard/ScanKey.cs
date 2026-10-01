using System.Text.Json.Serialization;

namespace QuickParrot.Core.Keyboard;

/// <summary>A physical key: set-1 scan code plus the E0 "extended" flag, independent of layout and NumLock.</summary>
public readonly record struct ScanKey(int ScanCode, bool IsExtended)
{
    public static readonly ScanKey DefaultChordKey = new(0x30, false); // B
    public static readonly ScanKey Escape = new(0x01, false);
    public static readonly ScanKey LeftShift = new(0x2A, false);
    public static readonly ScanKey RightShift = new(0x36, false);
    public static readonly ScanKey Enter = new(0x1C, false);
    public static readonly ScanKey NumpadEnter = new(0x1C, true);

    /// <summary>0-9 for number-row and numpad digit keys (the latter by position, so NumLock doesn't matter); else -1.</summary>
    [JsonIgnore]
    public int Digit => DigitOf(ScanCode, IsExtended);

    /// <summary>Main or numpad Enter.</summary>
    [JsonIgnore]
    public bool IsEnter => ScanCode == 0x1C;

    /// <summary>Either shift scan code, including the extended "fake shift" variants.</summary>
    [JsonIgnore]
    public bool IsShift => ScanCode is 0x2A or 0x36;

    public static int DigitOf(int scanCode, bool isExtended)
    {
        if (isExtended)
            return -1; // extended numpad positions are the dedicated arrow/Home/End/Ins keys

        return scanCode switch
        {
            >= 0x02 and <= 0x0A => scanCode - 1,
            0x0B => 0,
            0x4F => 1,
            0x50 => 2,
            0x51 => 3,
            0x4B => 4,
            0x4C => 5,
            0x4D => 6,
            0x47 => 7,
            0x48 => 8,
            0x49 => 9,
            0x52 => 0,
            _ => -1,
        };
    }

    /// <summary>
    /// Whether this key can be the chord key. Digits and shift are used within the chord, Enter is the grab key
    /// within it, Escape cancels key capture, and hiding Ctrl, Alt or Windows would break system shortcuts, so none
    /// of them can be.
    /// </summary>
    [JsonIgnore]
    public bool IsValidChordKey =>
        ScanCode is > 0 and <= 0xFF && Digit < 0 && !IsShift && !IsSystemModifier && !IsEnter && this != Escape;

    /// <summary>Either Ctrl, Alt or Windows key.</summary>
    [JsonIgnore]
    public bool IsSystemModifier => ScanCode is 0x1D or 0x38 || (IsExtended && ScanCode is 0x5B or 0x5C);

    public override string ToString() => ScanKeyNames.GetDisplayName(this);
}
