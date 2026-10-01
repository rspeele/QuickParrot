namespace QuickParrot.Core.Editing;

/// <param name="CeilingDbtp">True-peak ceiling the output never exceeds.</param>
/// <param name="MaxGainDb">Cap on boost, so near-silent clips don't turn into amplified noise.</param>
public sealed record LoudnessOptions(double TargetLufs = -18, double CeilingDbtp = -1, double MaxGainDb = 24);

/// <param name="MeasuredLufs">Loudness before any change (negative infinity for silence).</param>
/// <param name="GainDb">The make-up gain applied before limiting.</param>
public sealed record NormalizationResult(
    double MeasuredLufs, double GainDb, double OutputLufs, double OutputTruePeakDbtp, int LimitedFrames);

/// <summary>
/// Gains audio to a loudness target, then holds 4×-oversampled peaks under a ceiling with a 5 ms lookahead limiter
/// (~50 ms release); a final gain clamp catches anything the envelope misses, so the ceiling is guaranteed.
/// </summary>
public static class LoudnessNormalizer
{
    private const double LookaheadSeconds = 0.005;
    private const double ReleaseSeconds = 0.05;
    private const int MaxPasses = 3;
    private const double MaxCatchUpDb = 6; // extra gain allowed to make up for limiting

    /// <summary>The gain <see cref="Normalize"/> would apply before limiting, for previewing at roughly the final level.</summary>
    public static double GainDbFor(double measuredLufs, LoudnessOptions options) =>
        double.IsFinite(measuredLufs) ? Math.Min(options.TargetLufs - measuredLufs, options.MaxGainDb) : 0;

    /// <summary>
    /// Normalizes the interleaved samples in place. Limiting lowers loudness, so if the result falls short of the
    /// target the make-up gain is raised by the shortfall and the pass repeated (a few times at most).
    /// </summary>
    public static NormalizationResult Normalize(Span<float> interleaved, int channels, int sampleRate, LoudnessOptions options)
    {
        var measured = LoudnessMeter.IntegratedLufs(interleaved, channels, sampleRate);
        if (!double.IsFinite(measured))
            return new NormalizationResult(measured, 0, measured, TruePeakMeter.ToDb(TruePeakMeter.PeakLinear(interleaved, channels)), 0);

        var original = interleaved.ToArray();
        var ceiling = Math.Pow(10, options.CeilingDbtp / 20);
        var gainDb = GainDbFor(measured, options);
        var maxGainDb = Math.Min(options.MaxGainDb, gainDb + MaxCatchUpDb);
        var originalPeaks = TruePeakMeter.PerFramePeaks(original, channels, ceiling / Math.Pow(10, maxGainDb / 20));
        NormalizationResult result;
        for (var pass = 1; ; pass++)
        {
            original.CopyTo(interleaved);
            result = Apply(interleaved, channels, sampleRate, originalPeaks, ceiling, measured, gainDb);
            var shortfall = options.TargetLufs - result.OutputLufs;
            if (pass == MaxPasses || shortfall < 0.1 || gainDb >= maxGainDb || result.LimitedFrames == 0)
                return result;

            gainDb = Math.Min(gainDb + shortfall, maxGainDb);
        }
    }

    private static NormalizationResult Apply(
        Span<float> interleaved, int channels, int sampleRate, float[] originalPeaks, double ceiling, double measured, double gainDb)
    {
        var gain = Math.Pow(10, gainDb / 20);
        Scale(interleaved, (float)gain);
        var limited = Limit(interleaved, channels, sampleRate, ceiling, originalPeaks, gain);

        var peak = TruePeakMeter.PeakLinear(interleaved, channels);
        if (peak > ceiling)
        {
            Scale(interleaved, (float)(ceiling / peak * 0.9999));
            peak = TruePeakMeter.PeakLinear(interleaved, channels);
        }

        var output = LoudnessMeter.IntegratedLufs(interleaved, channels, sampleRate);
        return new NormalizationResult(measured, gainDb, output, TruePeakMeter.ToDb(peak), limited);
    }

    // Gain needed per frame (peaks scale with the make-up gain, so they're measured once), then the minimum over the
    // lookahead, a smooth release, and a lookahead-long moving average, which at a peak spans only gains at or below its need.
    private static int Limit(Span<float> interleaved, int channels, int sampleRate, double ceiling, float[] originalPeaks, double gain)
    {
        var frames = originalPeaks.Length;
        var required = new double[frames];
        var limitedFrames = 0;
        for (var f = 0; f < frames; f++)
        {
            var peak = originalPeaks[f] * gain;
            required[f] = peak > ceiling ? ceiling / peak : 1;
            if (required[f] < 1)
                limitedFrames++;
        }

        if (limitedFrames == 0)
            return 0;

        var window = Math.Max(1, (int)(sampleRate * LookaheadSeconds));
        var lookaheadMin = SlidingMinimum.Forward(required, window);
        var release = 1 - Math.Exp(-1 / (ReleaseSeconds * sampleRate));
        var held = lookaheadMin[0];
        var sum = 0.0;
        var smoothed = new double[frames];
        for (var f = 0; f < frames; f++)
        {
            held = lookaheadMin[f] < held ? lookaheadMin[f] : held + (lookaheadMin[f] - held) * release;
            smoothed[f] = held;
            sum += held;
            if (f >= window)
                sum -= smoothed[f - window];

            var envelope = (float)(sum / Math.Min(f + 1, window));
            for (var c = 0; c < channels; c++)
                interleaved[f * channels + c] *= envelope;
        }

        return limitedFrames;
    }

    private static void Scale(Span<float> samples, float gain)
    {
        for (var i = 0; i < samples.Length; i++)
            samples[i] *= gain;
    }
}
