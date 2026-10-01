using System.Diagnostics;

namespace QuickParrot.Core.Replay;

/// <summary>
/// A ring of recent audio on a real-time timeline: packets are placed by their QPC timestamps and gaps become silence,
/// since loopback delivers nothing while nothing plays. One writer thread; snapshots from any thread.
/// </summary>
public sealed class ReplayBuffer : IReplaySource
{
    public static readonly TimeSpan MinCapacity = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaxCapacity = TimeSpan.FromMinutes(5);

    /// <summary>Smaller gaps between packets are timestamp jitter, not missing audio.</summary>
    public static readonly TimeSpan JitterTolerance = TimeSpan.FromMilliseconds(30);

    /// <summary>
    /// Longer than any normal capture latency, so a snapshot only adds trailing silence once packets have really
    /// stopped arriving.
    /// </summary>
    public static readonly TimeSpan IdleThreshold = TimeSpan.FromMilliseconds(250);

    private const int MaxSnapshotAttempts = 3;

    private readonly object _lock = new();
    private readonly Func<long> _clock;
    private TimeSpan _capacity;
    private float[] _ring = [];
    private int _sampleRate;
    private int _channels;
    private int _capacityFrames;
    private long _totalFrames; // frames written since the timeline began, silence included
    private int _validFrames; // how many of the newest frames the ring actually holds
    private long _endTicks; // timeline position just after the last written frame
    private int _generation; // bumped whenever the ring is replaced or cleared

    /// <param name="clock">Now, in the same 100 ns units as packet timestamps; defaults to <see cref="QpcNow"/>.</param>
    public ReplayBuffer(TimeSpan capacity, Func<long>? clock = null)
    {
        _capacity = ClampCapacity(capacity);
        _clock = clock ?? QpcNow;
    }

    public TimeSpan Capacity
    {
        get
        {
            lock (_lock)
                return _capacity;
        }
        set
        {
            var capacity = ClampCapacity(value);
            lock (_lock)
            {
                if (capacity == _capacity)
                    return;

                _capacity = capacity;
                if (_channels > 0)
                    Reallocate();
            }
        }
    }

    /// <summary>The current format, or (0, 0) before <see cref="Begin"/> or after <see cref="Clear"/>.</summary>
    internal (int SampleRate, int Channels) Format
    {
        get
        {
            lock (_lock)
                return (_sampleRate, _channels);
        }
    }

    /// <summary>The performance counter in 100 ns units, the clock WASAPI capture timestamps use.</summary>
    public static long QpcNow() =>
        (long)((Int128)Stopwatch.GetTimestamp() * TimeSpan.TicksPerSecond / Stopwatch.Frequency);

    /// <summary>
    /// Prepares for packets in this format. The same format keeps the audio so far (the time since its last packet
    /// becomes silence), e.g. across a capture restart; a different one starts an empty timeline now.
    /// </summary>
    public void Begin(int sampleRate, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        lock (_lock)
        {
            if (sampleRate == _sampleRate && channels == _channels)
                return;

            _sampleRate = sampleRate;
            _channels = channels;
            _ring = [];
            _capacityFrames = 0;
            _totalFrames = 0;
            _validFrames = 0;
            _endTicks = _clock();
            Reallocate();
        }
    }

