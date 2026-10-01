using Microsoft.Extensions.Time.Testing;
using QuickParrot.Core.Engine;
using QuickParrot.Core.Grabs;
using QuickParrot.Core.Navigation;
using QuickParrot.Core.Replay;
using QuickParrot.Core.Settings;
using QuickParrot.Core.Tests.Fakes;

namespace QuickParrot.Core.Tests;

public sealed class QuickParrotEngineGrabTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly List<string> _log = [];
    private readonly FakeFolderSource _library = new();
    private readonly FakeReplaySource _replay = new();
    private readonly FakePendingGrabStore _grabs = new();
    private readonly List<string> _errors = [];
    private readonly TaskCompletionSource<PendingGrab> _saved = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string> _failed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly QuickParrotEngine _engine;

    public QuickParrotEngineGrabTests()
    {
        _library.AddFile("", "wall.wav");
        _engine = CreateEngine(new AppSettings { LibraryRoot = "root", ReplayBufferSeconds = 20 }, _replay);
        _engine.Start();
    }

    [Fact]
    public async Task ChordEnter_SavesTheReplayBuffer()
    {
        _replay.Next = new ReplaySnapshot([0.5f, -0.5f], 1000, 1);

        _engine.Post(new ChordPressed());
        _engine.Post(new GrabPressed());

        var grab = await _saved.Task.WaitAsync(Timeout);
        Assert.Equal(TimeSpan.FromSeconds(20), Assert.Single(_replay.Requests));
        Assert.Equal([grab], _grabs.List());
        Assert.Empty(_errors);
    }

    [Fact]
    public async Task Grab_FromTheApp_SavesTheReplayBuffer()
    {
        _replay.Next = new ReplaySnapshot([0.5f, -0.5f], 1000, 1);

        _engine.Grab();

        await _saved.Task.WaitAsync(Timeout);
    }

    [Fact]
    public async Task ChordReleaseAfterGrab_DoesNotStopPlayback()
    {
        _engine.Post(new ChordPressed());
        _engine.Post(new GrabPressed());
        _engine.Post(new ChordReleased());
        await _failed.Task.WaitAsync(Timeout);
        await _engine.FlushAsync();

        Assert.DoesNotContain("stop", _log);
    }

    [Fact]
    public async Task EmptyBuffer_ReportsAFriendlyFailure()
    {
        _engine.Grab();

        Assert.Equal(ReplayGrabber.EmptyMessage, await _failed.Task.WaitAsync(Timeout));
        await _engine.FlushAsync();
        Assert.Equal([ReplayGrabber.EmptyMessage], _errors);
    }

    [Fact]
    public async Task DisabledBuffer_ReportsAFriendlyFailure_WithoutSnapshotting()
    {
        _engine.UpdateSettings(s => s with { ReplayBufferEnabled = false });

        _engine.Grab();

        Assert.Equal(ReplayGrabber.DisabledMessage, await _failed.Task.WaitAsync(Timeout));
        Assert.Empty(_replay.Requests);
    }

    [Fact]
    public async Task NoReplaySource_ReportsDisabled()
    {
        using var engine = CreateEngine(new AppSettings(), replay: null);
        var failed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.GrabFailed += m => failed.TrySetResult(m);
        engine.Start();

        engine.Grab();

        Assert.Equal(ReplayGrabber.DisabledMessage, await failed.Task.WaitAsync(Timeout));
    }

    [Fact]
    public async Task ChordEnter_WithoutALibrary_StillGrabs()
    {
        _replay.Next = new ReplaySnapshot([0.5f, -0.5f], 1000, 1);
        _engine.UpdateSettings(s => s with { LibraryRoot = null });

        _engine.Post(new ChordPressed());
        _engine.Post(new GrabPressed());

        await _saved.Task.WaitAsync(Timeout);
    }

    [Fact]
    public async Task Settings_SetTheReplayCapacity()
    {
        await _engine.FlushAsync();
        Assert.Equal(TimeSpan.FromSeconds(20), _replay.Capacity);

        _engine.UpdateSettings(s => s with { ReplayBufferSeconds = 90 });
        await _engine.FlushAsync();

        Assert.Equal(TimeSpan.FromSeconds(90), _replay.Capacity);
    }

    public void Dispose() => _engine.Dispose();

    private QuickParrotEngine CreateEngine(AppSettings settings, IReplaySource? replay)
    {
        var engine = new QuickParrotEngine(
            new FakeClipPlayer(_log), new FakePushToTalk(_log), new FakeMicMuter(_log), new FakeSettingsStore(), settings,
            new FakeTimeProvider(), _ => _library, replay, _grabs);
        engine.ErrorOccurred += e => { lock (_errors) _errors.Add(e); };
        engine.GrabSaved += g => _saved.TrySetResult(g);
        engine.GrabFailed += m => _failed.TrySetResult(m);
        return engine;
    }
}
