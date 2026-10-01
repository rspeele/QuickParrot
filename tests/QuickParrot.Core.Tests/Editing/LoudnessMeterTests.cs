using QuickParrot.Core.Editing;
using static QuickParrot.Core.Tests.Editing.TestSignals;

namespace QuickParrot.Core.Tests.Editing;

public class LoudnessMeterTests
{
    // The gating cases (3-5) use a fifth of Tech 3341's durations to keep the suite fast; the proportions are unchanged.
    private const double Tech3341Tolerance = 0.1;

    private static double Measure(float[] samples, int sampleRate = 48000, int channels = 2) =>
        LoudnessMeter.IntegratedLufs(samples, channels, sampleRate);

    [Fact]
    public void KWeighting_At48kHz_MatchesTheBs1770CoefficientTables()
    {
        var (shelf, highPass) = LoudnessMeter.KWeighting(48000);

        Assert.Equal(1.53512485958697, shelf.B0, 1e-6);
        Assert.Equal(-2.69169618940638, shelf.B1, 1e-6);
        Assert.Equal(1.19839281085285, shelf.B2, 1e-6);
        Assert.Equal(-1.69065929318241, shelf.A1, 1e-6);
        Assert.Equal(0.73248077421585, shelf.A2, 1e-6);
        Assert.Equal((1.0, -2.0, 1.0), (highPass.B0, highPass.B1, highPass.B2));
        Assert.Equal(-1.99004745483398, highPass.A1, 1e-6);
        Assert.Equal(0.99007225036621, highPass.A2, 1e-6);
    }

    [Theory] // EBU Tech 3341 cases 1 and 2: stereo 1 kHz sine, in phase on both channels.
    [InlineData(-23.0)]
    [InlineData(-33.0)]
    public void StereoSine_ReadsItsPeakLevel_PerTech3341(double dbfs)
    {
        Assert.Equal(dbfs, Measure(Sine(1000, dbfs, 5)), Tech3341Tolerance);
    }

    [Fact]
    public void MonoSineAtMinus20Dbfs_ReadsMinus23Lufs()
    {
        // A single channel carries half the power of the same sine on two, hence 3 LU below the stereo reading.
        Assert.Equal(-23.0, Measure(Sine(1000, -20, 3, channels: 1), channels: 1), Tech3341Tolerance);
        Assert.Equal(-20.0, Measure(Sine(1000, -20, 3)), Tech3341Tolerance);
    }

    [Fact]
    public void RelativeGate_IgnoresQuieterPassages_PerTech3341Case3()
    {
        var signal = Concat(Sine(1000, -36, 2), Sine(1000, -23, 12), Sine(1000, -36, 2));

        Assert.Equal(-23.0, Measure(signal), Tech3341Tolerance);
    }

    [Fact]
    public void AbsoluteGate_IgnoresNearSilence_PerTech3341Case4()
    {
        var signal = Concat(
            Sine(1000, -72, 2), Sine(1000, -36, 2), Sine(1000, -23, 12), Sine(1000, -36, 2), Sine(1000, -72, 2));

        Assert.Equal(-23.0, Measure(signal), Tech3341Tolerance);
    }

    [Fact]
    public void LoudPassagesAverage_PerTech3341Case5()
    {
        var signal = Concat(Sine(1000, -26, 4), Sine(1000, -20, 4.02), Sine(1000, -26, 4));

        Assert.Equal(-23.0, Measure(signal), Tech3341Tolerance);
    }

    [Fact]
    public void At44100Hz_TheSameSineReadsTheSame()
    {
        Assert.Equal(-23.0, Measure(Sine(1000, -23, 3, sampleRate: 44100), sampleRate: 44100), Tech3341Tolerance);
    }

    [Fact]
    public void Silence_IsNegativeInfinity()
    {
        Assert.Equal(double.NegativeInfinity, Measure(Silence(2)));
        Assert.Equal(double.NegativeInfinity, Measure([]));
        Assert.Equal(double.NegativeInfinity, Measure(Sine(1000, -80, 1)));
    }

    [Fact]
    public void ClipShorterThanOneBlock_IsMeasuredAsOneBlock()
    {
        Assert.Equal(-23.0, Measure(Sine(1000, -23, 0.2)), 0.2);
        Assert.Equal(double.NegativeInfinity, Measure(Silence(0.2)));
    }

    [Fact]
    public void PinkNoise_ScalesLinearlyAndSumsAcrossChannels()
    {
        var stereo = PinkNoise(-20, 2);
        var mono = stereo.Where((_, i) => i % 2 == 0).ToArray();
        var dualMono = mono.SelectMany(s => new[] { s, s }).ToArray();
        var lufs = Measure(stereo);

        Assert.InRange(lufs, -24, -16); // pink noise's K-weighted level sits within a few LU of its RMS
        Assert.Equal(lufs - 6.0206, Measure(Scaled(stereo, -6.0206)), 0.01);
        Assert.Equal(Measure(mono, channels: 1) + 3.0103, Measure(dualMono), 0.01);
    }

    [Fact]
    public void KWeighting_DiscountsLowBassAndLiftsTreble()
    {
        var reference = Measure(Sine(1000, -23, 2));

        Assert.True(Measure(Sine(20, -23, 2)) < reference - 10);
        Assert.InRange(Measure(Sine(8000, -23, 2)) - reference, 3, 4.5);
    }

    [Fact]
    public void SurroundChannels_AreWeightedAndLfeIgnored()
    {
        var mono = Sine(1000, -23, 2, channels: 1);

        Assert.Equal(double.NegativeInfinity, Measure(OnChannel(mono, 3, 6), channels: 6));
        var front = Measure(OnChannel(mono, 0, 6), channels: 6);
        var surround = Measure(OnChannel(mono, 4, 6), channels: 6);
        Assert.Equal(10 * Math.Log10(1.41), surround - front, 0.01);
    }

    private static float[] OnChannel(float[] mono, int channel, int channels)
    {
        var result = new float[mono.Length * channels];
        for (var f = 0; f < mono.Length; f++)
            result[f * channels + channel] = mono[f];

        return result;
    }
}
