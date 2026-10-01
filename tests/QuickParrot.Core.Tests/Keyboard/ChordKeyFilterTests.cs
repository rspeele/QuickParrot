using QuickParrot.Core.Keyboard;
using QuickParrot.Core.Navigation;

namespace QuickParrot.Core.Tests.Keyboard;

public class ChordKeyFilterTests
{
    private static readonly ScanKey B = ScanKey.DefaultChordKey;
    private static readonly ScanKey Minus = new(0x0C, false);
    private static readonly ScanKey One = new(0x02, false);
    private static readonly ScanKey Two = new(0x03, false);
    private static readonly ScanKey Zero = new(0x0B, false);
    private static readonly ScanKey V = new(0x2F, false);
    private static readonly ScanKey Numpad7 = new(0x47, false);
    private static readonly ScanKey Home = new(0x47, true);
    private static readonly ScanKey FakeLeftShift = new(0x2A, true);

    private readonly ChordKeyFilter _filter = new(B);

    private KeyFilterResult Down(ScanKey key, bool injected = false, bool captureAllowed = true) =>
        _filter.Process(key.ScanCode, key.IsExtended, isKeyDown: true, injected, captureAllowed);

    private KeyFilterResult Up(ScanKey key, bool injected = false) =>
        _filter.Process(key.ScanCode, key.IsExtended, isKeyDown: false, injected);

    private static void AssertSwallowed(KeyFilterResult result, ChordEvent? expected = null)
    {
        Assert.True(result.Swallow);
        Assert.Equal(expected, result.Event);
    }

    private static void AssertPassed(KeyFilterResult result) => Assert.Equal(KeyFilterResult.PassThrough, result);

    [Fact]
    public void ChordPressAndRelease_AreSwallowed_AndEmitted()
    {
        AssertSwallowed(Down(B), new ChordPressed());
        Assert.True(_filter.ChordActive);
        AssertSwallowed(Up(B), new ChordReleased());
        Assert.False(_filter.ChordActive);
    }

    [Fact]
    public void ChordAutoRepeat_IsSwallowed_WithoutEvents()
    {
        Down(B);
        AssertSwallowed(Down(B));
        AssertSwallowed(Down(B));
        AssertSwallowed(Up(B), new ChordReleased());
    }

    [Fact]
    public void Digits_WithoutChord_PassThrough()
    {
        AssertPassed(Down(One));
        AssertPassed(Up(One));
        AssertPassed(Down(Numpad7));
        AssertPassed(Up(Numpad7));
    }

    [Theory]
    [InlineData(0x02, 1)]
    [InlineData(0x06, 5)]
    [InlineData(0x0A, 9)]
    [InlineData(0x0B, 0)]
    [InlineData(0x4F, 1)]
    [InlineData(0x50, 2)]
    [InlineData(0x51, 3)]
    [InlineData(0x4B, 4)]
    [InlineData(0x4C, 5)]
    [InlineData(0x4D, 6)]
    [InlineData(0x47, 7)]
    [InlineData(0x48, 8)]
    [InlineData(0x49, 9)]
    [InlineData(0x52, 0)]
    public void DigitsDuringChord_AreSwallowed_AndEmitted(int scanCode, int digit)
    {
        var key = new ScanKey(scanCode, false);
        Down(B);

        AssertSwallowed(Down(key), new DigitPressed(digit, false));
        AssertSwallowed(Up(key));
    }

    [Theory]
    [InlineData(0x47)] // Home
    [InlineData(0x48)] // Up
    [InlineData(0x4B)] // Left
    [InlineData(0x4F)] // End
    [InlineData(0x52)] // Insert
    public void ExtendedNavKeysDuringChord_PassThrough(int scanCode)
    {
        var key = new ScanKey(scanCode, true);
        Down(B);

        AssertPassed(Down(key));
        AssertPassed(Up(key));
    }

