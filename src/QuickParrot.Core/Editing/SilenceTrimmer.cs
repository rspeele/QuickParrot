namespace QuickParrot.Core.Editing;

/// <summary>Suggests a selection that drops leading and trailing near-silence, with a little padding kept.</summary>
public static class SilenceTrimmer
{
    private const double WindowSeconds = 0.01;
    private const double PadSeconds = 0.1;

    // Quieter than 45 dB below the loudest moment, or than -60 dBFS outright, counts as silence.
    private const double RelativeThresholdDb = -45;
    private const double AbsoluteThresholdDbfs = -60;

    public static ClipSelection Suggest(EditableAudio audio)
    {
        var window = Math.Max(1, (int)(audio.SampleRate * WindowSeconds));
        var windows = (audio.FrameCount + window - 1) / window;
        if (windows == 0)
            return ClipSelection.All(0);

        var rms = new double[windows];
        for (var w = 0; w < windows; w++)
        {
            var span = audio.Frames(w * window, Math.Min(audio.FrameCount, (w + 1) * window));
            var sum = 0.0;
            foreach (var s in span)
                sum += s * s;

            rms[w] = Math.Sqrt(sum / span.Length);
        }

        var loudest = rms.Max();
        var threshold = Math.Max(loudest * DbToLinear(RelativeThresholdDb), DbToLinear(AbsoluteThresholdDbfs));
        var first = Array.FindIndex(rms, r => r >= threshold);
        if (first < 0)
            return ClipSelection.All(audio.FrameCount);

        var last = Array.FindLastIndex(rms, r => r >= threshold);
        var pad = (int)(audio.SampleRate * PadSeconds);
        var start = Math.Max(0, first * window - pad);
        var end = Math.Min(audio.FrameCount, (last + 1) * window + pad);
        return new ClipSelection(start, end);
    }

    private static double DbToLinear(double db) => Math.Pow(10, db / 20);
}
