namespace QuickParrot.Core.Editing;

/// <summary>
/// A min/max peak pyramid over all channels, so any zoom level can be drawn without rescanning the samples.
/// Level 0 summarises <see cref="BaseBucketFrames"/> frames per bucket; each level above is <see cref="LevelFactor"/>× coarser.
/// </summary>
public sealed class WaveformPeaks
{
    public const int BaseBucketFrames = 32;
    public const int LevelFactor = 4;

    private readonly EditableAudio _audio;
    private readonly List<(float[] Min, float[] Max)> _levels = [];

    public WaveformPeaks(EditableAudio audio)
    {
        _audio = audio;
        var (min, max) = BuildBase(audio);
        _levels.Add((min, max));
        while (min.Length > 1)
        {
            (min, max) = Coarsen(min, max);
            _levels.Add((min, max));
        }
    }

    public int FrameCount => _audio.FrameCount;

    internal int LevelCount => _levels.Count;

    public static int BucketFrames(int level) => BaseBucketFrames * (int)Math.Pow(LevelFactor, level);

    /// <summary>
    /// Fills one min/max pair per column, column i covering frames [first + i·framesPerColumn, first + (i+1)·framesPerColumn).
    /// Columns outside the audio get NaN. Bucket edges may widen a column by up to one bucket smaller than the column.
    /// </summary>
    public void GetColumns(double firstFrame, double framesPerColumn, Span<float> mins, Span<float> maxs)
    {
        if (mins.Length != maxs.Length)
            throw new ArgumentException("Min and max spans must be the same length.");
        if (framesPerColumn <= 0)
            throw new ArgumentOutOfRangeException(nameof(framesPerColumn));

        var level = PickLevel(framesPerColumn);
        for (var i = 0; i < mins.Length; i++)
        {
            var a = firstFrame + i * framesPerColumn;
            var b = a + framesPerColumn;
            var start = (int)Math.Max(0, Math.Floor(a));
            var end = (int)Math.Min(FrameCount, Math.Ceiling(b));
            if (end <= start)
            {
                if (start < FrameCount && b > 0)
                    end = start + 1; // a column narrower than a frame still shows that frame
                else
                {
                    mins[i] = maxs[i] = float.NaN;
                    continue;
                }
            }

            (mins[i], maxs[i]) = level < 0 ? ScanRaw(start, end) : ScanLevel(level, start, end);
        }
    }

    private int PickLevel(double framesPerColumn)
    {
        var level = -1;
        while (level + 1 < _levels.Count && BucketFrames(level + 1) <= framesPerColumn)
            level++;

        return level;
    }

    private (float Min, float Max) ScanRaw(int startFrame, int endFrame)
    {
        var span = _audio.Frames(startFrame, endFrame);
        float min = float.MaxValue, max = float.MinValue;
        foreach (var s in span)
        {
            if (s < min) min = s;
            if (s > max) max = s;
        }

        return (min, max);
    }

    private (float Min, float Max) ScanLevel(int level, int startFrame, int endFrame)
    {
        var (mins, maxs) = _levels[level];
        var size = BucketFrames(level);
        var first = startFrame / size;
        var last = Math.Min(mins.Length, (endFrame + size - 1) / size);
        float min = float.MaxValue, max = float.MinValue;
        for (var i = first; i < last; i++)
        {
            if (mins[i] < min) min = mins[i];
            if (maxs[i] > max) max = maxs[i];
        }

        return (min, max);
    }

    private static (float[] Min, float[] Max) BuildBase(EditableAudio audio)
    {
        var buckets = Math.Max(1, (audio.FrameCount + BaseBucketFrames - 1) / BaseBucketFrames);
        var min = new float[buckets];
        var max = new float[buckets];
        var samples = audio.Samples.Span;
        var samplesPerBucket = BaseBucketFrames * audio.Channels;
        for (var b = 0; b < buckets; b++)
        {
            var from = b * samplesPerBucket;
            var to = Math.Min(samples.Length, from + samplesPerBucket);
            float lo = 0, hi = 0;
            if (from < to)
            {
                lo = hi = samples[from];
                for (var i = from + 1; i < to; i++)
                {
                    var s = samples[i];
                    if (s < lo) lo = s;
                    if (s > hi) hi = s;
                }
            }

            min[b] = lo;
            max[b] = hi;
        }

        return (min, max);
    }

    private static (float[] Min, float[] Max) Coarsen(float[] min, float[] max)
    {
        var count = (min.Length + LevelFactor - 1) / LevelFactor;
        var outMin = new float[count];
        var outMax = new float[count];
        for (var b = 0; b < count; b++)
        {
            var from = b * LevelFactor;
            var to = Math.Min(min.Length, from + LevelFactor);
            float lo = min[from], hi = max[from];
            for (var i = from + 1; i < to; i++)
            {
                if (min[i] < lo) lo = min[i];
                if (max[i] > hi) hi = max[i];
            }

            outMin[b] = lo;
            outMax[b] = hi;
        }

        return (outMin, outMax);
    }
}
