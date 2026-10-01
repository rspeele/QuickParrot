namespace QuickParrot.Core.Editing;

/// <summary>
/// Estimates inter-sample ("true") peaks by 4× oversampling with a 12-tap-per-phase Hann-windowed sinc interpolator,
/// in the spirit of BS.1770-4 Annex 2. Accurate to a fraction of a dB for content below about 0.8× Nyquist.
/// </summary>
public static class TruePeakMeter
{
    public const int Oversampling = 4;
    private const int HalfTaps = 6;
    private const int BoundBlock = 64;

    // Phases 1..3 (offsets ¼, ½, ¾); phase 0 is the sample itself. Taps cover x[n-5] .. x[n+6].
    private static readonly double[][] Phases = BuildPhases();

    // No interpolated point can exceed the largest nearby sample magnitude times this.
    private static readonly double TapBound = Phases.Max(taps => taps.Sum(Math.Abs));

    public static double PeakLinear(ReadOnlySpan<float> interleaved, int channels)
    {
        var samplePeak = 0f;
        foreach (var s in interleaved)
            samplePeak = Math.Max(samplePeak, Math.Abs(s));

        var peak = 0.0;
        foreach (var p in PerFramePeaks(interleaved, channels, samplePeak))
            peak = Math.Max(peak, p);

        return peak;
    }

    /// <summary>
    /// For each frame, the largest magnitude across channels at it and the three interpolated points after it.
    /// Interpolation is skipped where it provably can't exceed <paramref name="floor"/>, leaving just the sample there.
    /// </summary>
    public static float[] PerFramePeaks(ReadOnlySpan<float> interleaved, int channels, double floor = 0)
    {
        var frames = interleaved.Length / channels;
        var peaks = new float[frames];
        var padded = new double[frames + 2 * HalfTaps]; // padded[i] = x[i - HalfTaps], silence beyond the ends
        var blockMax = new double[frames / BoundBlock + 2];
        for (var c = 0; c < channels; c++)
        {
            Array.Clear(blockMax);
            for (var f = 0; f < frames; f++)
            {
                var x = interleaved[f * channels + c];
                padded[f + HalfTaps] = x;
                blockMax[f / BoundBlock] = Math.Max(blockMax[f / BoundBlock], Math.Abs(x));
            }

            for (var f = 0; f < frames; f++)
            {
                var peak = Math.Abs(interleaved[f * channels + c]);
                var block = f / BoundBlock;
                var nearby = Math.Max(blockMax[block], Math.Max(block > 0 ? blockMax[block - 1] : 0, blockMax[block + 1]));
                if (nearby * TapBound > floor)
                {
                    foreach (var taps in Phases)
                    {
                        var y = 0.0;
                        for (var k = 0; k < taps.Length; k++)
                            y += taps[k] * padded[f + 1 + k];

                        peak = Math.Max(peak, (float)Math.Abs(y));
                    }
                }

                if (peak > peaks[f])
                    peaks[f] = peak;
            }
        }

        return peaks;
    }

    public static double ToDb(double linear) => linear > 0 ? 20 * Math.Log10(linear) : double.NegativeInfinity;

    private static double[][] BuildPhases()
    {
        var phases = new double[Oversampling - 1][];
        for (var p = 1; p < Oversampling; p++)
        {
            var t = p / (double)Oversampling;
            var taps = new double[2 * HalfTaps];
            for (var k = 0; k < taps.Length; k++)
            {
                var d = k - HalfTaps + 1 - t; // distance from the interpolated point to tap k's sample
                var window = 0.5 + 0.5 * Math.Cos(Math.PI * d / (HalfTaps + 0.5));
                taps[k] = Sinc(d) * window;
            }

            var sum = taps.Sum();
            for (var k = 0; k < taps.Length; k++)
                taps[k] /= sum;

            phases[p - 1] = taps;
        }

        return phases;
    }

    private static double Sinc(double x) => x == 0 ? 1 : Math.Sin(Math.PI * x) / (Math.PI * x);
}
