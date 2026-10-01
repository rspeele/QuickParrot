using QuickParrot.Core.Keyboard;

namespace QuickParrot.Core.Tests.Keyboard;

public class ScanKeyTests
{
    [Theory]
    [InlineData(0x3B, false, 1)]
    [InlineData(0x44, false, 10)]
    [InlineData(0x57, false, 11)]
    [InlineData(0x58, false, 12)]
    [InlineData(0x3B, true, 0)]
    [InlineData(0x45, false, 0)]
    [InlineData(0x64, false, 0)] // F13
    public void FunctionKeys(int scanCode, bool extended, int expected)
    {
        var key = new ScanKey(scanCode, extended);
        Assert.Equal(expected, key.FunctionKey);
        if (expected > 0)
            Assert.Equal(key, ScanKey.ForFunctionKey(expected));
    }

    [Theory]
    [InlineData(0x53, true, true)] // Delete
    [InlineData(0x0E, false, true)] // Backspace
    [InlineData(0x53, false, false)] // numpad "."
    [InlineData(0x0E, true, false)]
    public void ClearKeys(int scanCode, bool extended, bool expected) =>
        Assert.Equal(expected, new ScanKey(scanCode, extended).IsClearKey);

    [Theory]
    [InlineData(0x30, false, "B")]
    [InlineData(0x10, false, "Q")]
    [InlineData(0x32, false, "M")]
    [InlineData(0x0C, false, "-")]
    [InlineData(0x29, false, "Backtick")]
    [InlineData(0x02, false, "1")]
    [InlineData(0x0B, false, "0")]
    [InlineData(0x4C, false, "Numpad 5")]
    [InlineData(0x52, false, "Numpad 0")]
    [InlineData(0x47, true, "Home")]
    [InlineData(0x1D, false, "Left Ctrl")]
    [InlineData(0x1D, true, "Right Ctrl")]
    [InlineData(0x1C, true, "Numpad Enter")]
    [InlineData(0x3B, false, "F1")]
    [InlineData(0x44, false, "F10")]
    [InlineData(0x58, false, "F12")]
    [InlineData(0x64, false, "F13")]
    [InlineData(0x76, false, "F24")]
    [InlineData(0x30, true, "Volume Up")]
    [InlineData(0x7E, false, "Scan 0x7E")]
    [InlineData(0x7E, true, "Scan 0xE07E")]
    public void DisplayNames(int scanCode, bool extended, string expected)
    {
        Assert.Equal(expected, ScanKeyNames.GetDisplayName(new ScanKey(scanCode, extended)));
    }

    [Fact]
    public void AllDigitKeys_MapToDigits()
    {
        var digits = Enumerable.Range(0, 0x100)
            .Select(code => ScanKey.DigitOf(code, false))
            .Where(d => d >= 0)
            .Order();

        Assert.Equal([0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9], digits);
        Assert.All(Enumerable.Range(0, 0x100), code => Assert.Equal(-1, ScanKey.DigitOf(code, true)));
    }

    // Enter is the grab key within the chord, so it can't also be the chord key: pressing it would be ambiguous.
    [Theory]
    [InlineData(0x1C, false)]
    [InlineData(0x1C, true)]
    public void Enter_IsNeverAValidChordKey(int scanCode, bool extended)
    {
        Assert.False(new ScanKey(scanCode, extended).IsValidChordKey);
    }

    [Fact]
    public void DefaultChordKey_IsValid()
    {
        Assert.True(ScanKey.DefaultChordKey.IsValidChordKey);
    }
}
