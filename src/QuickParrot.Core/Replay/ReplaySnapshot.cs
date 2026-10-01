using QuickParrot.Core.Playback;

namespace QuickParrot.Core.Replay;

/// <summary>A copy of recent replay audio: interleaved float frames, oldest first. Read-only once built.</summary>
public sealed record ReplaySnapshot(ReadOnlyMemory<float> Samples, int SampleRate, int Channels)
{
    /// <summary>Peaks below this (about -100 dB) count as silence.</summary>
    public const float SilenceThreshold = 1e-5f;

    public static readonly ReplaySnapshot Empty = new(ReadOnlyMemory<float>.Empty, 0, 0);

    public int Frames => Channels == 0 ? 0 : Samples.Length / Channels;

    public TimeSpan Duration => SampleRate == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds((double)Frames / SampleRate);

    public bool IsSilent
    {
        get
        {
            foreach (var sample in Samples.Span)
            {
                if (Math.Abs(sample) > SilenceThreshold)
                    return false;
            }

            return true;
        }
    }

    /// <summary>Surround captures are folded to stereo, which is all a soundboard clip needs.</summary>
    public ReplaySnapshot DownmixedToStereo()
    {
        if (Channels <= 2)
            return this;

        var mixer = new ChannelMixer(Channels, 2);
        var samples = Samples.Span;
        var output = new float[Frames * 2];
        for (var frame = 0; frame < Frames; frame++)
            mixer.MixFrame(samples.Slice(frame * Channels, Channels), output.AsSpan(frame * 2, 2));

        return new ReplaySnapshot(output, SampleRate, 2);
    }
}
