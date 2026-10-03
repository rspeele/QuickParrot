using QuickParrot.Core.Keyboard;
using QuickParrot.Core.Settings;

namespace QuickParrot.Core.Tests;

public sealed class AppSettingsSearchTests
{
    [Fact]
    public void SearchDefaultsToNumpadDivideAndRoundTrips()
    {
        Assert.Equal(new ScanKey(0x35, true), JsonSettingsStore.Deserialize("{}").SearchKey);
        var settings = new AppSettings { SearchKey = new ScanKey(0x4E, false) };
        Assert.Equal(settings, JsonSettingsStore.Deserialize(JsonSettingsStore.Serialize(settings)));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0x02, false)]
    [InlineData(0x2A, false)]
    [InlineData(0x1D, false)]
    [InlineData(0x38, true)]
    [InlineData(0x5B, true)]
    [InlineData(0x1C, false)]
    [InlineData(0x01, false)]
    [InlineData(0x0E, false)]
    [InlineData(0x53, true)]
    [InlineData(0x3B, false)]
    public void ReservedKeysAreRejectedAndSanitized(int scanCode, bool extended)
    {
        var settings = new AppSettings();
        var key = new ScanKey(scanCode, extended);
        var (result, error) = settings.WithSearchKey(key);
        Assert.Same(settings, result);
        Assert.NotNull(error);
        Assert.Equal(ScanKey.DefaultSearchKey, (settings with { SearchKey = key }).Sanitized().SearchKey);
    }

    [Fact]
    public void ConflictingKeysAreRejectedInBothDirections()
    {
        var settings = new AppSettings();
        Assert.NotNull(settings.WithSearchKey(settings.ChordKey).Error);
        Assert.NotNull(settings.WithSearchKey(settings.SaveNavigationKey).Error);
        Assert.NotNull(settings.WithSearchKey(settings.PushToTalkBinding.Key!.Value).Error);
        Assert.NotNull(settings.WithChordKey(settings.SearchKey).Error);
        Assert.NotNull(settings.WithSaveNavigationKey(settings.SearchKey).Error);
        Assert.NotNull(settings.WithPushToTalkBinding(PushToTalkBinding.FromKey(settings.SearchKey)).Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ExistingBindingsAtDivideArePreservedWithSafeSearchFallback(int occupied)
    {
        var settings = occupied switch
        {
            0 => new AppSettings { ChordKey = ScanKey.DefaultSearchKey },
            1 => new AppSettings { SaveNavigationKey = ScanKey.DefaultSearchKey },
            _ => new AppSettings { PushToTalkBinding = PushToTalkBinding.FromKey(ScanKey.DefaultSearchKey) },
        };
        var sanitized = settings.Sanitized();
        Assert.Equal(settings.ChordKey, sanitized.ChordKey);
        Assert.Equal(settings.SaveNavigationKey, sanitized.SaveNavigationKey);
        Assert.Equal(settings.PushToTalkBinding, sanitized.PushToTalkBinding);
        Assert.NotEqual(sanitized.ChordKey, sanitized.SearchKey);
        Assert.NotEqual(sanitized.SaveNavigationKey, sanitized.SearchKey);
        Assert.NotEqual(sanitized.PushToTalkBinding.Key, sanitized.SearchKey);
        Assert.Equal(sanitized, sanitized.Sanitized());
    }
}
