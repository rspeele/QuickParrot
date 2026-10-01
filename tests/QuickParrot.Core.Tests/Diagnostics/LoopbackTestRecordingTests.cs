using QuickParrot.Core.Diagnostics;
using QuickParrot.Core.Replay;

namespace QuickParrot.Core.Tests.Diagnostics;

public class LoopbackTestRecordingTests
{
    [Fact]
    public void StereoFloat_IsAveragedToMono()
    {
        var recording = new LoopbackTestRecording(48000, 2, SampleFormat.Float32, TimeSpan.FromSeconds(1));

        recording.Append(Bytes<float>([0.5f, 0.1f, -0.2f, -0.4f]), silent: false);

        Assert.Equal([0.3f, -0.3f], recording.ToArray(), (a, b) => Math.Abs(a - b) < 1e-6);
    }

    [Fact]
    public void Pcm16_IsScaledToUnitRange()
    {
        var recording = new LoopbackTestRecording(48000, 1, SampleFormat.Pcm16, TimeSpan.FromSeconds(1));

        recording.Append(Bytes<short>([16384, -32768]), silent: false);

        Assert.Equal([0.5f, -1f], recording.ToArray());
    }

    [Fact]
    public void Pcm24_KeepsTheSign()
    {
        var recording = new LoopbackTestRecording(48000, 1, SampleFormat.Pcm24, TimeSpan.FromSeconds(1));

        recording.Append([0x00, 0x00, 0x40, 0x00, 0x00, 0xC0], silent: false);

        Assert.Equal([0.5f, -0.5f], recording.ToArray());
    }

    [Fact]
    public void SilentPacket_AddsZeros()
    {
        var recording = new LoopbackTestRecording(48000, 2, SampleFormat.Float32, TimeSpan.FromSeconds(1));

        recording.Append(Bytes<float>([0.5f, 0.5f, 0.5f, 0.5f]), silent: true);

        Assert.Equal([0f, 0f], recording.ToArray());
    }

    [Fact]
    public void FramesPastTheMaximum_AreDropped()
    {
        var recording = new LoopbackTestRecording(4, 1, SampleFormat.Float32, TimeSpan.FromSeconds(1));

        recording.Append(Bytes<float>([1, 2, 3]), silent: false);
        recording.Append(Bytes<float>([4, 5, 6]), silent: false);

        Assert.Equal([1f, 2f, 3f, 4f], recording.ToArray());
        Assert.True(recording.IsFull);
    }

    private static byte[] Bytes<T>(T[] values) where T : unmanaged =>
        System.Runtime.InteropServices.MemoryMarshal.AsBytes(values.AsSpan()).ToArray();
}
