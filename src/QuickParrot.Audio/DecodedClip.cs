using NAudio.Wave;

namespace QuickParrot.Audio;

/// <summary>A clip decoded fully into interleaved float samples, shareable by any number of outputs.</summary>
internal sealed class DecodedClip
{
    // Guards against someone pointing a hotkey at an hour-long file and exhausting memory.
    private static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(10);

    private DecodedClip(string fullPath, float[] samples, WaveFormat format)
    {
        FullPath = fullPath;
        Samples = samples;
        Format = format;
    }

    public string FullPath { get; }

    public float[] Samples { get; }

    public WaveFormat Format { get; }

    public static DecodedClip Decode(string fullPath)
    {
        try
        {
            return DecodeCore(fullPath);
        }
        catch (Exception e) when (e is not (IOException or UnauthorizedAccessException or OutOfMemoryException
            or InvalidDataException))
        {
            throw new InvalidDataException("The file isn't a supported audio format.", e); // e.g. raw COM/MF errors
        }
    }

    private static DecodedClip DecodeCore(string fullPath)
    {
        using var reader = new AudioFileReader(fullPath);
        var format = WaveFormat.CreateIeeeFloatWaveFormat(reader.WaveFormat.SampleRate, reader.WaveFormat.Channels);
        var maxSamples = (long)(MaxDuration.TotalSeconds * format.SampleRate * format.Channels);
        if (reader.Length / sizeof(float) > maxSamples)
            throw new InvalidDataException($"Clips longer than {MaxDuration.TotalMinutes:0} minutes aren't supported.");

        var samples = new float[Math.Max(reader.Length / sizeof(float) + format.Channels, 4096)];
        var count = 0;
        while (true)
        {
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

        Array.Resize(ref samples, count - count % format.Channels);
        return new DecodedClip(fullPath, samples, format);
    }
}
