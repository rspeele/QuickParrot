using QuickParrot.Core.Keyboard;
using QuickParrot.Core.Navigation;

namespace QuickParrot.Core.Tests.Keyboard;

public class ChordKeyFilterFavoritesTests
{
    private static readonly ScanKey B = ScanKey.DefaultChordKey;
    private static readonly ScanKey F1 = ScanKey.ForFunctionKey(1);
    private static readonly ScanKey F3 = ScanKey.ForFunctionKey(3);
    private static readonly ScanKey F5 = ScanKey.ForFunctionKey(5);
    private static readonly ScanKey F12 = ScanKey.ForFunctionKey(12);
    private static readonly ScanKey Delete = new(0x53, true);
    private static readonly ScanKey NumpadDot = new(0x53, false);
    private static readonly ScanKey Backspace = new(0x0E, false);
    private static readonly ScanKey LeftShift = ScanKey.LeftShift;

    private bool _gameFocused = true;
    private int _focusChecks;
    private readonly ChordKeyFilter _filter;

    public ChordKeyFilterFavoritesTests()
    {
        _filter = new ChordKeyFilter(B, () =>
        {
            _focusChecks++;
            return _gameFocused;
        });
    }

    private KeyFilterResult Down(ScanKey key) => _filter.Process(key.ScanCode, key.IsExtended, isKeyDown: true, isInjected: false);

    private KeyFilterResult Up(ScanKey key) => _filter.Process(key.ScanCode, key.IsExtended, isKeyDown: false, isInjected: false);

    private static void AssertSwallowed(KeyFilterResult result, ChordEvent? expected = null)
    {
        Assert.True(result.Swallow);
        Assert.Equal(expected, result.Event);
    }

    private static void AssertPassed(KeyFilterResult result) => Assert.Equal(KeyFilterResult.PassThrough, result);

    [Theory]
    [InlineData(0x3B, 1)]
    [InlineData(0x40, 6)]
    [InlineData(0x44, 10)]
    [InlineData(0x57, 11)]
    [InlineData(0x58, 12)]
    public void FKeysDuringChord_AreSwallowed_AndEmitted(int scanCode, int slot)
    {
        var key = new ScanKey(scanCode, false);
        Down(B);

        AssertSwallowed(Down(key), new FavoritePressed(slot, false));
        AssertSwallowed(Up(key));
    }

    [Fact]
    public void ShiftFKeyDuringChord_EmitsShifted()
    {
        Down(B);
        Down(LeftShift);

        AssertSwallowed(Down(F3), new FavoritePressed(3, true));
    }

    [Fact]
    public void FKeyAutoRepeat_IsSwallowedWithoutRepeatingTheEvent()
    {
        Down(B);
        Down(F3);

        AssertSwallowed(Down(F3));
        AssertSwallowed(Down(F3));
        AssertSwallowed(Up(F3));
    }

    [Fact]
    public void FKeyReleasedAfterTheChord_StillHasItsUpHidden()
    {
        Down(B);
        Down(F3);
        AssertSwallowed(Up(B), new ChordReleased());

        AssertSwallowed(Up(F3));
        AssertPassed(Down(F3)); // a fresh press without the chord is the game's again
    }

    [Fact]
    public void FKeysWithoutChord_PassThroughByDefault()
    {
        AssertPassed(Down(F3));
        AssertPassed(Up(F3));
        Assert.Equal(0, _focusChecks);
    }

    [Fact]
    public void ChordKeyAsAnFKey_IsTheChordNotAFavorite()
    {
        var filter = new ChordKeyFilter(F3, () => true) { ChordlessFavoriteSlots = 0b100 };

        AssertSwallowed(filter.Process(F3.ScanCode, false, true, false), new ChordPressed());
        AssertSwallowed(filter.Process(F5.ScanCode, false, true, false), new FavoritePressed(5, false));
    }

    [Fact]
    public void PushToTalkFKey_IsLeftForTheGame_EvenDuringChord()
    {
        _filter.PushToTalkKey = F5;
        _filter.ChordlessFavoriteSlots = 0b1_0000;

        AssertPassed(Down(F5));
        AssertPassed(Up(F5));
        Down(B);
        AssertPassed(Down(F5));
        AssertPassed(Up(F5));
    }

    [Theory]
    [InlineData(0x53, true)]
    [InlineData(0x0E, false)]
    public void ClearKeysDuringChord_AreSwallowed_AndEmitted(int scanCode, bool extended)
    {
        var key = new ScanKey(scanCode, extended);
        Down(B);

        AssertSwallowed(Down(key), new FavoriteClearPressed());
        AssertSwallowed(Down(key)); // auto-repeat
        AssertSwallowed(Up(key));
    }

