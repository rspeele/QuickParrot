namespace QuickParrot.Core.Naming;

public static class AudioDownmixer
{
    /// <summary>Averages interleaved multi-channel samples down to mono.</summary>
    public static float[] ToMono(ReadOnlySpan<float> interleaved, int channels)
    {
        if (channels <= 0)
            throw new ArgumentOutOfRangeException(nameof(channels));

        if (channels == 1)
            return interleaved.ToArray();

        var frames = interleaved.Length / channels;
        var mono = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            float sum = 0;
            var baseIndex = i * channels;
            for (var c = 0; c < channels; c++)
                sum += interleaved[baseIndex + c];

            mono[i] = sum / channels;
        }

        return mono;
    }
}
