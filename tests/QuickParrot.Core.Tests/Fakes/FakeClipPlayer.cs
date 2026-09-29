using QuickParrot.Core.Playback;

namespace QuickParrot.Core.Tests.Fakes;

// Records calls into a shared log so tests can assert the ordering of player, PTT and mic calls.
public sealed class FakeClipPlayer(List<string> log) : IClipPlayer
{
    public event Action<ClipFinished>? Finished;

    public HashSet<string> UnloadablePaths { get; } = [];

    /// <summary>Clips disposed by their owner; the player itself owns (and never reports) clips it played.</summary>
    public List<string> DisposedClips { get; } = [];

    public HashSet<string> UnplayablePaths { get; } = [];

    /// <summary>Runs during Prepare, e.g. to advance fake time and simulate slow decoding.</summary>
    public Action? OnPrepare { get; set; }

    public long? CurrentPlayId { get; private set; }

    public OutputSettings? Settings { get; private set; }

    public IPreparedClip Prepare(string fullPath)
    {
        log.Add($"prepare:{fullPath}");
        OnPrepare?.Invoke();
        if (UnloadablePaths.Contains(fullPath))
            throw new FileNotFoundException("File not found.", fullPath);

        return new FakeClip(fullPath, this);
    }

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
    }

    public void Configure(OutputSettings settings) => Settings = settings;

    /// <summary>Simulates the current (or a given) clip ending, optionally with an output failure.</summary>
    public void RaiseFinished(long? playId = null, Exception? error = null) =>
        Finished?.Invoke(new ClipFinished(playId ?? CurrentPlayId!.Value, error));

    private sealed class FakeClip(string fullPath, FakeClipPlayer owner) : IPreparedClip
    {
        public string FullPath => fullPath;

        public bool Played { get; set; }

        public void Dispose()
        {
            Assert.False(Played, "The controller disposed a clip it had handed to the player.");
            owner.DisposedClips.Add(fullPath);
        }
    }
}

public sealed class FakePushToTalk(List<string> log) : IPushToTalk
{
    public void Press() => log.Add("ptt:press");

    public void Release() => log.Add("ptt:release");
}

public sealed class FakeMicMuter(List<string> log) : IMicMuter
{
    public void Mute() => log.Add("mic:mute");

    public void Unmute() => log.Add("mic:unmute");
}
