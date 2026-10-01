using QuickParrot.Core.Editing;
using static QuickParrot.Core.Tests.Editing.TestSignals;

namespace QuickParrot.Core.Tests.Editing;

public class LoudnessNormalizerTests
{
    private static readonly LoudnessOptions Defaults = new();

    private static NormalizationResult Normalize(float[] samples, LoudnessOptions? options = null, int channels = 2) =>
        LoudnessNormalizer.Normalize(samples, channels, 48000, options ?? Defaults);

    [Fact]
    public void QuietPinkNoise_IsBroughtUpToTheTarget()
    {
        var samples = PinkNoise(-40, 2);

        var result = Normalize(samples);

        Assert.InRange(result.MeasuredLufs, -44, -36);
        Assert.Equal(-18, result.OutputLufs, 0.2);
        Assert.Equal(-18, LoudnessMeter.IntegratedLufs(samples, 2, 48000), 0.2);
        Assert.True(TruePeakMeter.ToDb(TruePeakMeter.PeakLinear(samples, 2)) <= -1);
    }

    [Fact]
    public void LoudSine_IsTurnedDownWithoutLimiting()
    {
        var samples = Sine(1000, -6, 2);

        var result = Normalize(samples);

        Assert.Equal(-18 - result.MeasuredLufs, result.GainDb, 1e-9);
        Assert.Equal(0, result.LimitedFrames);
        Assert.Equal(-18, result.OutputLufs, 0.05);
        Assert.Equal(DbToLinear(-6 + result.GainDb), samples.Max(), 0.001);
    }

    [Fact]
    public void Transients_AreLimitedToTheCeiling_WhileTheBodyReachesTheTarget()
    {
        var samples = PinkNoise(-36, 2, seed: 3);
        for (var click = 0; click < 4; click++)
        {
            var at = (20000 + click * 20000) * 2;
            samples[at] = samples[at + 1] = 0.9f;
            samples[at + 2] = samples[at + 3] = -0.9f;
        }

        var result = Normalize(samples);

        Assert.True(result.LimitedFrames > 0);
        Assert.True(result.OutputTruePeakDbtp <= -1 + 1e-6, $"true peak {result.OutputTruePeakDbtp:0.00} dBTP");
        Assert.Equal(-18, result.OutputLufs, 0.5);
    }

    [Fact]
    public void Ceiling_HoldsForAFullScaleHighFrequencySine()
    {
        // A quarter-sample-rate sine sampled 45° off its peaks: samples sit 3 dB below the true peak.
        var samples = Sine(12000, -3, 1, phase: Math.PI / 4);

        var result = Normalize(samples, new LoudnessOptions(TargetLufs: -6));

        Assert.True(result.OutputTruePeakDbtp <= -1 + 1e-6);
        Assert.True(samples.Max() < DbToLinear(-1));
    }

    [Fact]
    public void Silence_IsLeftAlone()
    {
        var samples = Silence(1);

        var result = Normalize(samples);

        Assert.Equal(0, result.GainDb);
        Assert.Equal(double.NegativeInfinity, result.MeasuredLufs);
        Assert.All(samples, s => Assert.Equal(0f, s));
    }

    [Fact]
    public void Boost_IsCapped()
    {
        var samples = PinkNoise(-62, 1);

        var result = Normalize(samples, new LoudnessOptions(MaxGainDb: 24));

        Assert.Equal(24, result.GainDb);
        Assert.Equal(result.MeasuredLufs + 24, result.OutputLufs, 0.05);
    }

    [Fact]
    public void ShortClip_StillReachesTheTarget()
    {
        var samples = Sine(440, -30, 0.25);

        var result = Normalize(samples);

        Assert.Equal(-18, result.OutputLufs, 0.2);
    }

    [Fact]
    public void GainDbFor_TreatsSilenceAsNoChange()
    {
        Assert.Equal(0, LoudnessNormalizer.GainDbFor(double.NegativeInfinity, Defaults));
        Assert.Equal(5, LoudnessNormalizer.GainDbFor(-23, Defaults));
        Assert.Equal(24, LoudnessNormalizer.GainDbFor(-80, Defaults));
    }
}

public class TruePeakMeterTests
{
    [Fact]
    public void FindsTheInterSamplePeakOfAQuarterRateSine()
    {
        var samples = Sine(12000, 0, 0.1, channels: 1, phase: Math.PI / 4);

        Assert.Equal(DbToLinear(-3.01), samples.Max(), 0.001);
        Assert.Equal(0, TruePeakMeter.ToDb(TruePeakMeter.PeakLinear(samples, 1)), 0.3);
    }

    [Fact]
    public void LowFrequencySine_PeaksAtItsAmplitude()
    {
        var samples = Sine(997, -6, 0.1);

        Assert.Equal(-6, TruePeakMeter.ToDb(TruePeakMeter.PeakLinear(samples, 2)), 0.05);
    }

    [Fact]
    public void PerFramePeaks_AreAtLeastTheSamplesThemselves()
    {
        var samples = PinkNoise(-12, 0.05);

        var peaks = TruePeakMeter.PerFramePeaks(samples, 2);

        Assert.Equal(samples.Length / 2, peaks.Length);
        for (var f = 0; f < peaks.Length; f++)
            Assert.True(peaks[f] >= Math.Max(Math.Abs(samples[2 * f]), Math.Abs(samples[2 * f + 1])));
    }
}

public class SlidingMinimumTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(17)]
    [InlineData(500)]
    public void MatchesBruteForce(int window)
    {
        var random = new Random(window);
        var values = Enumerable.Range(0, 300).Select(_ => random.NextDouble()).ToArray();

        var result = SlidingMinimum.Forward(values, window);

        for (var i = 0; i < values.Length; i++)
            Assert.Equal(values.Skip(i).Take(window).Min(), result[i]);
    }
}
