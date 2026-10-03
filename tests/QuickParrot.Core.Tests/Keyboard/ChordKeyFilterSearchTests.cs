using QuickParrot.Core.Keyboard;

namespace QuickParrot.Core.Tests.Keyboard;

public sealed class ChordKeyFilterSearchTests
{
    private static readonly ScanKey B = new(0x30, false);
    private static readonly ScanKey V = new(0x2F, false);
    private readonly ChordKeyFilter _filter = new(B) { PushToTalkKey = V };

    [Fact]
    public void SearchKeyRequiresChordAndInitialReleasePassesThenChordBecomesText()
    {
        Assert.False(Down(ScanKey.DefaultSearchKey).Swallow);
        Up(ScanKey.DefaultSearchKey);
        Start();
        Assert.Null(Down(B).Event);
        Assert.Equal(new ChordReleased(), Up(B).Event);
        Assert.True(_filter.SearchActive);
        Assert.Equal(new SearchKeyPressed(B), Down(B).Event);
        Assert.True(Up(B).Swallow);
        Assert.Equal(new SearchKeyPressed(B), Down(B).Event);
    }

    [Fact]
    public void TriggerRepeatIsIgnoredButFreshSearchKeyCanBeTyped()
    {
        Start();
        Assert.Null(Down(ScanKey.DefaultSearchKey).Event);
        Assert.True(Up(ScanKey.DefaultSearchKey).Swallow);
        Assert.Equal(new SearchKeyPressed(ScanKey.DefaultSearchKey), Down(ScanKey.DefaultSearchKey).Event);
    }

    [Theory]
    [InlineData(0x02, false, 1)]
    [InlineData(0x49, false, 9)]
    [InlineData(0x1C, true, 1)]
    [InlineData(0x1C, false, 1)]
    public void SelectionEndsImmediatelyAndHidesItsUpAndRepeats(int scanCode, bool extended, int number)
    {
        Start();
        Up(B);
        var key = new ScanKey(scanCode, extended);
        Assert.Equal(new SearchSelectionPressed(number), Down(key).Event);
        Assert.False(_filter.SearchActive);
        Assert.True(Down(key).Swallow);
        Assert.Null(Down(key).Event);
        Assert.True(Up(key).Swallow);
        Assert.False(Down(V).Swallow);
    }

    [Fact]
    public void PreviouslyHeldNavigationDigitDoesNotSelectOnRepeat()
    {
        Down(B);
        var one = new ScanKey(0x02, false);
        Down(one);
        Down(ScanKey.DefaultSearchKey);
        Assert.Null(Down(one).Event);
        Assert.True(_filter.SearchActive);
        Up(one);
        Assert.Equal(new SearchSelectionPressed(1), Down(one).Event);
    }

    [Fact]
    public void SearchSwallowsNewPushToTalkButPreservesPreviouslyPassedRelease()
    {
        Assert.False(Down(V).Swallow);
        Start();
        Assert.False(Up(V).Swallow);
        Assert.Equal(new SearchKeyPressed(V), Down(V).Event);
        Assert.True(Up(V).Swallow);
    }

    [Fact]
    public void TextAndBackspaceRepeatWhileZeroIsIgnored()
    {
        Start();
        Up(B);
        Assert.Equal(new SearchKeyPressed(B), Down(B).Event);
        Assert.Equal(new SearchKeyPressed(B), Down(B).Event);
        var backspace = new ScanKey(0x0E, false);
        Assert.IsType<SearchBackspacePressed>(Down(backspace).Event);
        Assert.IsType<SearchBackspacePressed>(Down(backspace).Event);
        var zero = new ScanKey(0x0B, false);
        Assert.True(Down(zero).Swallow);
        Assert.Null(Down(zero).Event);
    }

    [Fact]
    public void EscapeCancelsAndAlreadySwallowedUpsStayHidden()
    {
        Start();
        Up(B);
        Down(B);
        Assert.IsType<ChordCancelled>(Down(ScanKey.Escape).Event);
        Assert.False(_filter.SearchActive);
        Assert.True(Up(B).Swallow);
        Assert.True(Up(ScanKey.Escape).Swallow);
        Assert.True(Up(ScanKey.DefaultSearchKey).Swallow);
        Assert.IsType<ChordPressed>(Down(B).Event);
    }

    [Fact]
    public void ShiftPressedInSearchIsHiddenThroughReleaseAfterSelection()
    {
        Start();
        Up(B);
        var shift = new ScanKey(0x2A, false);
        Assert.True(Down(shift).Swallow);
        Down(new ScanKey(0x02, false));
        Assert.True(Up(shift).Swallow);
        Assert.False(_filter.ShiftHeld);
    }

    [Fact]
    public void ResetPreservesOwnershipOfAPushToTalkHeldBeforeHookStarted()
    {
        var merger = new PushToTalkMerger(PushToTalkBinding.FromKey(V));
        merger.SyncPhysical(true);
        _filter.Reset(pushToTalkDown: merger.PhysicallyHeld);
        Start();
        Assert.False(Down(V).Swallow);
        var released = Up(V);
        Assert.False(released.Swallow);
        merger.HandleKey(V, false, false);
        Assert.False(merger.PhysicallyHeld);
        Assert.Equal(PushToTalkSend.Down, merger.Press());
    }

    [Fact]
    public void SearchAfterChordReleaseCancelsOnDisableResetAndRebind()
    {
        Start();
        Up(B);
        Assert.IsType<ChordCancelled>(_filter.SetEnabled(false));
        Assert.False(_filter.SearchActive);
        _filter.SetEnabled(true);
        Up(ScanKey.DefaultSearchKey);
        Start();
        Up(B);
        Assert.IsType<ChordCancelled>(_filter.ResetIfChordActive(false));
        Start();
        Up(B);
        Assert.IsType<ChordCancelled>(_filter.SetChordKey(new ScanKey(0x4A, false)));
    }

    private void Start()
    {
        Down(B);
        Assert.IsType<SearchPressed>(Down(ScanKey.DefaultSearchKey).Event);
    }

    private KeyFilterResult Down(ScanKey key) => _filter.Process(key.ScanCode, key.IsExtended, true, false);
    private KeyFilterResult Up(ScanKey key) => _filter.Process(key.ScanCode, key.IsExtended, false, false);
}
