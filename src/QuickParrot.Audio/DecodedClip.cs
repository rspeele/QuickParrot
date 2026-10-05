using NAudio.Wave;
using QuickParrot.Core.Playback;

namespace QuickParrot.Audio;

/// <summary>A clip decoded fully into interleaved float samples, shareable by any number of outputs.</summary>
internal sealed class DecodedClip
{
    // Guards against someone pointing a hotkey at an hour-long file and exhausting memory.
    private static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(10);

    private DecodedClip(string fullPath, ReadOnlyMemory<float> samples, WaveFormat format)
    {
        FullPath = fullPath;
        Samples = samples;
        Format = format;
    }

    public string FullPath { get; }

    public ReadOnlyMemory<float> Samples { get; }

    public WaveFormat Format { get; }

    public static DecodedClip Decode(string fullPath, CancellationToken cancellationToken = default)
    {
        try
        {
            return DecodeCore(new AudioFileReader(fullPath), fullPath, cancellationToken);
        }
        catch (Exception e) when (e is not (IOException or UnauthorizedAccessException or OutOfMemoryException
            or InvalidDataException or OperationCanceledException))
        {
            throw new InvalidDataException("The file isn't a supported audio format.", e); // e.g. raw COM/MF errors
        }
    }

    public static DecodedClip DecodePhrase(IReadOnlyList<string> fullPaths, CancellationToken cancellationToken)
    {
        if (fullPaths.Count == 0)
            throw new ArgumentException("A phrase must contain at least one fragment.", nameof(fullPaths));

        var parts = new List<AudioPhrasePart>();
        var duration = 0d;
        foreach (var fullPath in fullPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var clip = Decode(fullPath, cancellationToken);
            duration += (double)(clip.Samples.Length / clip.Format.Channels) / clip.Format.SampleRate;
            if (duration > AudioPhraseComposer.MaxDuration.TotalSeconds)
                throw new InvalidDataException($"Phrases longer than {AudioPhraseComposer.MaxDuration.TotalMinutes:0} minutes aren't supported.");

            parts.Add(new AudioPhrasePart(clip.Samples, clip.Format.SampleRate, clip.Format.Channels));
        }

        var phrase = AudioPhraseComposer.Compose(parts, cancellationToken);
        return new DecodedClip(fullPaths[0], phrase.Samples,
            WaveFormat.CreateIeeeFloatWaveFormat(phrase.SampleRate, phrase.Channels));
    }

    /// <summary>Decodes silent MP3 from memory, loading the same Media Foundation decoder a real clip needs.</summary>
    public static DecodedClip DecodeWarmUpClip() =>
        DecodeCore(new AudioFileReader(new MemoryStream(SilentMp3())), "warm-up", CancellationToken.None);

    private static DecodedClip DecodeCore(AudioFileReader source, string fullPath, CancellationToken cancellationToken)
    {
        using var reader = source;
        var format = WaveFormat.CreateIeeeFloatWaveFormat(reader.WaveFormat.SampleRate, reader.WaveFormat.Channels);
        var maxSamples = (long)(MaxDuration.TotalSeconds * format.SampleRate * format.Channels);
        if (reader.Length / sizeof(float) > maxSamples)
            throw new InvalidDataException($"Clips longer than {MaxDuration.TotalMinutes:0} minutes aren't supported.");

        var samples = new float[Math.Max(reader.Length / sizeof(float) + format.Channels, 4096)];
        var count = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (count == samples.Length)
            {
                if (samples.Length >= maxSamples)
                    throw new InvalidDataException($"Clips longer than {MaxDuration.TotalMinutes:0} minutes aren't supported.");

                Array.Resize(ref samples, (int)Math.Min(samples.Length * 2L, maxSamples));
            }

            var read = reader.Read(samples.AsSpan(count));
            if (read == 0)
                break;

            count += read;
        }

        if (count == 0)
            throw new InvalidDataException("The file contains no audio.");

        return new DecodedClip(fullPath, samples.AsMemory(0, count - count % format.Channels), format);
    }

    // Half a second of MPEG-1 Layer III frames (128 kbps, 44.1 kHz, mono) whose all-zero side info decodes as silence.
    private static byte[] SilentMp3()
    {
        const int frameSize = 417;
        var bytes = new byte[frameSize * 20];
        for (var frame = 0; frame < bytes.Length; frame += frameSize)
            ((ReadOnlySpan<byte>)[0xFF, 0xFB, 0x90, 0xC0]).CopyTo(bytes.AsSpan(frame));

        return bytes;
    }
}
