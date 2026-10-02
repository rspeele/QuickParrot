using QuickParrot.Core.Keyboard;

namespace QuickParrot.Core.Tests.Keyboard;

public sealed class ChordKeyFilterShiftTests
{
    private readonly ChordKeyFilter _filter = new(ScanKey.DefaultChordKey);

    private KeyFilterResult Process(ScanKey key, bool down, bool injected = false) =>
        _filter.Process(key.ScanCode, key.IsExtended, down, injected);

    [Fact]
    public void ShiftAlreadyHeld_DoesNotChangeTheChordEvent()
    {
        Assert.Equal(KeyFilterResult.PassThrough, Process(ScanKey.LeftShift, true));
        Assert.Equal(new KeyFilterResult(false, new ChordPressed()), Process(ScanKey.DefaultChordKey, true));
        Assert.Equal(KeyFilterResult.PassThrough, Process(ScanKey.LeftShift, false));
    }

    [Fact]
    public void BothShifts_PassThroughWithoutEventsOrRepeats()
    {
        Process(ScanKey.DefaultChordKey, true);

        Assert.Equal(KeyFilterResult.PassThrough, Process(ScanKey.LeftShift, true));
        Assert.Equal(KeyFilterResult.PassThrough, Process(ScanKey.LeftShift, true));
        Assert.Equal(KeyFilterResult.PassThrough, Process(ScanKey.RightShift, true));
        Assert.Equal(KeyFilterResult.PassThrough, Process(ScanKey.LeftShift, false));
        Assert.Equal(KeyFilterResult.PassThrough, Process(ScanKey.RightShift, false));
        Assert.Equal(KeyFilterResult.PassThrough, Process(ScanKey.RightShift, false));
    }

    [Fact]
    public void FakeAndInjectedShifts_DoNotChangeEffectiveState()
    {
        Process(ScanKey.DefaultChordKey, true);
        Process(ScanKey.RightShift, true);

        Assert.Equal(KeyFilterResult.PassThrough, Process(ScanKey.RightShift, false, injected: true));
        Assert.Equal(KeyFilterResult.PassThrough, Process(new ScanKey(0x36, true), false));
        Assert.Equal(KeyFilterResult.PassThrough, Process(new ScanKey(0x2A, true), true));
        Assert.Equal(KeyFilterResult.PassThrough, _filter.Process(0x22A, false, false, false));
        Assert.True(_filter.ShiftHeld);
        Assert.Equal(KeyFilterResult.PassThrough, Process(ScanKey.RightShift, false));
    }

    [Fact]
    public void OutsideAChord_ShiftPassesThroughWithoutEvents()
    {
        Assert.Equal(KeyFilterResult.PassThrough, Process(ScanKey.RightShift, true));
        Assert.Equal(KeyFilterResult.PassThrough, Process(ScanKey.RightShift, false));
        Process(ScanKey.DefaultChordKey, true);
        Process(ScanKey.DefaultChordKey, false);
        Assert.Equal(KeyFilterResult.PassThrough, Process(ScanKey.LeftShift, true));
        Assert.Equal(KeyFilterResult.PassThrough, Process(ScanKey.LeftShift, false));
    }
}
