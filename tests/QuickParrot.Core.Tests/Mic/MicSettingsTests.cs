using QuickParrot.Core.Mic;
using QuickParrot.Core.Settings;

namespace QuickParrot.Core.Tests.Mic;

public class MicSettingsTests
{
    [Fact]
    public void Defaults_AreOffAt20PercentWithAutoDevice()
    {
        var settings = JsonSettingsStore.Deserialize("{}");

        Assert.Equal(new MicDuckSettings(MicDuckMode.Off, 20, null), settings.ToMicDuckSettings());
        Assert.False(settings.ToPlaybackOptions().MicMuteEnabled);
    }

    [Theory]
    [InlineData("""{ "micMuteEnabled": true }""", MicDuckMode.Mute)]
    [InlineData("""{ "micMuteEnabled": false }""", MicDuckMode.Off)]
    [InlineData("""{ "micMuteEnabled": true, "micDuckMode": 2 }""", MicDuckMode.Attenuate)]
    [InlineData("""{ "micDuckMode": 1 }""", MicDuckMode.Mute)]
    [InlineData("""{ "micDuckMode": 7 }""", MicDuckMode.Off)]
    [InlineData("""{ "micDuckMode": -1, "micMuteEnabled": true }""", MicDuckMode.Off)]
    public void OldMuteFlag_MigratesAndBadModesTurnOff(string json, MicDuckMode expected)
    {
        Assert.Equal(expected, JsonSettingsStore.Deserialize(json).MicDuckMode);
    }

    [Fact]
    public void MigratedSettings_DropTheOldFlag()
    {
        var migrated = JsonSettingsStore.Deserialize("""{ "micMuteEnabled": true }""");
        var json = JsonSettingsStore.Serialize(migrated);

        Assert.DoesNotContain("micMuteEnabled", json);
        Assert.Equal(migrated, JsonSettingsStore.Deserialize(json));
        Assert.Equal(new AppSettings { MicDuckMode = MicDuckMode.Mute }, migrated);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(150, 100)]
    [InlineData(40, 40)]
    public void AttenuationPercent_IsClamped(int stored, int expected)
    {
        var settings = JsonSettingsStore.Deserialize($$"""{ "micAttenuationPercent": {{stored}} }""");

        Assert.Equal(expected, settings.MicAttenuationPercent);
    }

    [Fact]
    public void EmptyDeviceId_MeansAuto()
    {
        Assert.Null(JsonSettingsStore.Deserialize("""{ "micDeviceId": "" }""").MicDeviceId);
    }

    [Fact]
    public void AnyDuckMode_EnablesMuteInPlaybackOptions()
    {
        var settings = new AppSettings { MicDuckMode = MicDuckMode.Attenuate, MicAttenuationPercent = 10, MicDeviceId = "m" };

        Assert.True(settings.ToPlaybackOptions().MicMuteEnabled);
        Assert.Equal(new MicDuckSettings(MicDuckMode.Attenuate, 10, "m"), settings.ToMicDuckSettings());
    }
}
