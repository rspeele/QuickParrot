using System.Text.Json.Serialization;

namespace QuickParrot.Core.Keyboard;

/// <summary>A physical key: set-1 scan code plus the E0 "extended" flag, independent of layout and NumLock.</summary>
public readonly record struct ScanKey(int ScanCode, bool IsExtended)
{
    public static readonly ScanKey DefaultChordKey = new(0x4A, false);
    public static readonly ScanKey DefaultSaveNavigationKey = new(0x37, false);
    public static readonly ScanKey DefaultSearchKey = new(0x35, true);
    public static readonly ScanKey Escape = new(0x01, false);
    internal static readonly ScanKey LeftShift = new(0x2A, false);
    internal static readonly ScanKey RightShift = new(0x36, false);
    internal static readonly ScanKey Enter = new(0x1C, false);
    internal static readonly ScanKey NumpadEnter = new(0x1C, true);

    /// <summary>0-9 for number-row and numpad digit keys (the latter by position, so NumLock doesn't matter); else -1.</summary>
    [JsonIgnore]
    public int Digit => DigitOf(ScanCode, IsExtended);

    /// <summary>Main or numpad Enter.</summary>
    [JsonIgnore]
    public bool IsEnter => ScanCode == 0x1C;

    /// <summary>Either shift scan code, including the extended "fake shift" variants.</summary>
    [JsonIgnore]
    public bool IsShift => ScanCode is 0x2A or 0x36;

    /// <summary>1-12 for F1-F12; else 0.</summary>
    [JsonIgnore]
    public int FunctionKey => FunctionKeyOf(ScanCode, IsExtended);

    /// <summary>Delete (not numpad ".") or Backspace: clears a favorite while assigning.</summary>
    [JsonIgnore]
    public bool IsClearKey => IsClearKeyCode(ScanCode, IsExtended);

    private static int FunctionKeyOf(int scanCode, bool isExtended) => isExtended ? 0 : scanCode switch
    {
        >= 0x3B and <= 0x44 => scanCode - 0x3A,
        0x57 => 11,
        0x58 => 12,
        _ => 0,
    };

    private static bool IsClearKeyCode(int scanCode, bool isExtended) =>
        isExtended ? scanCode == 0x53 : scanCode == 0x0E;

    /// <summary>The physical key for F1-F12.</summary>
    public static ScanKey ForFunctionKey(int number) => number switch
    {
        >= 1 and <= 10 => new ScanKey(0x3A + number, false),
        11 => new ScanKey(0x57, false),
        12 => new ScanKey(0x58, false),
        _ => throw new ArgumentOutOfRangeException(nameof(number)),
    };

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

    [JsonIgnore]
    public bool IsValidSaveNavigationKey => IsValidChordKey && FunctionKey == 0 && !IsClearKey;

    [JsonIgnore]
    public bool IsValidSearchKey => IsValidSaveNavigationKey;

    /// <summary>Either Ctrl, Alt or Windows key.</summary>
    [JsonIgnore]
    public bool IsSystemModifier => ScanCode is 0x1D or 0x38 || IsWindowsKey;

    [JsonIgnore]
    public bool IsWindowsKey => IsExtended && ScanCode is 0x5B or 0x5C;

    public override string ToString() => ScanKeyNames.GetDisplayName(this);
}
