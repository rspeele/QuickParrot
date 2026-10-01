using QuickParrot.Core.Replay;

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
        _bytesPerSample = SampleConverter.BytesPerSample(format);
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
                    {
                        var at = frame * frameBytes + channel * _bytesPerSample;
                        sum += SampleConverter.Read(interleaved.Slice(at, _bytesPerSample), Format);
                    }
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
}
