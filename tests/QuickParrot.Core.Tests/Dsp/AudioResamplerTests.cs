using QuickParrot.Core.Dsp;

namespace QuickParrot.Core.Tests.Dsp;

public class AudioResamplerTests
{
    [Fact]
    public void SameSampleRate_ReturnsEquivalentData()
    {
        float[] source = [0.1f, -0.2f, 0.3f];

        var result = AudioResampler.Resample(source, 16_000, 16_000);

        Assert.Equal(source, result);
    }

    [Fact]
    public void EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(AudioResampler.Resample([], 48_000, 16_000));
    }

    [Theory]
    [InlineData(48_000, 16_000)]
    [InlineData(44_100, 16_000)]
    [InlineData(8_000, 16_000)]
    public void OutputLength_MatchesTargetDuration(int sourceRate, int targetRate)
    {
        var oneSecond = new float[sourceRate];

        var result = AudioResampler.Resample(oneSecond, sourceRate, targetRate);

        Assert.InRange(result.Length, targetRate - 1, targetRate + 1);
    }

    [Fact]
    public void Downsampling_PreservesToneFrequency()
    {
        const int sourceRate = 48_000;
        const int targetRate = 16_000;
        const double toneHz = 440;

        var source = GenerateSine(toneHz, sourceRate, seconds: 0.25);
        var resampled = AudioResampler.Resample(source, sourceRate, targetRate);

        var estimatedHz = EstimateFrequencyByZeroCrossings(resampled, targetRate);

        Assert.InRange(estimatedHz, toneHz - 15, toneHz + 15);
    }

    [Fact]
    public void Upsampling_PreservesToneFrequency()
    {
        const int sourceRate = 8_000;
        const int targetRate = 16_000;
        const double toneHz = 440;

        var source = GenerateSine(toneHz, sourceRate, seconds: 0.25);
        var resampled = AudioResampler.Resample(source, sourceRate, targetRate);

        var estimatedHz = EstimateFrequencyByZeroCrossings(resampled, targetRate);

        Assert.InRange(estimatedHz, toneHz - 15, toneHz + 15);
    }

    [Theory] // 12 kHz would fold to 4 kHz and 10 kHz to 6 kHz, right in the speech band.
    [InlineData(12_000)]
    [InlineData(10_000)]
    public void Downsampling_FiltersToneAboveTheNewNyquistInsteadOfAliasingIt(double toneHz)
    {
        var resampled = AudioResampler.Resample(GenerateSine(toneHz, 48_000, seconds: 0.25), 48_000, 16_000);

        Assert.True(InteriorRms(resampled) < 0.01, $"RMS {InteriorRms(resampled):0.0000}");
    }

    [Theory]
    [InlineData(48_000)]
    [InlineData(44_100)]
    public void Downsampling_KeepsSpeechBandLevel(int sourceRate)
    {
        var resampled = AudioResampler.Resample(GenerateSine(1000, sourceRate, seconds: 0.25), sourceRate, 16_000);

        Assert.Equal(Math.Sqrt(0.5), InteriorRms(resampled), 0.01);
    }

    [Fact]
    public void Downsampling_PassesDcAtUnityGain()
    {
        var resampled = AudioResampler.Resample(Enumerable.Repeat(0.5f, 4800).ToArray(), 48_000, 16_000);

        Assert.All(resampled[20..^20], s => Assert.Equal(0.5, s, 1e-4));
    }

    private static double InteriorRms(float[] samples)
    {
        var interior = samples[(samples.Length / 10)..(samples.Length * 9 / 10)];
        return Math.Sqrt(interior.Average(s => (double)s * s));
    }

    private static float[] GenerateSine(double frequencyHz, int sampleRate, double seconds)
    {
        var count = (int)(sampleRate * seconds);
        var samples = new float[count];
        for (var i = 0; i < count; i++)
            samples[i] = (float)Math.Sin(2 * Math.PI * frequencyHz * i / sampleRate);

        return samples;
    }

    private static double EstimateFrequencyByZeroCrossings(float[] samples, int sampleRate)
    {
        var crossings = 0;
        for (var i = 1; i < samples.Length; i++)
        {
            if (samples[i - 1] < 0 && samples[i] >= 0)
                crossings++;
        }

        var durationSeconds = (double)samples.Length / sampleRate;
        return crossings / durationSeconds;
    }
}
