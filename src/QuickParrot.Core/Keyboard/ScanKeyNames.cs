using System.Collections.Frozen;

namespace QuickParrot.Core.Keyboard;

/// <summary>Display names for physical keys, named after their US-layout legend.</summary>
public static class ScanKeyNames
{
    private static readonly FrozenDictionary<ScanKey, string> Names = BuildNames();

    public static string GetDisplayName(ScanKey key) =>
        Names.TryGetValue(key, out var name)
            ? name
            : key.IsExtended ? $"Scan 0xE0{key.ScanCode:X2}" : $"Scan 0x{key.ScanCode:X2}";

    private static FrozenDictionary<ScanKey, string> BuildNames()
    {
        var names = new Dictionary<ScanKey, string>();

        void Add(int scanCode, string name) => names[new ScanKey(scanCode, false)] = name;
        void AddExtended(int scanCode, string name) => names[new ScanKey(scanCode, true)] = name;

        Add(0x01, "Escape");
        for (var scanCode = 0x02; scanCode <= 0x0B; scanCode++)
            Add(scanCode, ScanKey.DigitOf(scanCode, false).ToString());
        Add(0x0C, "-");
        Add(0x0D, "=");
        Add(0x0E, "Backspace");
        Add(0x0F, "Tab");
        AddRow(0x10, "QWERTYUIOP");
        Add(0x1A, "[");
        Add(0x1B, "]");
        Add(0x1C, "Enter");
        Add(0x1D, "Left Ctrl");
        AddRow(0x1E, "ASDFGHJKL");
        Add(0x27, ";");
        Add(0x28, "'");
        Add(0x29, "Backtick");
        Add(0x2A, "Left Shift");
        Add(0x2B, "\\");
        AddRow(0x2C, "ZXCVBNM");
        Add(0x33, ",");
        Add(0x34, ".");
        Add(0x35, "/");
        Add(0x36, "Right Shift");
        Add(0x37, "Numpad *");
        Add(0x38, "Left Alt");
        Add(0x39, "Space");
        Add(0x3A, "Caps Lock");
        for (var f = 1; f <= 10; f++)
            Add(0x3A + f, $"F{f}");
        Add(0x45, "Pause");
        Add(0x46, "Scroll Lock");
        Add(0x4A, "Numpad -");
        Add(0x4E, "Numpad +");
        Add(0x53, "Numpad .");
        Add(0x56, "\\ (ISO)");
        Add(0x57, "F11");
        Add(0x58, "F12");
        for (var f = 13; f <= 23; f++)
            Add(0x64 + f - 13, $"F{f}");
        Add(0x76, "F24");

        for (var scanCode = 0x47; scanCode <= 0x52; scanCode++)
        {
            if (ScanKey.DigitOf(scanCode, false) is var digit and >= 0)
                Add(scanCode, $"Numpad {digit}");
        }

        AddExtended(0x10, "Previous Track");
        AddExtended(0x19, "Next Track");
        AddExtended(0x1C, "Numpad Enter");
        AddExtended(0x1D, "Right Ctrl");
        AddExtended(0x20, "Mute");
        AddExtended(0x22, "Play/Pause");
        AddExtended(0x24, "Stop");
        AddExtended(0x2E, "Volume Down");
        AddExtended(0x30, "Volume Up");
        AddExtended(0x35, "Numpad /");
        AddExtended(0x37, "Print Screen");
        AddExtended(0x38, "Right Alt");
        AddExtended(0x45, "Num Lock");
        AddExtended(0x46, "Break");
        AddExtended(0x47, "Home");
        AddExtended(0x48, "Up");
        AddExtended(0x49, "Page Up");
        AddExtended(0x4B, "Left");
        AddExtended(0x4D, "Right");
        AddExtended(0x4F, "End");
        AddExtended(0x50, "Down");
        AddExtended(0x51, "Page Down");
        AddExtended(0x52, "Insert");
        AddExtended(0x53, "Delete");
        AddExtended(0x5B, "Left Windows");
        AddExtended(0x5C, "Right Windows");
        AddExtended(0x5D, "Menu");

        return names.ToFrozenDictionary();

        void AddRow(int firstScanCode, string letters)
        {
            for (var i = 0; i < letters.Length; i++)
                Add(firstScanCode + i, letters[i].ToString());
        }
    }
}
