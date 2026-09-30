using static QuickParrot.Core.Diagnostics.LoopbackTestSpectrum;

namespace QuickParrot.Core.Diagnostics;

/// <summary>
/// A simple voice activity check: ~40 ms frames whose 100-3500 Hz energy, ignoring the chime's pitches, is loud and
/// stands out from the recording's quietest frames.
/// </summary>
internal static class LoopbackTestVoiceDetector
{
    private const double FrameSeconds = 0.04;
    private const double LowHz = 100;
    private const double HighHz = 3500;
    private const int ExcludedBinsAroundTone = 5;
    private const double BackgroundPercentile = 0.1;
    private const double MinVoiceDb = -50;
    private const double MinAboveBackgroundDb = 12;
    private const double AlwaysVoiceDb = -35;
    private const double MinVoiceSeconds = 0.3;

    public readonly record struct Detection(bool Detected, double VoiceSeconds, double LevelDb, double BackgroundDb);

    public static Detection Detect(float[] samples, int sampleRate, IReadOnlyList<double> excludedHz)
    {
        var length = NextPowerOfTwo((int)(FrameSeconds * sampleRate));
        var bins = BandBins(length, sampleRate, excludedHz);
        var window = BlackmanHarris(length);
        var windowPower = window.Sum(w => w * w);
        var (cosTable, sinTable) = Twiddles(length);
        var real = new double[length];
        var imaginary = new double[length];

        var frameDbs = new List<double>();
        for (var start = 0; start + length <= samples.Length; start += length)
        {
            for (var i = 0; i < length; i++)
                real[i] = samples[start + i] * window[i];
            Array.Clear(imaginary);
            Fft(real, imaginary, cosTable, sinTable);

            var power = 0.0;
            foreach (var k in bins)
                power += real[k] * real[k] + imaginary[k] * imaginary[k];

            frameDbs.Add(MeanSquareToDb(2 * power / (length * windowPower)));
        }

        if (frameDbs.Count == 0)
            return new Detection(false, 0, SilenceDb, SilenceDb);

        // Continuous talking raises the background estimate, so anything clearly loud counts regardless.
        var background = Percentile(frameDbs, BackgroundPercentile);
        var threshold = Math.Max(MinVoiceDb, Math.Min(background + MinAboveBackgroundDb, AlwaysVoiceDb));
        var active = frameDbs.Where(db => db >= threshold).ToList();
        var seconds = active.Count * length / (double)sampleRate;
        var level = active.Count > 0 ? Percentile(active, 0.5) : SilenceDb;
        return new Detection(seconds >= MinVoiceSeconds, seconds, level, background);
    }

    private static int[] BandBins(int length, int sampleRate, IReadOnlyList<double> excludedHz)
    {
        var binHz = sampleRate / (double)length;
        var bins = new List<int>();
        for (var k = (int)Math.Ceiling(LowHz / binHz); k <= Math.Min(HighHz / binHz, length / 2 - 1); k++)
        {
            if (excludedHz.All(hz => Math.Abs(k - hz / binHz) > ExcludedBinsAroundTone))
                bins.Add(k);
        }

        return bins.ToArray();
    }
}
