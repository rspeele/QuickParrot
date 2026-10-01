namespace QuickParrot.Core.Dsp;

public static class AudioDownmixer
{
    /// <summary>Averages interleaved multi-channel samples down to mono.</summary>
    public static float[] ToMono(ReadOnlySpan<float> interleaved, int channels)
    {
        if (channels <= 0)
            throw new ArgumentOutOfRangeException(nameof(channels));

        if (channels == 1)
            return interleaved.ToArray();

        var mono = new float[interleaved.Length / channels];
        for (var i = 0; i < mono.Length; i++)
            mono[i] = Mean(interleaved.Slice(i * channels, channels));

        return mono;
    }

    /// <summary>The mono value of one interleaved frame: the mean of its channels.</summary>
    public static float Mean(ReadOnlySpan<float> frame)
    {
        float sum = 0;
        foreach (var sample in frame)
            sum += sample;

        return sum / frame.Length;
    }
}
