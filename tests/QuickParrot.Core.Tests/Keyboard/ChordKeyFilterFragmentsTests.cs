using QuickParrot.Core.Keyboard;

namespace QuickParrot.Core.Tests.Keyboard;

public sealed class ChordKeyFilterFragmentsTests
{
    private readonly ChordKeyFilter _filter = new(ScanKey.DefaultChordKey);
    private static readonly ScanKey Backspace = new(0x0E, false);

    [Fact]
    public void FragmentSearchPersistsAfterChordReleaseAndSelectsNumbersOnRelease()
    {
        Assert.Null(Down(ScanKey.DefaultFragmentsKey).Event);
        Up(ScanKey.DefaultFragmentsKey);
        Open();
        Assert.IsType<ChordReleased>(Up(ScanKey.DefaultChordKey).Event);
        var digit = new ScanKey(0x03, false);
        Assert.Null(Down(digit).Event);
        Assert.Null(Down(digit).Event);
        Assert.Equal(new FragmentSelectionPressed(2), Up(digit).Event);
        Assert.True(_filter.SearchActive);
        Assert.True(_filter.FragmentsActive);
    }

    [Fact]
    public void EnterHasDistinctDownAndUpAndRepeatsAreIgnored()
    {
        Open();
        Assert.IsType<FragmentEnterPressed>(Down(ScanKey.Enter).Event);
        Assert.Null(Down(ScanKey.Enter).Event);
        Assert.Null(Down(ScanKey.NumpadEnter).Event);
        Assert.Null(Up(ScanKey.NumpadEnter).Event);
        Assert.IsType<FragmentEnterReleased>(Up(ScanKey.Enter).Event);
    }

    [Fact]
    public void BackspaceReportsRepeatsWithoutTreatingThemAsFreshPresses()
    {
        Open();
        Assert.Equal(new FragmentBackspacePressed(), Down(Backspace).Event);
        Assert.Equal(new FragmentBackspacePressed(true), Down(Backspace).Event);
        Up(Backspace);
        Assert.Equal(new FragmentBackspacePressed(), Down(Backspace).Event);
    }

    [Fact]
    public void CompletionSwallowsEventualEnterUpAndIgnoresStaleSession()
    {
        var first = Open();
        Down(ScanKey.Enter);
        _filter.CompleteFragmentsSession(first.SessionId);
        Assert.False(_filter.FragmentsActive);
        Assert.True(Up(ScanKey.Enter).Swallow);
        Up(ScanKey.DefaultChordKey);
        Up(ScanKey.DefaultFragmentsKey);
        var second = Open();
        Assert.NotEqual(first.SessionId, second.SessionId);
        _filter.CompleteFragmentsSession(first.SessionId);
        Assert.True(_filter.FragmentsActive);
    }

    [Fact]
    public void DigitHeldBeforeOpeningDoesNotSelectOnRelease()
    {
        Down(ScanKey.DefaultChordKey);
        var digit = new ScanKey(0x02, false);
        Down(digit);
        Down(ScanKey.DefaultFragmentsKey);
        Assert.Null(Down(digit).Event);
        Assert.Null(Up(digit).Event);
    }

    [Fact]
    public void EscapeAndDisableCancelAndPreserveHiddenUps()
    {
        Open();
        Down(ScanKey.Enter);
        Assert.IsType<ChordCancelled>(Down(ScanKey.Escape).Event);
        Assert.Null(Up(ScanKey.Enter).Event);
        Assert.True(Up(ScanKey.Escape).Swallow);
        Up(ScanKey.DefaultChordKey);
        Up(ScanKey.DefaultFragmentsKey);
        Open();
        Assert.IsType<ChordCancelled>(_filter.SetEnabled(false));
        Assert.False(_filter.FragmentsActive);
    }

    private FragmentsPressed Open()
    {
        Down(ScanKey.DefaultChordKey);
        return Assert.IsType<FragmentsPressed>(Down(ScanKey.DefaultFragmentsKey).Event);
    }

    private KeyFilterResult Down(ScanKey key) => _filter.Process(key.ScanCode, key.IsExtended, true, false);
    private KeyFilterResult Up(ScanKey key) => _filter.Process(key.ScanCode, key.IsExtended, false, false);
}
