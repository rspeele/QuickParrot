using QuickParrot.Core.Dsp;
using QuickParrot.Core.Replay;

namespace QuickParrot.Core.Diagnostics;

/// <summary>
/// Collects captured packets as mono float samples (channels averaged), up to a maximum length. Thread-safe.
/// </summary>
public sealed class LoopbackTestRecording
{
    private readonly object _lock = new();
    private readonly float[] _samples;
    private readonly float[] _frame; // scratch for one frame's channels, used under the lock
    private readonly int _bytesPerSample;
    private int _count;

    public LoopbackTestRecording(int sampleRate, int channels, SampleFormat format, TimeSpan maxDuration)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        SampleRate = sampleRate;
        Channels = channels;
        Format = format;
        _bytesPerSample = SampleConverter.BytesPerSample(format);
        _samples = new float[(int)Math.Ceiling(maxDuration.TotalSeconds * sampleRate)];
        _frame = new float[channels];
    }

    public int SampleRate { get; }

    public int Channels { get; }

    public SampleFormat Format { get; }

    public int Count
    {
        get
        {
            lock (_lock)
                return _count;
        }
    }

    internal bool IsFull => Count == _samples.Length;

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
                if (silent)
                {
                    _samples[_count++] = 0;
                    continue;
                }

                SampleConverter.ToFloat(interleaved.Slice(frame * frameBytes, frameBytes), Format, _frame);
                _samples[_count++] = AudioDownmixer.Mean(_frame);
            }
        }
    }

    public float[] ToArray()
    {
        lock (_lock)
            return _samples.AsSpan(0, _count).ToArray();
    }
}
