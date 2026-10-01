using QuickParrot.Core.Playback;

namespace QuickParrot.Core.Editing;

/// <param name="Normalize">Loudness-normalize (and peak-limit) the clip; off leaves levels untouched.</param>
public sealed record ClipRenderOptions(bool Normalize, LoudnessOptions Loudness);

/// <param name="Normalization">Null when normalization was off.</param>
public sealed record RenderedClip(EditableAudio Audio, NormalizationResult? Normalization);

/// <summary>
/// Turns a selection into the audio that gets saved: copied out, folded down to stereo if it's surround (so loudness
/// is measured on what's actually saved), faded at the edges, and normalized.
/// </summary>
public static class ClipRenderer
{
    public static RenderedClip Render(EditableAudio source, ClipSelection selection, ClipRenderOptions options)
    {
        var clip = source.Slice(selection.Start, selection.End);
        if (clip.Channels > 2)
            clip = DownmixToStereo(clip);

        ClipFades.Apply(clip.Samples, clip.Channels, clip.FramesFor(ClipFades.DefaultLength));
        var normalization = options.Normalize
            ? LoudnessNormalizer.Normalize(clip.Samples, clip.Channels, clip.SampleRate, options.Loudness)
            : null;
        return new RenderedClip(clip, normalization);
    }

    private static EditableAudio DownmixToStereo(EditableAudio clip)
    {
        var mixer = new ChannelMixer(clip.Channels, 2);
        var stereo = new float[clip.FrameCount * 2];
        for (var f = 0; f < clip.FrameCount; f++)
            mixer.MixFrame(clip.Samples.AsSpan(f * clip.Channels, clip.Channels), stereo.AsSpan(f * 2, 2));

        return new EditableAudio(stereo, clip.SampleRate, 2, clip.SuggestedTitle, clip.SourceLabel);
    }
}
