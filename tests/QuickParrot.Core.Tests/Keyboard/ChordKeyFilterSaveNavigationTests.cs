using QuickParrot.Core.Keyboard;

namespace QuickParrot.Core.Tests.Keyboard;

public sealed class ChordKeyFilterSaveNavigationTests
{
    private readonly ChordKeyFilter _filter = new(ScanKey.DefaultChordKey);

    private KeyFilterResult Process(ScanKey key, bool down, bool injected = false) =>
        _filter.Process(key.ScanCode, key.IsExtended, down, injected);

    [Fact]
    public void SaveKeyIsSwallowedDuringChord_WithNoRepeat_AndMatchedUpAfterRelease()
    {
        Process(ScanKey.DefaultChordKey, true);
        Assert.Equal(new KeyFilterResult(true, new SaveNavigationPressed()), Process(ScanKey.DefaultSaveNavigationKey, true));
        Assert.Equal(KeyFilterResult.SwallowSilently, Process(ScanKey.DefaultSaveNavigationKey, true));
        Process(ScanKey.DefaultChordKey, false);
        Assert.Equal(KeyFilterResult.SwallowSilently, Process(ScanKey.DefaultSaveNavigationKey, false));
        Assert.Equal(KeyFilterResult.PassThrough, Process(ScanKey.DefaultSaveNavigationKey, true));
    }

    [Fact]
    public void SaveKeyHeldBeforeChord_IsNotTakenOver_AndExtendedOrInjectedKeysAreIgnored()
    {
        Process(ScanKey.DefaultSaveNavigationKey, true);
        Process(ScanKey.DefaultChordKey, true);
        Assert.Equal(KeyFilterResult.PassThrough, Process(ScanKey.DefaultSaveNavigationKey, true));
        Assert.Equal(KeyFilterResult.PassThrough, Process(ScanKey.DefaultSaveNavigationKey, false));
        Assert.Equal(KeyFilterResult.PassThrough, Process(new ScanKey(0x37, true), true));
        Assert.Equal(KeyFilterResult.PassThrough, Process(ScanKey.DefaultSaveNavigationKey, true, injected: true));
        Assert.Equal(new KeyFilterResult(true, new SaveNavigationPressed()), Process(ScanKey.DefaultSaveNavigationKey, true));
    }

    [Fact]
    public void ConfiguredKeyReplacesDefault_AndShiftDoesNotAffectSaving()
    {
        _filter.SaveNavigationKey = new ScanKey(0x29, false);
        Process(ScanKey.DefaultChordKey, true);
        Process(ScanKey.LeftShift, true);
        Assert.Equal(KeyFilterResult.PassThrough, Process(ScanKey.DefaultSaveNavigationKey, true));
        Assert.Equal(new KeyFilterResult(true, new SaveNavigationPressed()), Process(_filter.SaveNavigationKey, true));
    }

    [Fact]
    public void SaveKeyCaptureAndDisabledModeKeepExistingBehavior()
    {
        _filter.BeginCapture();
        Assert.Equal(KeyFilterResult.Captured(ScanKey.DefaultSaveNavigationKey), Process(ScanKey.DefaultSaveNavigationKey, true));
        Process(ScanKey.DefaultSaveNavigationKey, false);
        _filter.SetEnabled(false);
        Process(ScanKey.DefaultChordKey, true);
        Assert.Equal(KeyFilterResult.PassThrough, Process(ScanKey.DefaultSaveNavigationKey, true));
    }

    [Fact]
    public void SaveKeyProcessingDoesNotAllocate()
    {
        Process(ScanKey.DefaultChordKey, true);
        Process(ScanKey.DefaultSaveNavigationKey, true);
        Process(ScanKey.DefaultSaveNavigationKey, false);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            Process(ScanKey.DefaultSaveNavigationKey, true);
            Process(ScanKey.DefaultSaveNavigationKey, false);
        }
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }
}
