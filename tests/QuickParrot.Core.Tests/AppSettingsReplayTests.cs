using QuickParrot.Core.Settings;

namespace QuickParrot.Core.Tests;

public class AppSettingsReplayTests
{
    [Fact]
    public void Defaults_KeepThirtySeconds()
    {
        var settings = JsonSettingsStore.Deserialize("{}");

        Assert.True(settings.ReplayBufferEnabled);
        Assert.Equal(30, settings.ReplayBufferSeconds);
    }

    [Fact]
    public void RoundTripsThroughJson()
    {
        var settings = new AppSettings { ReplayBufferEnabled = false, ReplayBufferSeconds = 90 };

        var roundTripped = JsonSettingsStore.Deserialize(JsonSettingsStore.Serialize(settings));

        Assert.False(roundTripped.ReplayBufferEnabled);
        Assert.Equal(90, roundTripped.ReplayBufferSeconds);
    }

    [Theory]
    [InlineData(0, AppSettings.MinReplayBufferSeconds)]
    [InlineData(-5, AppSettings.MinReplayBufferSeconds)]
    [InlineData(5, 5)]
    [InlineData(120, 120)]
    [InlineData(100_000, AppSettings.MaxReplayBufferSeconds)]
    public void ReplayBufferSeconds_IsClamped(int seconds, int expected) =>
        Assert.Equal(expected, new AppSettings { ReplayBufferSeconds = seconds }.Sanitized().ReplayBufferSeconds);
}