    [Fact]
    public void OtherKeysDuringChord_PassThrough()
    {
        Down(B);

        AssertPassed(Down(V));
        AssertPassed(Up(V));
        Assert.True(_filter.ChordActive);
    }

    [Fact]
    public void DigitAutoRepeat_IsSwallowed_WithoutEvents()
    {
        Down(B);
        Down(One);

        AssertSwallowed(Down(One));
        AssertSwallowed(Down(One));
        AssertSwallowed(Up(One));
    }

    [Fact]
    public void RepeatedDigitPresses_EachEmit()
    {
        Down(B);
        Assert.Equal(new DigitPressed(0, false), Down(Zero).Event);
        Up(Zero);
        Assert.Equal(new DigitPressed(0, false), Down(Zero).Event);
        Up(Zero);
        Assert.Equal(new DigitPressed(2, false), Down(Two).Event);
    }

    [Fact]
    public void ChordReleasedBeforeDigit_DigitUpIsStillSwallowed()
    {
        Down(B);
        Down(One);

        AssertSwallowed(Up(B), new ChordReleased());
        AssertSwallowed(Down(One)); // auto-repeat after the chord ended
        AssertSwallowed(Up(One));
        AssertPassed(Down(One));
        AssertPassed(Up(One));
    }

    [Fact]
    public void DigitHeldBeforeChord_StaysVisibleToGame()
    {
        AssertPassed(Down(One));
        Down(B);

        AssertPassed(Down(One)); // auto-repeat of the press the game already saw
        AssertPassed(Up(One));
        AssertSwallowed(Down(One), new DigitPressed(1, false)); // a fresh press navigates
        AssertSwallowed(Up(One));
    }

    [Fact]
    public void ChordKeyHeldWhileDisabled_IsNotTakenOverWhenReenabled()
    {
        _filter.SetEnabled(false);
        AssertPassed(Down(B));
        _filter.SetEnabled(true);

        AssertPassed(Down(B));
        AssertPassed(Up(B));
        Assert.False(_filter.ChordActive);
        AssertSwallowed(Down(B), new ChordPressed());
    }

    [Fact]
    public void Shift_IsReportedWithDigits_AndAlwaysPassesThrough()
    {
        AssertPassed(Down(ScanKey.LeftShift));
        Down(B);

        AssertSwallowed(Down(One), new DigitPressed(1, true));
        Up(One);
        AssertPassed(Up(ScanKey.LeftShift));
        AssertSwallowed(Down(One), new DigitPressed(1, false));
    }

    [Fact]
    public void EitherShift_Counts_UntilBothReleased()
    {
        Down(ScanKey.LeftShift);
        Down(ScanKey.RightShift);
        Up(ScanKey.LeftShift);
        Assert.True(_filter.ShiftHeld);
        Up(ScanKey.RightShift);
        Assert.False(_filter.ShiftHeld);
    }

    [Fact]
    public void ShiftNumpad_IgnoresFakeExtendedShifts()
    {
        Down(ScanKey.LeftShift);
        Down(B);

        // With NumLock on, Windows wraps Shift+numpad in a synthesized shift release and re-press.
        AssertPassed(Up(FakeLeftShift));
        AssertSwallowed(Down(Numpad7), new DigitPressed(7, true));
        AssertPassed(Down(FakeLeftShift));
        AssertPassed(Up(FakeLeftShift));
        AssertSwallowed(Up(Numpad7));
        AssertPassed(Down(FakeLeftShift));

        Assert.True(_filter.ShiftHeld);
    }

    [Fact]
    public void ShiftNumpad_IgnoresWindowsSimulatedShifts()
    {
        Down(ScanKey.LeftShift);
        Down(B);

        // Observed from Windows 10: synthesized shifts carry scan code 0x22A and no extended flag.
        AssertPassed(_filter.Process(0x22A, false, isKeyDown: false, isInjected: false));
        AssertSwallowed(Down(Numpad7), new DigitPressed(7, true));
        AssertPassed(_filter.Process(0x22A, false, isKeyDown: true, isInjected: false));
        AssertPassed(_filter.Process(0x22A, false, isKeyDown: false, isInjected: false));
        AssertSwallowed(Up(Numpad7));
        AssertPassed(_filter.Process(0x22A, false, isKeyDown: true, isInjected: false));

        Assert.True(_filter.ShiftHeld);
        Up(ScanKey.LeftShift);
        Assert.False(_filter.ShiftHeld);
    }

