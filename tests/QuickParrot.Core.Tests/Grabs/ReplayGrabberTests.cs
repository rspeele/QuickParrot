using QuickParrot.Core.Grabs;
using QuickParrot.Core.Replay;
using QuickParrot.Core.Tests.Fakes;

namespace QuickParrot.Core.Tests.Grabs;

public sealed class ReplayGrabberTests
{
    private static readonly DateTimeOffset At = new DateTime(2026, 9, 30, 14, 25, 1, DateTimeKind.Local);

    private readonly FakeReplaySource _replay = new();
    private readonly FakePendingGrabStore _store = new();
    private readonly ReplayGrabber _grabber;

    public ReplayGrabberTests()
    {
        _grabber = new ReplayGrabber(_replay, _store);
    }

    [Fact]
    public void Grab_SavesTheRequestedLength()
    {
        _replay.Next = new ReplaySnapshot(Tone(1000 * 30), 1000, 1);

        var result = _grabber.Grab(TimeSpan.FromSeconds(30), At);

        Assert.Null(result.Error);
        Assert.Equal(TimeSpan.FromSeconds(30), Assert.Single(_replay.Requests));
        Assert.Equal((30_000, 1000, 1), Assert.Single(_store.Saves));
        Assert.Equal("Grabbed last 30 s", ReplayGrabber.SavedMessage(result.Grab!));
    }

    [Fact]
    public void Grab_FoldsSurroundToStereo()
    {
        _replay.Next = new ReplaySnapshot(Tone(6 * 100), 1000, 6);

        _grabber.Grab(TimeSpan.FromSeconds(30), At);

        Assert.Equal((200, 1000, 2), Assert.Single(_store.Saves));
    }

    [Fact]
    public void EmptyBuffer_IsReported()
    {
        var result = _grabber.Grab(TimeSpan.FromSeconds(30), At);

        Assert.Equal(ReplayGrabber.EmptyMessage, result.Error);
        Assert.Empty(_store.Saves);
    }

    [Fact]
    public void SilentBuffer_IsReported()
    {
        _replay.Next = new ReplaySnapshot(new float[1000 * 30], 1000, 1);

        var result = _grabber.Grab(TimeSpan.FromSeconds(30), At);

        Assert.Equal("Nothing was playing in the last 30 s.", result.Error);
        Assert.Empty(_store.Saves);
    }

    [Fact]
    public void SaveFailure_IsReported()
    {
        _replay.Next = new ReplaySnapshot(Tone(100), 1000, 1);
        _store.ThrowOnSave = new IOException("Disk full.");

        var result = _grabber.Grab(TimeSpan.FromSeconds(30), At);

        Assert.Equal("Couldn't save the grab: Disk full.", result.Error);
        Assert.Null(result.Grab);
    }

    [Fact]
    public void SavedMessage_RoundsToWholeSeconds_AtLeastOne()
    {
        Assert.Equal("Grabbed last 1 s", ReplayGrabber.SavedMessage(Grab(TimeSpan.FromMilliseconds(200))));
        Assert.Equal("Grabbed last 13 s", ReplayGrabber.SavedMessage(Grab(TimeSpan.FromSeconds(12.6))));
    }

    private static PendingGrab Grab(TimeSpan duration) => new("a", "a.wav", At, duration);

    private static float[] Tone(int count) => Enumerable.Range(0, count).Select(i => i % 2 == 0 ? 0.5f : -0.5f).ToArray();
}
