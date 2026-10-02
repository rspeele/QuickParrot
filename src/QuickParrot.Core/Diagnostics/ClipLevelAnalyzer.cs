using QuickParrot.Core.Dsp;

namespace QuickParrot.Core.Diagnostics;

/// <summary>
/// Compares the short-window level of a cable recording with the clip that was played into it, aligned by onset,
/// to tell whether something turned the clip down after it started.
/// </summary>
public static class ClipLevelAnalyzer
{
    private const double WindowSeconds = 0.05;
    private const double HopSeconds = 0.01;
    private const int WindowHops = 5;
    private const double SilenceDb = -120;
    private const double NothingThroughDb = -70;
    private const double DropThresholdDb = 6;
    private const double StoppedRangeDb = 45; // this far below the recording's peak counts as nothing
    private const double OnsetRangeDb = 30;
    private const double ActiveRangeDb = 25;
    private const double BaselineSeconds = 0.3;
    private const int SmoothingHalfWidth = 12; // windows either side: a ~250 ms running median
    private const int MinActiveWindows = 20;

    /// <param name="searchFrom">Where in the recording the clip could first appear, so audio before it is ignored.</param>
    public static ClipLevelAnalysis Analyze(MonoAudio source, MonoAudio recording, TimeSpan searchFrom)
    {
        if (IsSilent(source.Samples))
            return new ClipLevelAnalysis(ClipLevelVerdict.SilentClip);

        var recordedLevels = Envelope(recording);
        var from = Math.Clamp((int)(searchFrom.TotalSeconds / HopSeconds), 0, recordedLevels.Length);
        var recordedPeak = Max(recordedLevels, from);
        if (recordedPeak < NothingThroughDb)
            return new ClipLevelAnalysis(ClipLevelVerdict.NothingCameThrough);

        // Only the part of the clip the recording could hold, so a capped recording isn't judged against the rest.
        var sourceLevels = Envelope(Trim(source, recording.Duration - searchFrom));
        var sourcePeak = Max(sourceLevels, 0);
        if (sourcePeak < NothingThroughDb)
            return new ClipLevelAnalysis(ClipLevelVerdict.TooShortToJudge);

        var sourceOnset = FirstAtOrAbove(sourceLevels, 0, sourcePeak - OnsetRangeDb);
        var recordedOnset = FirstAtOrAbove(recordedLevels, from, recordedPeak - OnsetRangeDb);
        var overlap = Math.Min(sourceLevels.Length - sourceOnset, recordedLevels.Length - recordedOnset);
        var differences = new List<(int Window, double Db)>();
        for (var i = 0; i < overlap; i++)
        {
            if (sourceLevels[sourceOnset + i] >= sourcePeak - ActiveRangeDb)
                differences.Add((i, recordedLevels[recordedOnset + i] - sourceLevels[sourceOnset + i]));
        }

        if (differences.Count < MinActiveWindows)
        {
            return new ClipLevelAnalysis(
                ClipLevelVerdict.TooShortToJudge, differences.Count > 0 ? Median(differences.Select(d => d.Db)) : null);
        }

        var baselineWindows = (int)(BaselineSeconds / HopSeconds);
        var early = differences.Where(d => d.Window < baselineWindows).Select(d => d.Db).ToList();
        var baseline = Median(early.Count >= 5 ? early : differences.Take(10).Select(d => d.Db));

        var smoothed = RunningMedian(differences.Select(d => d.Db).ToArray(), SmoothingHalfWidth);
        for (var k = 0; k < smoothed.Length; k++)
        {
            if (smoothed[k] >= baseline - DropThresholdDb)
                continue;

            var at = differences[k].Window;
            var after = TimeSpan.FromSeconds(at * HopSeconds);
            var restStart = Math.Min(recordedOnset + at + WindowHops, recordedLevels.Length);
            var rest = recordedLevels.AsSpan(
                restStart, Math.Clamp(overlap - at - WindowHops, 0, recordedLevels.Length - restStart));
            if (!rest.IsEmpty && Max(rest) < recordedPeak - StoppedRangeDb)
                return new ClipLevelAnalysis(ClipLevelVerdict.StoppedEarly, baseline, DropAfter: after);

            return new ClipLevelAnalysis(ClipLevelVerdict.Dropped, baseline, baseline - smoothed.AsSpan(k).ToArray().Min(), after);
        }

        return new ClipLevelAnalysis(ClipLevelVerdict.NoDrop, baseline);
    }

    // RMS level in dB of overlapping windows, one every HopSeconds.
    private static double[] Envelope(MonoAudio audio)
    {
        var samples = audio.Samples;
        var window = Math.Max(1, (int)Math.Round(WindowSeconds * audio.SampleRate));
        var hop = HopSeconds * audio.SampleRate;
        if (samples.Length < window || hop <= 0)
            return [];

        var levels = new double[(int)((samples.Length - window) / hop) + 1];
        for (var w = 0; w < levels.Length; w++)
        {
            var start = Math.Min((int)Math.Round(w * hop), samples.Length - window);
            double sum = 0;
            foreach (var sample in samples.AsSpan(start, window))
                sum += (double)sample * sample;

            levels[w] = Decibels.FromPower(sum / window, SilenceDb);
        }

        return levels;
    }

    private static MonoAudio Trim(MonoAudio audio, TimeSpan length)
    {
        var count = (int)Math.Clamp(length.TotalSeconds * audio.SampleRate, 0, audio.Samples.Length);
        return count == audio.Samples.Length ? audio : audio with { Samples = audio.Samples[..count] };
    }

    private static bool IsSilent(float[] samples)
    {
        var threshold = Decibels.ToAmplitude(NothingThroughDb);
        foreach (var sample in samples)
        {
            if (Math.Abs(sample) >= threshold)
                return false;
        }

        return true;
    }

    private static double Max(ReadOnlySpan<double> levels)
    {
        var max = SilenceDb;
        foreach (var level in levels)
            max = Math.Max(max, level);

        return max;
    }

    private static double Max(double[] levels, int from) => Max(levels.AsSpan(from));

    private static int FirstAtOrAbove(double[] levels, int from, double threshold)
    {
        for (var i = from; i < levels.Length; i++)
        {
            if (levels[i] >= threshold)
                return i;
        }

        return levels.Length;
    }

    private static double[] RunningMedian(double[] values, int halfWidth)
    {
        var result = new double[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            var start = Math.Max(0, i - halfWidth);
            var end = Math.Min(values.Length, i + halfWidth + 1);
            result[i] = Median(values[start..end]);
        }

        return result;
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }
}
