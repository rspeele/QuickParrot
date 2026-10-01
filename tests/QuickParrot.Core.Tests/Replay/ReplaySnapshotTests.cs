using System.Buffers.Binary;
using QuickParrot.Core.Replay;

namespace QuickParrot.Core.Tests.Replay;

public sealed class ReplaySnapshotTests
{
    [Fact]
    public void Duration_CountsFrames()
    {
        var snapshot = new ReplaySnapshot(new float[48_000 * 2 * 3], 48_000, 2);

        Assert.Equal(48_000 * 3, snapshot.Frames);
        Assert.Equal(TimeSpan.FromSeconds(3), snapshot.Duration);
        Assert.Equal(TimeSpan.Zero, ReplaySnapshot.Empty.Duration);
    }

    [Fact]
    public void IsSilent_IgnoresNoiseFloor()
    {
        Assert.True(new ReplaySnapshot(new float[] { 0f, 1e-6f, -1e-6f }, 1000, 1).IsSilent);
        Assert.False(new ReplaySnapshot(new float[] { 0f, 0.01f, 0f }, 1000, 1).IsSilent);
    }

    [Fact]
    public void DownmixedToStereo_LeavesStereoAlone()
    {
        var stereo = new ReplaySnapshot(new float[] { 0.1f, 0.2f }, 1000, 2);

        Assert.Same(stereo, stereo.DownmixedToStereo());
    }

    [Fact]
    public void DownmixedToStereo_FoldsSurroundIntoFrontPair()
    {
        // 5.1 in WAVE order: FL, FR, FC, LFE, BL, BR.
        var surround = new ReplaySnapshot(new float[] { 0.5f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0.25f }, 1000, 6);

        var stereo = surround.DownmixedToStereo();

        Assert.Equal((2, 2, 1000), (stereo.Channels, stereo.Frames, stereo.SampleRate));
        Assert.Equal(0.5f, stereo.Samples.Span[0], 3);
        Assert.Equal(0f, stereo.Samples.Span[1], 3);
        Assert.Equal(0f, stereo.Samples.Span[2], 3);
        Assert.True(stereo.Samples.Span[3] > 0.1f);
    }

    [Fact]
    public void SampleConverter_HandlesEachFormat()
    {
        var target = new float[2];

        var pcm16 = new byte[4];
        BinaryPrimitives.WriteInt16LittleEndian(pcm16, short.MinValue);
        BinaryPrimitives.WriteInt16LittleEndian(pcm16.AsSpan(2), 16384);
        Assert.Equal(2, SampleConverter.ToFloat(pcm16, SampleFormat.Pcm16, target));
        Assert.Equal([-1f, 0.5f], target);

        byte[] pcm24 = [0x00, 0x00, 0x40, 0x00, 0x00, 0xC0];
        SampleConverter.ToFloat(pcm24, SampleFormat.Pcm24, target);
        Assert.Equal([0.5f, -0.5f], target);

        var float32 = new byte[8];
        BinaryPrimitives.WriteSingleLittleEndian(float32, 0.25f);
        BinaryPrimitives.WriteSingleLittleEndian(float32.AsSpan(4), -0.75f);
        SampleConverter.ToFloat(float32, SampleFormat.Float32, target);
        Assert.Equal([0.25f, -0.75f], target);
    }

    [Fact]
    public void SampleConverter_StopsAtTheShorterSide()
    {
        Assert.Equal(1, SampleConverter.ToFloat(new byte[5], SampleFormat.Pcm32, new float[4]));
        Assert.Equal(1, SampleConverter.ToFloat(new byte[8], SampleFormat.Pcm32, new float[1]));
    }
}
