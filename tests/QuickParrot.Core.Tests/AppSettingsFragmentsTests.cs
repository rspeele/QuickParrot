using QuickParrot.Core.Keyboard;
using QuickParrot.Core.Settings;

namespace QuickParrot.Core.Tests;

public sealed class AppSettingsFragmentsTests
{
    [Fact]
    public void DefaultsToPlusAndReboundKeyRoundTrips()
    {
        Assert.Equal(new ScanKey(0x4E, false), JsonSettingsStore.Deserialize("{}").FragmentsKey);
        var settings = new AppSettings().WithFragmentsKey(new ScanKey(0x29, false)).Settings;
        Assert.Equal(settings, JsonSettingsStore.Deserialize(JsonSettingsStore.Serialize(settings)));
    }

    [Fact]
    public void ConflictsAreRejectedInBothDirections()
    {
        var settings = new AppSettings();
        Assert.NotNull(settings.WithFragmentsKey(settings.ChordKey).Error);
        Assert.NotNull(settings.WithFragmentsKey(settings.SaveNavigationKey).Error);
        Assert.NotNull(settings.WithFragmentsKey(settings.SearchKey).Error);
        Assert.NotNull(settings.WithFragmentsKey(settings.PushToTalkBinding.Key!.Value).Error);
        Assert.NotNull(settings.WithChordKey(settings.FragmentsKey).Error);
        Assert.NotNull(settings.WithSaveNavigationKey(settings.FragmentsKey).Error);
        Assert.NotNull(settings.WithSearchKey(settings.FragmentsKey).Error);
        Assert.NotNull(settings.WithPushToTalkBinding(PushToTalkBinding.FromKey(settings.FragmentsKey)).Error);
        Assert.NotNull(settings.WithFragmentsKey(new ScanKey(0x02, false)).Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ExistingBindingsAtPlusArePreservedAndFragmentsGetsSafeFallback(int binding)
    {
        var plus = ScanKey.DefaultFragmentsKey;
        var settings = binding switch
        {
            0 => new AppSettings { ChordKey = plus },
            1 => new AppSettings { SaveNavigationKey = plus },
            2 => new AppSettings { SearchKey = plus },
            _ => new AppSettings { PushToTalkBinding = PushToTalkBinding.FromKey(plus) },
        };
        var sanitized = settings.Sanitized();
        Assert.Equal(settings.ChordKey, sanitized.ChordKey);
        Assert.Equal(settings.SaveNavigationKey, sanitized.SaveNavigationKey);
        Assert.Equal(settings.SearchKey, sanitized.SearchKey);
        Assert.Equal(settings.PushToTalkBinding, sanitized.PushToTalkBinding);
        Assert.NotEqual(plus, sanitized.FragmentsKey);
        Assert.Equal(sanitized, sanitized.Sanitized());
    }
}
