using QuickParrot.Core.Replay;

namespace QuickParrot.Core.Tests.Replay;

// At 1 kHz mono one frame is exactly 1 ms, so expected sample counts read as milliseconds.
public sealed class ReplayBufferTests
{
    private const int Rate = 1000;
    private const long Ms = TimeSpan.TicksPerMillisecond;
    private const long T0 = 1_000_000 * Ms;

    private long _now = T0;
    private readonly ReplayBuffer _buffer;

    public ReplayBufferTests()
    {
        _buffer = new ReplayBuffer(TimeSpan.FromSeconds(5), () => _now);
        _buffer.Begin(Rate, 1);
    }

    [Fact]
    public void ContiguousPackets_AreKeptInOrder()
    {
        Packet(0, 10, 1f);
        Packet(10, 10, 2f);
        Packet(20, 10, 3f);

        Assert.Equal([.. Fill(10, 1f), .. Fill(10, 2f), .. Fill(10, 3f)], Snap(5000).Samples.ToArray());
    }

    [Fact]
    public void GapBetweenPackets_BecomesSilence()
    {
        Packet(0, 10, 1f);
        Packet(110, 10, 2f);

        Assert.Equal([.. Fill(10, 1f), .. Fill(100, 0f), .. Fill(10, 2f)], Snap(5000).Samples.ToArray());
    }

    [Fact]
    public void GapSinceBegin_BecomesSilence()
    {
        Packet(500, 10, 1f);

        Assert.Equal([.. Fill(500, 0f), .. Fill(10, 1f)], Snap(5000).Samples.ToArray());
    }

    [Fact]
    public void TimestampJitter_BelowTolerance_IsNotFilled()
    {
        Packet(0, 10, 1f);
        Packet(10 + 20, 10, 2f);
        Packet(20 + 15, 10, 3f); // a little early relative to the re-anchored timeline

        Assert.Equal([.. Fill(10, 1f), .. Fill(10, 2f), .. Fill(10, 3f)], Snap(5000).Samples.ToArray());
    }

    [Fact]
    public void Discontinuity_FillsEvenSmallGaps()
    {
        Packet(0, 10, 1f);
        Packet(15, 10, 2f, ReplayPacketFlags.Discontinuity);

        Assert.Equal([.. Fill(10, 1f), .. Fill(5, 0f), .. Fill(10, 2f)], Snap(5000).Samples.ToArray());
    }

    [Fact]
    public void TimestampError_AppendsContiguously()
    {
        Packet(0, 10, 1f);
        Packet(400, 10, 2f, ReplayPacketFlags.TimestampError);
        Packet(20, 10, 3f);
        _now = T0 + 30 * Ms;

        Assert.Equal([.. Fill(10, 1f), .. Fill(10, 2f), .. Fill(10, 3f)], Snap(5000).Samples.ToArray());
    }

    [Fact]
    public void SilentPacket_IsWrittenAsZeros()
    {
        Packet(0, 10, 1f);
        Packet(10, 10, 0.5f, ReplayPacketFlags.Silent);

        Assert.Equal([.. Fill(10, 1f), .. Fill(10, 0f)], Snap(5000).Samples.ToArray());
    }

    [Fact]
    public void Snapshot_AfterPacketsStop_IncludesSilenceUpToNow()
    {
        Packet(0, 10, 1f);
        _now = T0 + 10 * Ms + 1000 * Ms;

        Assert.Equal([.. Fill(10, 1f), .. Fill(1000, 0f)], Snap(5000).Samples.ToArray());
    }

    [Fact]
    public void Snapshot_DuringNormalLatency_AddsNoTrailingSilence()
    {
        Packet(0, 10, 1f);
        _now = T0 + 10 * Ms + ReplayBuffer.IdleThreshold.Ticks - Ms;

        Assert.Equal(10, Snap(5000).Frames);
    }

    [Fact]
    public void TrailingSilence_DoesNotChangeTheTimeline()
    {
        Packet(0, 10, 1f);
        _now = T0 + 1000 * Ms;
        Snap(5000);
        Packet(2000, 10, 2f);

        Assert.Equal([.. Fill(10, 1f), .. Fill(1990, 0f), .. Fill(10, 2f)], Snap(5000).Samples.ToArray());
    }

    [Fact]
    public void Snapshot_ReturnsOnlyTheMostRecentDuration()
    {
        Packet(0, 10, 1f);
        Packet(10, 10, 2f);

        Assert.Equal([1f, 1f, .. Fill(10, 2f)], Snap(12).Samples.ToArray());
    }

    [Fact]
    public void Snapshot_IsCappedAtCapacity_AfterWrapping()
    {
        _buffer.Capacity = TimeSpan.FromSeconds(1);
        for (var i = 0; i < 15; i++)
            Ramp(i * 100, 100);

        var samples = Snap(10_000).Samples.ToArray();

        Assert.Equal(Enumerable.Range(500, 1000).Select(i => (float)i), samples);
    }

    [Fact]
    public void PacketLargerThanCapacity_KeepsItsNewestFrames()
    {
        _buffer.Capacity = TimeSpan.FromSeconds(1);
        Ramp(0, 2500);

        Assert.Equal(Enumerable.Range(1500, 1000).Select(i => (float)i), Snap(10_000).Samples.ToArray());
    }

    [Fact]
    public void GapLongerThanCapacity_LeavesOnlySilenceBeforeTheNewPacket()
    {
        Packet(0, 10, 1f);
        Packet(60_000, 10, 2f);

        Assert.Equal([.. Fill(4990, 0f), .. Fill(10, 2f)], Snap(5000).Samples.ToArray());
    }

