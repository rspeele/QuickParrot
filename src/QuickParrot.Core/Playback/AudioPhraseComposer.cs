using QuickParrot.Core.Dsp;

namespace QuickParrot.Core.Playback;

public sealed record AudioPhrasePart(ReadOnlyMemory<float> Samples, int SampleRate, int Channels);

public static class AudioPhraseComposer
{
    public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(10);

    public static AudioPhrasePart Compose(IReadOnlyList<AudioPhrasePart> parts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Count == 0)
            throw new ArgumentException("A phrase must contain at least one fragment.", nameof(parts));

        var sampleRate = 0;
        var channels = 1;
        var duration = 0d;
        foreach (var part in parts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentOutOfRangeException.ThrowIfLessThan(part.SampleRate, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(part.Channels, 1);
            if (part.Samples.Length == 0 || part.Samples.Length % part.Channels != 0)
                throw new ArgumentException("Each fragment must contain complete audio frames.", nameof(parts));

            sampleRate = Math.Max(sampleRate, part.SampleRate);
            channels = Math.Max(channels, Math.Min(part.Channels, 2));
            duration += (double)(part.Samples.Length / part.Channels) / part.SampleRate;
        }

        if (duration > MaxDuration.TotalSeconds)
            throw new InvalidDataException($"Phrases longer than {MaxDuration.TotalMinutes:0} minutes aren't supported.");

        var frameCounts = parts.Select(part => checked((int)Math.Round(
            (part.Samples.Length / part.Channels) / ((double)part.SampleRate / sampleRate)))).ToArray();
        var samples = new float[checked((int)(frameCounts.Sum(count => (long)count) * channels))];
        var offset = 0;
        for (var i = 0; i < parts.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var part = parts[i];
            var target = samples.AsSpan(offset, frameCounts[i] * channels);
            if (part.SampleRate == sampleRate && part.Channels == channels)
            {
                part.Samples.Span.CopyTo(target);
            }
            else
            {
                Convert(part, target, sampleRate, channels, cancellationToken);
            }

            offset += target.Length;
        }

        return new AudioPhrasePart(samples, sampleRate, channels);
    }

    private static void Convert(AudioPhrasePart part, Span<float> target, int sampleRate, int channels,
        CancellationToken cancellationToken)
    {
        var frames = part.Samples.Length / part.Channels;
        var mapped = new float[frames * channels];
        var mixer = new ChannelMixer(part.Channels, channels);
        for (var frame = 0; frame < frames; frame++)
        {
            if (frame % 4096 == 0)
                cancellationToken.ThrowIfCancellationRequested();

            mixer.MixFrame(part.Samples.Span.Slice(frame * part.Channels, part.Channels),
                mapped.AsSpan(frame * channels, channels));
        }

        if (part.SampleRate == sampleRate)
        {
            mapped.CopyTo(target);
            return;
        }

        var sourceChannel = new float[frames];
        for (var channel = 0; channel < channels; channel++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var frame = 0; frame < frames; frame++)
                sourceChannel[frame] = mapped[frame * channels + channel];

            var converted = AudioResampler.Resample(sourceChannel, part.SampleRate, sampleRate, cancellationToken);
            for (var frame = 0; frame < converted.Length; frame++)
                target[frame * channels + channel] = converted[frame];
        }
    }
}