    [Fact]
    public void FakeShiftDown_DoesNotSetShift()
    {
        Down(B);
        Down(FakeLeftShift);
        Down(new ScanKey(0x36, true));

        Assert.Equal(new DigitPressed(1, false), Down(One).Event);
    }

    [Fact]
    public void InjectedEvents_PassThrough_AndDoNotAffectState()
    {
        AssertPassed(Down(B, injected: true));
        Assert.False(_filter.ChordActive);

        Down(B);
        AssertPassed(Down(One, injected: true));
        AssertPassed(Down(ScanKey.LeftShift, injected: true));
        AssertPassed(Up(B, injected: true));
        Assert.True(_filter.ChordActive);
        Assert.False(_filter.ShiftHeld);

        AssertSwallowed(Down(One), new DigitPressed(1, false));
        AssertPassed(Up(One, injected: true));
        AssertSwallowed(Up(One));
        AssertSwallowed(Up(B), new ChordReleased());
    }

    [Fact]
    public void ChordKey_MatchesExtendedFlagExactly()
    {
        AssertPassed(Down(new ScanKey(0x30, true))); // Volume Up shares B's scan code
        Assert.False(_filter.ChordActive);
    }

    [Fact]
    public void CustomChordKey_Works_AndOldKeyPassesThrough()
    {
        var filter = new ChordKeyFilter(Minus);

        Assert.False(filter.Process(B.ScanCode, false, true, false).Swallow);
        Assert.Equal(new ChordPressed(), filter.Process(Minus.ScanCode, false, true, false).Event);
        Assert.Equal(new DigitPressed(7, false), filter.Process(0x47, false, true, false).Event);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0x100)]
    [InlineData(0xE030)]
    public void OutOfRangeScanCodes_PassThrough(int scanCode)
    {
        Down(B);

        Assert.Equal(KeyFilterResult.PassThrough, _filter.Process(scanCode, false, true, false));
    }

    [Fact]
    public void Reset_MidChord_CancelsWithoutRelease()
    {
        Down(B);
        Down(One);

        Assert.Equal(new ChordCancelled(), _filter.Reset());
        Assert.False(_filter.ChordActive);
        AssertPassed(Up(One)); // forgotten, so stray ups pass through
        AssertPassed(Up(B));
    }

    [Fact]
    public void Reset_WhileIdle_EmitsNothing_AndClearsShift()
    {
        Down(ScanKey.LeftShift);

        Assert.Null(_filter.Reset());
        Assert.False(_filter.ShiftHeld);
    }

    [Fact]
    public void Reset_ForgetsHeldKeys_SoNextPressIsFresh()
    {
        Down(B);
        Up(B, injected: true); // stands in for an up lost to the secure desktop
        _filter.Reset();

        AssertSwallowed(Down(B), new ChordPressed());
    }

    [Fact]
    public void Reset_WithChordKeyAlreadyDown_LetsItsRepeatsAndUpThrough()
    {
        Assert.Null(_filter.Reset(chordKeyDown: true));

        AssertPassed(Down(B)); // auto-repeat of a press other apps already saw
        AssertPassed(Down(B));
        AssertPassed(Up(B));
        Assert.False(_filter.ChordActive);
        AssertSwallowed(Down(B), new ChordPressed());
    }

    [Fact]
    public void Reset_WithExtendedChordKeyDown_MarksTheExtendedKey()
    {
        var filter = new ChordKeyFilter(Home);

        filter.Reset(chordKeyDown: true);

        Assert.Equal(KeyFilterResult.PassThrough, filter.Process(0x47, true, isKeyDown: true, isInjected: false));
        Assert.Equal(KeyFilterResult.PassThrough, filter.Process(0x47, true, isKeyDown: false, isInjected: false));
    }

    [Fact]
    public void Reset_MidChord_WithChordKeyDown_CancelsAndReleasesOwnership()
    {
        Down(B);

        Assert.Equal(new ChordCancelled(), _filter.Reset(chordKeyDown: true));
        AssertPassed(Down(B));
        AssertPassed(Up(B));
    }

    [Fact]
    public void ResetIfChordActive_MidChord_Cancels()
    {
        Down(B);
        Down(One);

        Assert.Equal(new ChordCancelled(), _filter.ResetIfChordActive(chordKeyDown: false));
        Assert.False(_filter.ChordActive);
        AssertPassed(Up(One));
        AssertSwallowed(Down(B), new ChordPressed()); // repeat of an owned press that other apps never saw
    }

    [Fact]
    public void ResetIfChordActive_WhenIdle_KeepsHiddenKeysHidden()
    {
        Down(B);
        Down(One);
        Up(B);

        Assert.Null(_filter.ResetIfChordActive(chordKeyDown: true));
        AssertSwallowed(Down(One));
        AssertSwallowed(Up(One));
        AssertSwallowed(Down(B), new ChordPressed());
    }

    [Fact]
    public void Disabled_PassesEverythingThrough()
    {
        Assert.Null(_filter.SetEnabled(false));

        AssertPassed(Down(B));
        AssertPassed(Down(One));
        AssertPassed(Up(One));
        AssertPassed(Up(B));
    }

    [Fact]
    public void DisablingMidChord_Cancels_ButKeepsHiddenKeysHiddenThroughTheirUp()
    {
        Down(B);
        Down(One);

        Assert.Equal(new ChordCancelled(), _filter.SetEnabled(false));
        AssertSwallowed(Down(One)); // auto-repeat
        AssertSwallowed(Up(One));
        AssertSwallowed(Up(B));
        AssertPassed(Down(B));
    }

    [Fact]
    public void ChangingChordKeyMidChord_Cancels_AndOldKeyUpStaysHidden()
    {
        Down(B);

        Assert.Equal(new ChordCancelled(), _filter.SetChordKey(Minus));
        AssertSwallowed(Up(B));
        AssertPassed(Down(B));
        AssertSwallowed(Down(Minus), new ChordPressed());
    }

    [Fact]
    public void SettingSameChordKey_DoesNothing()
    {
        Down(B);

        Assert.Null(_filter.SetChordKey(B));
        Assert.True(_filter.ChordActive);
    }

    [Theory]
    [InlineData(0x02, false)]
    [InlineData(0x0B, false)]
    [InlineData(0x47, false)]
    [InlineData(0x52, false)]
    [InlineData(0x2A, false)]
    [InlineData(0x36, false)]
    [InlineData(0x2A, true)]
    [InlineData(0x01, false)]
    [InlineData(0x1D, false)] // Left Ctrl
    [InlineData(0x1D, true)] // Right Ctrl
    [InlineData(0x38, false)] // Left Alt
    [InlineData(0x38, true)] // Right Alt
    [InlineData(0x5B, true)] // Left Windows
    [InlineData(0x5C, true)] // Right Windows
    [InlineData(0, false)]
    [InlineData(0x100, false)]
    public void InvalidChordKeys_AreRejected(int scanCode, bool extended)
    {
        var key = new ScanKey(scanCode, extended);

        Assert.False(key.IsValidChordKey);
        Assert.Throws<ArgumentException>(() => new ChordKeyFilter(key));
        Assert.Throws<ArgumentException>(() => _filter.SetChordKey(key));
        Assert.Equal(B, _filter.ChordKey);
    }

    [Theory]
    [InlineData(0x30, false)]
    [InlineData(0x0C, false)]
    [InlineData(0x47, true)] // Home: extended numpad position is not a digit
    [InlineData(0x52, true)] // Insert
    [InlineData(0x29, false)]
    [InlineData(0x3A, false)]
    [InlineData(0x5D, true)] // Menu
    public void ValidChordKeys_AreAccepted(int scanCode, bool extended)
    {
        Assert.True(new ScanKey(scanCode, extended).IsValidChordKey);
    }

    [Fact]
    public void Capture_ReportsAndHidesNextKey_InsteadOfChording()
    {
        _filter.BeginCapture();

        var result = Down(B);

        Assert.Equal(KeyFilterResult.Captured(B), result);
        Assert.True(result.Swallow);
        Assert.False(_filter.ChordActive);
        Assert.False(_filter.Capturing);
        AssertSwallowed(Down(B)); // auto-repeat
        AssertSwallowed(Up(B));
        AssertSwallowed(Down(B), new ChordPressed()); // back to normal
    }

    [Fact]
    public void Capture_Escape_Cancels()
    {
        _filter.BeginCapture();

        var result = Down(ScanKey.Escape);

        Assert.True(result.CaptureEnded);
        Assert.Null(result.CapturedKey);
        Assert.True(result.Swallow);
        AssertSwallowed(Up(ScanKey.Escape));
    }

    [Fact]
    public void Capture_IgnoresShiftInjectedAndAutoRepeat()
    {
        Down(V);
        _filter.BeginCapture();

        AssertPassed(Down(ScanKey.LeftShift));
        AssertPassed(Down(Minus, injected: true));
        AssertPassed(Down(V)); // repeat of a key held before the capture began
        AssertPassed(Up(V));
        Assert.True(_filter.Capturing);

        Assert.Equal(KeyFilterResult.Captured(Home), Down(Home));
    }

    [Fact]
    public void Capture_WorksWhileDisabled_AndCanBeCancelled()
    {
        _filter.SetEnabled(false);
        _filter.BeginCapture();
        Assert.Equal(KeyFilterResult.Captured(One), Down(One));

        _filter.BeginCapture();
        _filter.CancelCapture();
        AssertPassed(Down(Two));
    }

    [Fact]
    public void Capture_NotAllowed_CancelsAndProcessesTheKeyNormally()
    {
        _filter.BeginCapture();

        var result = Down(V, captureAllowed: false);

        Assert.Equal(new KeyFilterResult(false, null, CaptureEnded: true), result);
        Assert.False(_filter.Capturing);
        AssertPassed(Up(V));
    }

    [Fact]
    public void Capture_NotAllowed_ChordKeyStillChords()
    {
        _filter.BeginCapture();

        Assert.Equal(new KeyFilterResult(true, new ChordPressed(), CaptureEnded: true), Down(B, captureAllowed: false));
        AssertSwallowed(Up(B), new ChordReleased());
    }

    [Fact]
    public void Capture_NotAllowed_OnlyAffectsCandidateKeys()
    {
        Down(V);
        _filter.BeginCapture();

        AssertPassed(Down(ScanKey.LeftShift, captureAllowed: false));
        AssertPassed(Down(Minus, injected: true, captureAllowed: false));
        AssertPassed(Down(V, captureAllowed: false)); // auto-repeat
        Assert.True(_filter.Capturing);
    }

    [Fact]
    public void Capture_DuringChord_TakesTheKey_ChordContinues()
    {
        Down(B);
        _filter.BeginCapture();

        Assert.Equal(KeyFilterResult.Captured(One), Down(One));
        AssertSwallowed(Up(One));
        AssertSwallowed(Down(Two), new DigitPressed(2, false));
        AssertSwallowed(Up(B), new ChordReleased());
    }

    [Fact]
    public void Process_DoesNotAllocate()
    {
        Down(B); // warm up
        Up(B);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            Down(ScanKey.LeftShift);
            Down(B);
            Down(Numpad7);
            Up(Numpad7);
            Up(B);
            Up(ScanKey.LeftShift);
        }

        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }
}
