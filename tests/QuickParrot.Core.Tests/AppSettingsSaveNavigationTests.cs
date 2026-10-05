using QuickParrot.Core.Keyboard;
using QuickParrot.Core.Settings;

namespace QuickParrot.Core.Tests;

public sealed class AppSettingsSaveNavigationTests
{
    [Fact]
    public void NewDefaultsAreNumpadMinusAndMultiply_ExistingChordIsPreserved()
    {
        var defaults = JsonSettingsStore.Deserialize("{}");
        Assert.Equal(new ScanKey(0x4A, false), defaults.ChordKey);
        Assert.Equal(new ScanKey(0x37, false), defaults.SaveNavigationKey);
        var existing = JsonSettingsStore.Deserialize("""{ "chordKey": { "scanCode": 48, "isExtended": false } }""");
        Assert.Equal(new ScanKey(0x30, false), existing.ChordKey);
    }

    [Fact]
    public void SaveKeyRoundTripsThroughJson()
    {
        var settings = new AppSettings { SaveNavigationKey = new ScanKey(0x4E, false) }.Sanitized();
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
    public void ReservedSaveKeysAreRejected_AndSanitized(int scanCode, bool extended)
    {
        var settings = new AppSettings();
        var key = new ScanKey(scanCode, extended);
        var (result, error) = settings.WithSaveNavigationKey(key);
        Assert.Same(settings, result);
        Assert.NotNull(error);
        Assert.Equal(ScanKey.DefaultSaveNavigationKey, (settings with { SaveNavigationKey = key }).Sanitized().SaveNavigationKey);
    }

    [Fact]
    public void SaveChordAndPushToTalkConflictsAreRejectedInBothDirections()
    {
        var settings = new AppSettings();
        Assert.NotNull(settings.WithSaveNavigationKey(settings.ChordKey).Error);
        Assert.NotNull(settings.WithSaveNavigationKey(settings.PushToTalkBinding.Key!.Value).Error);
        Assert.NotNull(settings.WithChordKey(settings.SaveNavigationKey).Error);
        Assert.NotNull(settings.WithPushToTalkBinding(PushToTalkBinding.FromKey(settings.SaveNavigationKey)).Error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExistingChordOrPushToTalkAtMultiplyGetsASafeSaveFallback(bool chord)
    {
        var settings = chord
            ? new AppSettings { ChordKey = ScanKey.DefaultSaveNavigationKey }
            : new AppSettings { PushToTalkBinding = PushToTalkBinding.FromKey(ScanKey.DefaultSaveNavigationKey), PushToTalkEnabled = true };
        var sanitized = JsonSettingsStore.Deserialize(JsonSettingsStore.Serialize(settings));
        Assert.Equal(settings.ChordKey, sanitized.ChordKey);
        Assert.Equal(settings.PushToTalkBinding, sanitized.PushToTalkBinding);
        Assert.Equal(settings.PushToTalkEnabled, sanitized.PushToTalkEnabled);
        Assert.NotEqual(sanitized.ChordKey, sanitized.SaveNavigationKey);
        Assert.NotEqual(sanitized.PushToTalkBinding.Key, sanitized.SaveNavigationKey);
        Assert.True(sanitized.SaveNavigationKey.IsValidSaveNavigationKey);
        Assert.Equal(sanitized, JsonSettingsStore.Deserialize(JsonSettingsStore.Serialize(sanitized)));
    }
}
