using QuickParrot.Core.Editing;

namespace QuickParrot.Core.Tests.Editing;

public class LoudnessDescriptionTests
{
    private static readonly LoudnessOptions Options = new(TargetLufs: -18, MaxGainDb: 24);

    [Fact]
    public void BeforeAndWithoutAMeasurement()
    {
        Assert.Equal("Measuring…", LoudnessDescription.Describe(LoudnessReading.Measuring, Options));
        Assert.Equal("Loudness unknown", LoudnessDescription.Describe(LoudnessReading.Unavailable, Options));
        Assert.Equal("Selection is silent", LoudnessDescription.Describe(LoudnessReading.Of(double.NegativeInfinity), Options));
    }

    [Theory]
    [InlineData(-18.3, "Already about right")]
    [InlineData(-30, "Will be turned up 12 dB")]
    [InlineData(-60, "Will be turned up 24 dB")] // capped at the maximum gain
    [InlineData(-12, "Will be turned down 6 dB")]
    public void DescribesTheNormalizationGain(double lufs, string expected)
    {
        Assert.Equal(expected, LoudnessDescription.Describe(LoudnessReading.Of(lufs), Options));
    }
}
