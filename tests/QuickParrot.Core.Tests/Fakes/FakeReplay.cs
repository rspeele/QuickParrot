using QuickParrot.Core.Grabs;
using QuickParrot.Core.Replay;

namespace QuickParrot.Core.Tests.Fakes;

public sealed class FakeReplaySource : IReplaySource
{
    public TimeSpan Capacity { get; set; }

    public ReplaySnapshot Next { get; set; } = ReplaySnapshot.Empty;

    public List<TimeSpan> Requests { get; } = [];

    public ReplaySnapshot Snapshot(TimeSpan duration)
    {
        lock (Requests)
            Requests.Add(duration);

        return Next;
    }
}

public sealed class FakePendingGrabStore : IPendingGrabStore
{
    private readonly List<PendingGrab> _grabs = [];

    public event Action? Changed;

    public Exception? ThrowOnSave { get; set; }

    public List<(int Samples, int SampleRate, int Channels)> Saves { get; } = [];

    public IReadOnlyList<PendingGrab> List()
    {
        lock (_grabs)
            return _grabs.ToList();
    }

    public PendingGrab Save(ReadOnlySpan<float> samples, int sampleRate, int channels, DateTimeOffset grabbedAt)
    {
        if (ThrowOnSave is { } e)
            throw e;

        var grab = new PendingGrab(
            GrabFileName.Format(grabbedAt), "fake.wav", grabbedAt,
            TimeSpan.FromSeconds((double)samples.Length / channels / sampleRate));
        lock (_grabs)
        {
            Saves.Add((samples.Length, sampleRate, channels));
            _grabs.Add(grab);
        }

        Changed?.Invoke();
        return grab;
    }

    public void Delete(PendingGrab grab)
    {
        lock (_grabs)
            _grabs.Remove(grab);

        Changed?.Invoke();
    }
}
