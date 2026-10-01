namespace QuickParrot.Core.Editing;

/// <summary>Nudges selection edges to the quietest nearby point, so cuts land in gaps rather than mid-waveform.</summary>
public static class QuietPointSnapper
{
    public static readonly TimeSpan DefaultRadius = TimeSpan.FromMilliseconds(5);

    // Energy is judged over ±1 ms, then the edge lands on the sample nearest zero within that quiet stretch.
    private const double EnergyHalfWindowSeconds = 0.001;

    public static ClipSelection Snap(EditableAudio audio, ClipSelection selection, TimeSpan radius, int minLength)
    {
        var radiusFrames = audio.FramesFor(radius);
        var start = Snap(audio, selection.Start, radiusFrames);
        var end = Snap(audio, selection.End, radiusFrames);
        return end - start >= Math.Min(minLength, audio.FrameCount) ? new ClipSelection(start, end) : selection;
    }

    /// <summary>The frame boundary within ±<paramref name="radiusFrames"/> of <paramref name="frame"/> with the least energy around it.</summary>
    public static int Snap(EditableAudio audio, int frame, int radiusFrames)
    {
        var total = audio.FrameCount;
        frame = Math.Clamp(frame, 0, total);
        if (radiusFrames <= 0 || total == 0)
            return frame;

        var halfWindow = Math.Max(1, (int)(audio.SampleRate * EnergyHalfWindowSeconds));
        var lo = Math.Max(0, frame - radiusFrames);
        var hi = Math.Min(total, frame + radiusFrames);
        var origin = lo - halfWindow;
        var magnitudes = new double[hi - lo + 2 * halfWindow + 1];
        for (var i = 0; i < magnitudes.Length; i++)
            magnitudes[i] = Math.Abs(MonoAt(audio, origin + i));

        var prefix = new double[magnitudes.Length + 1];
        for (var i = 0; i < magnitudes.Length; i++)
            prefix[i + 1] = prefix[i] + magnitudes[i];

        var best = frame;
        var bestScore = double.MaxValue;
        for (var c = lo; c <= hi; c++)
        {
            var i = c - origin;
            var energy = prefix[i + halfWindow + 1] - prefix[i - halfWindow];
            var score = energy * (1 + 0.1 * Math.Abs(c - frame) / radiusFrames); // mild preference for staying put
            if (score < bestScore || (score == bestScore && Math.Abs(c - frame) < Math.Abs(best - frame)))
            {
                bestScore = score;
                best = c;
            }
        }

        var nearestZero = best;
        for (var c = Math.Max(lo, best - halfWindow); c <= Math.Min(hi, best + halfWindow); c++)
        {
            if (magnitudes[c - origin] < magnitudes[nearestZero - origin])
                nearestZero = c;
        }

        return nearestZero;
    }

    // Frames outside the audio count as silence, so the very start and end are natural cut points.
    private static double MonoAt(EditableAudio audio, int frame)
    {
        if (frame < 0 || frame >= audio.FrameCount)
            return 0;

        var sum = 0.0;
        var offset = frame * audio.Channels;
        for (var c = 0; c < audio.Channels; c++)
            sum += audio.Samples[offset + c];

        return sum / audio.Channels;
    }
}
