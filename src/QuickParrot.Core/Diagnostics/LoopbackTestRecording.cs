using System.Buffers.Binary;

namespace QuickParrot.Core.Diagnostics;

public enum LoopbackSampleFormat
{
    Float32,
    Pcm16,
    Pcm24,
    Pcm32,
}

/// <summary>
/// Collects captured packets as mono float samples (channels averaged), up to a maximum length. Thread-safe.
/// </summary>
public sealed class LoopbackTestRecording
{
    private readonly object _lock = new();
    private readonly float[] _samples;
    private readonly int _bytesPerSample;
    private int _count;

    public LoopbackTestRecording(int sampleRate, int channels, LoopbackSampleFormat format, TimeSpan maxDuration)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        SampleRate = sampleRate;
        Channels = channels;
        Format = format;
        _bytesPerSample = format switch
        {
            LoopbackSampleFormat.Pcm16 => 2,
            LoopbackSampleFormat.Pcm24 => 3,
            _ => 4,
        };
        _samples = new float[(int)Math.Ceiling(maxDuration.TotalSeconds * sampleRate)];
    }

    public int SampleRate { get; }

    public int Channels { get; }

    public LoopbackSampleFormat Format { get; }

    public int Count
    {
        get
        {
            lock (_lock)
                return _count;
        }
    }

    public bool IsFull => Count == _samples.Length;

    /// <summary>Appends interleaved frames; a packet flagged silent contributes zeros. Frames past the cap are dropped.</summary>
    public void Append(ReadOnlySpan<byte> interleaved, bool silent)
    {
        var frameBytes = _bytesPerSample * Channels;
        var frames = interleaved.Length / frameBytes;
        lock (_lock)
        {
            frames = Math.Min(frames, _samples.Length - _count);
            for (var frame = 0; frame < frames; frame++)
            {
                var sum = 0f;
                if (!silent)
                {
                    for (var channel = 0; channel < Channels; channel++)
                        sum += Read(interleaved.Slice(frame * frameBytes + channel * _bytesPerSample, _bytesPerSample));
                }

                _samples[_count++] = sum / Channels;
            }
        }
    }

    public float[] ToArray()
    {
        lock (_lock)
            return _samples.AsSpan(0, _count).ToArray();
    }

    private float Read(ReadOnlySpan<byte> sample) => Format switch
    {
        LoopbackSampleFormat.Float32 => BinaryPrimitives.ReadSingleLittleEndian(sample),
        LoopbackSampleFormat.Pcm16 => BinaryPrimitives.ReadInt16LittleEndian(sample) / 32768f,
        LoopbackSampleFormat.Pcm24 => ((sample[2] << 24) | (sample[1] << 16) | (sample[0] << 8)) / 2147483648f,
        _ => BinaryPrimitives.ReadInt32LittleEndian(sample) / 2147483648f,
    };
}
