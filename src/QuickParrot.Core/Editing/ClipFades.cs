using QuickParrot.Core.Dsp;

namespace QuickParrot.Core.Editing;

/// <summary>Short raised-cosine fades at a clip's edges so it starts and stops without a click.</summary>
public static class ClipFades
{
    public static readonly TimeSpan DefaultLength = TimeSpan.FromMilliseconds(5);

    /// <summary>Fades in and out over <paramref name="fadeFrames"/> each, shortened to half the clip if it's tiny.</summary>
    public static void Apply(Span<float> interleaved, int channels, int fadeFrames)
    {
        var frames = interleaved.Length / channels;
        fadeFrames = Math.Min(fadeFrames, frames / 2);
        for (var i = 0; i < fadeFrames; i++)
        {
            var gain = (float)RaisedCosine.At(i / (double)fadeFrames);
            var head = i * channels;
            var tail = (frames - 1 - i) * channels;
            for (var c = 0; c < channels; c++)
            {
                interleaved[head + c] *= gain;
                interleaved[tail + c] *= gain;
            }
        }
    }
}