    [Fact]
    public void NumpadDot_IsNotDelete()
    {
        Down(B);

        AssertPassed(Down(NumpadDot));
        AssertPassed(Up(NumpadDot));
    }

    [Fact]
    public void ClearKeysWithoutChord_PassThrough()
    {
        AssertPassed(Down(Delete));
        AssertPassed(Down(Backspace));
    }

    [Fact]
    public void ChordlessFKey_WithAClipInAGame_IsSwallowedAndEmitted()
    {
        _filter.ChordlessFavoriteSlots = 1 << 11;

        AssertSwallowed(Down(F12), new ChordlessFavoritePressed(12));
        AssertSwallowed(Down(F12)); // auto-repeat
        AssertSwallowed(Up(F12));
        Assert.Equal(1, _focusChecks);
    }

    [Fact]
    public void ChordlessFKey_IgnoresShift()
    {
        _filter.ChordlessFavoriteSlots = 0b100;
        Down(LeftShift);

        AssertSwallowed(Down(F3), new ChordlessFavoritePressed(3));
    }

    [Theory]
    [InlineData(0x38, false)] // Alt+F4 must still close the game
    [InlineData(0x38, true)]
    [InlineData(0x1D, false)]
    [InlineData(0x1D, true)]
    [InlineData(0x5B, true)]
    public void ChordlessFKey_WithCtrlAltOrWin_PassesThrough(int scanCode, bool extended)
    {
        var modifier = new ScanKey(scanCode, extended);
        _filter.ChordlessFavoriteSlots = 0b1000;
        Down(modifier);

        AssertPassed(Down(ScanKey.ForFunctionKey(4)));
        AssertPassed(Up(ScanKey.ForFunctionKey(4)));
        Up(modifier);
        AssertSwallowed(Down(ScanKey.ForFunctionKey(4)), new ChordlessFavoritePressed(4));
    }

    [Fact]
    public void ChordlessFKey_MaskClearedMidPress_KeepsItsUpHidden()
    {
        _filter.ChordlessFavoriteSlots = 0b100;
        Down(F3);
        _filter.ChordlessFavoriteSlots = 0;

        AssertSwallowed(Up(F3));
        AssertPassed(Down(F3));
    }

    [Fact]
    public void ChordlessFKey_Injected_PassesThroughWithoutCheckingFocus()
    {
        _filter.ChordlessFavoriteSlots = 0b100;

        AssertPassed(_filter.Process(F3.ScanCode, false, isKeyDown: true, isInjected: true));
        Assert.Equal(0, _focusChecks);
    }

    [Fact]
    public void ChordlessFKey_SurvivesAFocusResetOutsideAChord()
    {
        _filter.ChordlessFavoriteSlots = 0b100;
        Down(F3);

        Assert.Null(_filter.ResetIfChordActive(chordKeyDown: false));
        AssertSwallowed(Up(F3));
    }

    [Fact]
    public void ChordlessFKey_WithoutAClip_PassesThroughWithoutCheckingFocus()
    {
        _filter.ChordlessFavoriteSlots = 0b100;

        AssertPassed(Down(F1));
        AssertPassed(Up(F1));
        Assert.Equal(0, _focusChecks);
    }

    [Fact]
    public void ChordlessFKey_OutsideAGame_PassesThrough()
    {
        _filter.ChordlessFavoriteSlots = 0b100;
        _gameFocused = false;

        AssertPassed(Down(F3));
        AssertPassed(Down(F3)); // auto-repeat isn't re-checked
        AssertPassed(Up(F3));
        Assert.Equal(1, _focusChecks);
    }

    [Fact]
    public void ChordlessFKey_SwallowedThenGameLosesFocus_KeepsItsUpHidden()
    {
        _filter.ChordlessFavoriteSlots = 0b100;
        Down(F3);
        _gameFocused = false;

        AssertSwallowed(Up(F3));
    }

    [Fact]
    public void ChordlessFKeys_PassThroughWhileDisabled()
    {
        _filter.ChordlessFavoriteSlots = 0b100;
        _filter.SetEnabled(false);

        AssertPassed(Down(F3));
        Assert.Equal(0, _focusChecks);
    }

    [Fact]
    public void FKeyHeldWhenChordStarts_IsntSwallowedOnItsRepeatOrUp()
    {
        AssertPassed(Down(F3));
        Down(B);

        AssertPassed(Down(F3)); // auto-repeat of a key other apps saw go down
        AssertPassed(Up(F3));
    }
}
