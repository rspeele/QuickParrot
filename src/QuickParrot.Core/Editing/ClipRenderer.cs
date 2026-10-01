using QuickParrot.Core.Playback;

namespace QuickParrot.Core.Editing;

/// <param name="Normalize">Loudness-normalize (and peak-limit) the clip; off leaves levels untouched.</param>
public sealed record ClipRenderOptions(bool Normalize, LoudnessOptions Loudness);

/// <summary>
/// Turns a selection into the audio that gets saved: copied out, folded down to stereo if it's surround (so loudness
/// is measured on what's actually saved), faded at the edges, and normalized.
/// </summary>
public static class ClipRenderer
{
    public static EditableAudio Render(EditableAudio source, ClipSelection selection, ClipRenderOptions options)
    {
        var channels = Math.Min(source.Channels, 2);
        var samples = CopyOut(source.Frames(selection.Start, selection.End), source.Channels);
        ClipFades.Apply(samples, channels, source.FramesFor(ClipFades.DefaultLength));
        if (options.Normalize)
            LoudnessNormalizer.Normalize(samples, channels, source.SampleRate, options.Loudness);

        return new EditableAudio(samples, source.SampleRate, channels, source.SuggestedTitle, source.SourceLabel);
    }

    // The selection's one copy, which Render owns and edits in place; surround is mixed straight down to stereo.
    private static float[] CopyOut(ReadOnlySpan<float> frames, int channels)
    {
        if (channels <= 2)
            return frames.ToArray();

        var mixer = new ChannelMixer(channels, 2);
        var stereo = new float[frames.Length / channels * 2];
        for (var f = 0; f < stereo.Length / 2; f++)
            mixer.MixFrame(frames.Slice(f * channels, channels), stereo.AsSpan(f * 2, 2));

        return stereo;
    }
}