    /// <summary>Forgets everything and frees the memory; packets are ignored until the next <see cref="Begin"/>.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _ring = [];
            _sampleRate = 0;
            _channels = 0;
            _capacityFrames = 0;
            _totalFrames = 0;
            _validFrames = 0;
            _generation++;
        }
    }

    /// <summary>Appends interleaved frames captured at <paramref name="timestamp"/>, in 100 ns units.</summary>
    public void Write(ReadOnlySpan<float> interleaved, long timestamp, ReplayPacketFlags flags = ReplayPacketFlags.None)
    {
        lock (_lock)
        {
            if (_channels == 0)
                return;

            var frames = interleaved.Length / _channels;
            if (frames == 0)
                return;

            var duration = FramesToTicks(frames);
            if (flags.HasFlag(ReplayPacketFlags.TimestampError) || timestamp <= 0)
            {
                _endTicks += duration;
            }
            else
            {
                var gap = timestamp - _endTicks;
                var tolerance = flags.HasFlag(ReplayPacketFlags.Discontinuity) ? 0 : JitterTolerance.Ticks;
                if (gap > tolerance)
                    WriteFrames(ReadOnlySpan<float>.Empty, TicksToFrames(gap));

                // Re-anchoring on every packet keeps clock drift from accumulating.
                _endTicks = timestamp + duration;
            }

            var data = flags.HasFlag(ReplayPacketFlags.Silent)
                ? ReadOnlySpan<float>.Empty
                : interleaved[..(frames * _channels)];
            WriteFrames(data, frames);
        }
    }

    public ReplaySnapshot Snapshot(TimeSpan duration)
    {
        for (var attempt = 0; attempt < MaxSnapshotAttempts; attempt++)
        {
            if (TrySnapshot(duration) is { } snapshot)
                return snapshot;
        }

        return ReplaySnapshot.Empty; // the format kept changing underneath us
    }

    // Copies outside the lock; frames the writer overwrote meanwhile (only ever the oldest) are trimmed afterwards.
    private ReplaySnapshot? TrySnapshot(TimeSpan duration)
    {
        float[] ring;
        int sampleRate, channels, capacityFrames, validFrames, generation;
        long totalFrames, endTicks;
        lock (_lock)
        {
            if (_channels == 0 || duration <= TimeSpan.Zero)
                return ReplaySnapshot.Empty;

            (ring, sampleRate, channels, capacityFrames) = (_ring, _sampleRate, _channels, _capacityFrames);
            (totalFrames, validFrames, endTicks, generation) = (_totalFrames, _validFrames, _endTicks, _generation);
        }

        var wanted = (int)Math.Min(capacityFrames, TicksToFrames(duration.Ticks, sampleRate));
        var idle = _clock() - endTicks;
        var trailing = idle > IdleThreshold.Ticks ? (int)Math.Min(wanted, TicksToFrames(idle, sampleRate)) : 0;
        var content = Math.Min(wanted - trailing, validFrames);
        var samples = new float[(content + trailing) * channels];
        CopyOut(ring, capacityFrames, channels, totalFrames - content, content, samples);

        long totalAfter;
        lock (_lock)
        {
            if (_generation != generation)
                return null;

            totalAfter = _totalFrames;
        }

        var overwritten = (totalAfter - capacityFrames) - (totalFrames - content);
        if (overwritten > 0)
            samples = samples[(int)(Math.Min(overwritten, content) * channels)..];

        return new ReplaySnapshot(samples, sampleRate, channels);
    }

    // Writes frames at the ring position matching the timeline; empty data writes silence.
    private void WriteFrames(ReadOnlySpan<float> data, long frames)
    {
        var skipped = Math.Max(0, frames - _capacityFrames);
        var count = (int)(frames - skipped);
        var start = (int)((_totalFrames + skipped) % _capacityFrames);
        var first = Math.Min(count, _capacityFrames - start);
        var source = data.IsEmpty ? data : data[(int)(skipped * _channels)..];

        Fill(_ring.AsSpan(start * _channels, first * _channels), source.IsEmpty ? source : source[..(first * _channels)]);
        Fill(_ring.AsSpan(0, (count - first) * _channels), source.IsEmpty ? source : source[(first * _channels)..]);
        _totalFrames += frames;
        _validFrames = (int)Math.Min(_validFrames + frames, _capacityFrames);
    }

    private static void Fill(Span<float> target, ReadOnlySpan<float> source)
    {
        if (source.IsEmpty)
            target.Clear();
        else
            source.CopyTo(target);
    }

    private static void CopyOut(float[] ring, int capacityFrames, int channels, long fromFrame, int frames, Span<float> target)
    {
        if (frames == 0)
            return;

        var start = (int)(fromFrame % capacityFrames);
        var first = Math.Min(frames, capacityFrames - start);
        ring.AsSpan(start * channels, first * channels).CopyTo(target);
        ring.AsSpan(0, (frames - first) * channels).CopyTo(target[(first * channels)..]);
    }

    // Allocates the ring for the current capacity, carrying over the most recent audio that fits.
    private void Reallocate()
    {
        var capacityFrames = (int)Math.Ceiling(_capacity.TotalSeconds * _sampleRate);
        var kept = Math.Min(_validFrames, capacityFrames);
        var recent = new float[kept * _channels];
        CopyOut(_ring, _capacityFrames, _channels, _totalFrames - kept, kept, recent);

        _ring = new float[(long)capacityFrames * _channels];
        _capacityFrames = capacityFrames;
        _validFrames = 0;
        if (kept > 0)
        {
            _totalFrames -= kept;
            WriteFrames(recent, kept);
        }

        _generation++;
    }

    private long FramesToTicks(long frames) => frames * TimeSpan.TicksPerSecond / _sampleRate;

    private long TicksToFrames(long ticks) => TicksToFrames(ticks, _sampleRate);

    private static long TicksToFrames(long ticks, int sampleRate) =>
        (long)Math.Round((double)ticks * sampleRate / TimeSpan.TicksPerSecond);

    private static TimeSpan ClampCapacity(TimeSpan capacity) =>
        capacity < MinCapacity ? MinCapacity : capacity > MaxCapacity ? MaxCapacity : capacity;
}
