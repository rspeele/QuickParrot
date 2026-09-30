using QuickParrot.Core.Mic;
using QuickParrot.Core.Playback;

namespace QuickParrot.Core.Tests.Fakes;

// Records calls into a shared log so tests can assert the ordering of player, PTT and mic calls.
public sealed class FakeClipPlayer(List<string> log) : IClipPlayer
{
    private readonly List<PendingPrepare> _pending = [];

    public event Action<ClipFinished>? Finished;

    /// <summary>When set, prepares stay in flight until <see cref="CompletePrepare"/> or <see cref="FailPrepare"/>.</summary>
    public bool ManualPrepare { get; set; }

    public HashSet<string> UnloadablePaths { get; } = [];

    /// <summary>Clips disposed by their owner; the player itself owns (and never reports) clips it played.</summary>
    public List<string> DisposedClips { get; } = [];

    public HashSet<string> UnplayablePaths { get; } = [];

    public bool ThrowOnStop { get; set; }

    public Exception? ThrowFromPrepare { get; set; }

    /// <summary>The cancellation token of every prepare, in request order.</summary>
    public List<(string Path, CancellationToken Token)> Prepares { get; } = [];

    public long? CurrentPlayId { get; private set; }

    public OutputSettings? Settings { get; private set; }

    public Task<IPreparedClip> PrepareAsync(string fullPath, CancellationToken cancellationToken)
    {
        log.Add($"prepare:{fullPath}");
        if (ThrowFromPrepare is not null)
            throw ThrowFromPrepare;

        Prepares.Add((fullPath, cancellationToken));
        var pending = new PendingPrepare(fullPath);
        if (ManualPrepare)
            _pending.Add(pending);
        else
            Complete(pending);

        return pending.Source.Task;
    }

    /// <summary>Finishes the oldest in-flight prepare of <paramref name="path"/>, failing it if the path is unloadable.</summary>
    public void CompletePrepare(string path) => Complete(TakePending(path));

    public void FailPrepare(string path, Exception error) => TakePending(path).Source.SetException(error);

    public void CancelPrepare(string path) => TakePending(path).Source.SetCanceled();

    public bool WasCancelled(string path) => Prepares.Last(p => p.Path == path).Token.IsCancellationRequested;

    public void Play(IPreparedClip clip, long playId)
    {
        ((FakeClip)clip).Played = true;
        if (UnplayablePaths.Contains(clip.FullPath))
            throw new InvalidOperationException("Device unavailable.");

        log.Add($"play:{clip.FullPath}");
        CurrentPlayId = playId;
    }

    public void Stop()
    {
        log.Add("stop");
        CurrentPlayId = null;
        if (ThrowOnStop)
            throw new InvalidOperationException("Stop failed.");
    }

    public void Configure(OutputSettings settings) => Settings = settings;

    /// <summary>Simulates the current (or a given) clip ending, optionally with an output failure.</summary>
    public void RaiseFinished(long? playId = null, Exception? error = null) =>
        Finished?.Invoke(new ClipFinished(playId ?? CurrentPlayId!.Value, error));

    private void Complete(PendingPrepare pending)
    {
        if (UnloadablePaths.Contains(pending.Path))
            pending.Source.SetException(new FileNotFoundException("File not found.", pending.Path));
        else
            pending.Source.SetResult(new FakeClip(pending.Path, this));
    }

    private PendingPrepare TakePending(string path)
    {
        var pending = _pending.First(p => p.Path == path);
        _pending.Remove(pending);
        return pending;
    }

    // Continuations run synchronously on completion, keeping tests deterministic.
    private sealed record PendingPrepare(string Path)
    {
        public TaskCompletionSource<IPreparedClip> Source { get; } = new();
    }

    private sealed class FakeClip(string fullPath, FakeClipPlayer owner) : IPreparedClip
    {
        private bool _disposed;

        public string FullPath => fullPath;

        public bool Played { get; set; }

        public void Dispose()
        {
            Assert.False(Played, "The controller disposed a clip it had handed to the player.");
            Assert.False(_disposed, "The clip was disposed twice.");
            _disposed = true;
            lock (owner.DisposedClips)
                owner.DisposedClips.Add(fullPath);
        }
    }
}

public sealed class FakePushToTalk(List<string> log) : IPushToTalk
{
    public bool ThrowOnRelease { get; set; }

    public void Press() => log.Add("ptt:press");

    public void Release()
    {
        log.Add("ptt:release");
        if (ThrowOnRelease)
            throw new InvalidOperationException("Release failed.");
    }
}

public sealed class FakeMicMuter(List<string> log) : IMicMuter
{
    public MicDuckSettings? Settings { get; private set; }

    public void Configure(MicDuckSettings settings) => Settings = settings;

    public void Mute() => log.Add("mic:mute");

    public void Unmute() => log.Add("mic:unmute");
}
