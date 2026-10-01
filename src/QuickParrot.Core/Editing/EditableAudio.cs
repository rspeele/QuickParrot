namespace QuickParrot.Core.Editing;

/// <summary>Interleaved float audio for the trim editor, plus labels describing where it came from.</summary>
public sealed class EditableAudio
{
    public EditableAudio(float[] samples, int sampleRate, int channels, string? suggestedTitle = null, string? sourceLabel = null)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        if (samples.Length % channels != 0)
            throw new ArgumentException("Sample count must be a whole number of frames.", nameof(samples));

        Samples = samples;
        SampleRate = sampleRate;
        Channels = channels;
        SuggestedTitle = suggestedTitle;
        SourceLabel = sourceLabel;
    }

    public float[] Samples { get; }

    public int SampleRate { get; }

    public int Channels { get; }

    /// <summary>A starting point for the clip's name (e.g. the window title of what was playing), if known.</summary>
    public string? SuggestedTitle { get; }

    /// <summary>Where the audio came from, for display (e.g. "Grab at 21:04").</summary>
    public string? SourceLabel { get; }

    public int FrameCount => Samples.Length / Channels;

    public TimeSpan Duration => TimeSpan.FromSeconds(FrameCount / (double)SampleRate);

    public int FramesFor(TimeSpan duration) => (int)Math.Round(duration.TotalSeconds * SampleRate);

    public double SecondsAt(double frame) => frame / SampleRate;

    public ReadOnlySpan<float> Frames(int startFrame, int endFrame)
    {
        CheckRange(startFrame, endFrame);
        return Samples.AsSpan(startFrame * Channels, (endFrame - startFrame) * Channels);
    }

    /// <summary>Copies frames [start, end) into new audio with the same format and labels.</summary>
    public EditableAudio Slice(int startFrame, int endFrame) =>
        new(Frames(startFrame, endFrame).ToArray(), SampleRate, Channels, SuggestedTitle, SourceLabel);

    public EditableAudio WithSamples(float[] samples) => new(samples, SampleRate, Channels, SuggestedTitle, SourceLabel);

    private void CheckRange(int startFrame, int endFrame)
    {
        if (startFrame < 0 || endFrame > FrameCount || startFrame > endFrame)
            throw new ArgumentOutOfRangeException(nameof(startFrame), $"Frames [{startFrame}, {endFrame}) are outside 0..{FrameCount}.");
    }
}