    [Fact]
    public void CapacityChange_KeepsTheMostRecentAudio()
    {
        Ramp(0, 3000);

        _buffer.Capacity = TimeSpan.FromSeconds(1);
        Assert.Equal(Enumerable.Range(2000, 1000).Select(i => (float)i), Snap(10_000).Samples.ToArray());

        _buffer.Capacity = TimeSpan.FromSeconds(3);
        Ramp(3000, 500);
        Assert.Equal(Enumerable.Range(2000, 1500).Select(i => (float)i), Snap(10_000).Samples.ToArray());
    }

    [Fact]
    public void Capacity_IsClamped()
    {
        _buffer.Capacity = TimeSpan.Zero;
        Assert.Equal(ReplayBuffer.MinCapacity, _buffer.Capacity);

        _buffer.Capacity = TimeSpan.FromHours(1);
        Assert.Equal(ReplayBuffer.MaxCapacity, _buffer.Capacity);
    }

    [Fact]
    public void Begin_WithSameFormat_KeepsAudio_AndFillsTheDowntime()
    {
        Packet(0, 10, 1f);
        _now = T0 + 50 * Ms;
        _buffer.Begin(Rate, 1);
        Packet(100, 10, 2f);

        Assert.Equal([.. Fill(10, 1f), .. Fill(90, 0f), .. Fill(10, 2f)], Snap(5000).Samples.ToArray());
    }

    [Fact]
    public void Begin_WithNewFormat_DiscardsAudio()
    {
        Packet(0, 10, 1f);
        _now = T0 + 50 * Ms;
        _buffer.Begin(2 * Rate, 2);
        _buffer.Write([0.5f, -0.5f, 0.25f, -0.25f], _now);

        var snapshot = Snap(5000);

        Assert.Equal((2 * Rate, 2), (snapshot.SampleRate, snapshot.Channels));
        Assert.Equal([0.5f, -0.5f, 0.25f, -0.25f], snapshot.Samples.ToArray());
    }

    [Fact]
    public void PartialFrames_AreDropped()
    {
        _buffer.Begin(Rate, 2);
        _buffer.Write([1f, 2f, 3f], _now);

        Assert.Equal([1f, 2f], Snap(5000).Samples.ToArray());
    }

    [Fact]
    public void Clear_EmptiesAndIgnoresWritesUntilBegin()
    {
        Packet(0, 10, 1f);

        _buffer.Clear();
        Packet(10, 10, 2f);

        Assert.Same(ReplaySnapshot.Empty, _buffer.Snapshot(TimeSpan.FromSeconds(5)));
        Assert.Equal((0, 0), _buffer.Format);
    }

    [Fact]
    public void BeforeBegin_SnapshotIsEmpty()
    {
        var buffer = new ReplayBuffer(TimeSpan.FromSeconds(5), () => _now);
        buffer.Write([1f], _now);

        Assert.Equal(0, buffer.Snapshot(TimeSpan.FromSeconds(5)).Frames);
    }

    [Fact]
    public void NonPositiveDuration_IsEmpty()
    {
        Packet(0, 10, 1f);

        Assert.Equal(0, _buffer.Snapshot(TimeSpan.Zero).Frames);
    }

    // The writer only ever overwrites the oldest frames, so a torn copy would show up as a break in the ramp.
    [Fact]
    public async Task ConcurrentSnapshots_AreNeverTorn()
    {
        var buffer = new ReplayBuffer(TimeSpan.FromSeconds(1), () => T0);
        buffer.Begin(48_000, 1);
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var writer = Task.Run(() =>
        {
            var packet = new float[480];
            var next = 0;
            while (!stop.IsCancellationRequested)
            {
                for (var i = 0; i < packet.Length; i++)
                    packet[i] = next++ % 1_000_000;

                buffer.Write(packet, T0, ReplayPacketFlags.TimestampError);
            }
        });

        var checkedSnapshots = 0;
        while (!writer.IsCompleted)
        {
            var samples = buffer.Snapshot(TimeSpan.FromSeconds(1)).Samples.ToArray();
            for (var i = 1; i < samples.Length; i++)
                Assert.Equal((samples[i - 1] + 1) % 1_000_000, samples[i]);

            checkedSnapshots++;
        }

        await writer;
        Assert.True(checkedSnapshots > 0);
    }

    [Fact]
    public void QpcNow_Advances()
    {
        var first = ReplayBuffer.QpcNow();
        Thread.SpinWait(1000);

        Assert.True(ReplayBuffer.QpcNow() >= first);
        Assert.True(first > 0);
    }

    private void Packet(int atMs, int frames, float value, ReplayPacketFlags flags = ReplayPacketFlags.None)
    {
        _now = Math.Max(_now, T0 + (atMs + frames) * Ms);
        _buffer.Write(Fill(frames, value), T0 + atMs * Ms, flags);
    }

    private void Ramp(int startFrame, int frames)
    {
        _now = Math.Max(_now, T0 + (startFrame + frames) * Ms);
        var samples = Enumerable.Range(startFrame, frames).Select(i => (float)i).ToArray();
        _buffer.Write(samples, T0 + startFrame * Ms);
    }

    private ReplaySnapshot Snap(int ms) => _buffer.Snapshot(TimeSpan.FromMilliseconds(ms));

    private static float[] Fill(int count, float value) => Enumerable.Repeat(value, count).ToArray();
}
