using Microsoft.Extensions.Time.Testing;
using QuickParrot.Core.Engine;
using QuickParrot.Core.Keyboard;
using QuickParrot.Core.Navigation;
using QuickParrot.Core.Playback;
using QuickParrot.Core.Settings;
using QuickParrot.Core.Tests.Fakes;

namespace QuickParrot.Core.Tests;

public sealed class QuickParrotEngineTests : IDisposable
{
    private readonly List<string> _log = [];
    private readonly FakeTimeProvider _time = new();
    private readonly FakeFolderSource _library = new();
    private readonly FakeSettingsStore _store = new();
    private readonly List<string> _errors = [];
    private readonly FakeClipPlayer _player;
    private readonly QuickParrotEngine _engine;

    public QuickParrotEngineTests()
    {
        _player = new FakeClipPlayer(_log);
        var settings = new AppSettings { LibraryRoot = "root", PushToTalkEnabled = true, CableVolume = 0.5f };
        _engine = new QuickParrotEngine(
            _player, new FakePushToTalk(_log), new FakeMicMuter(_log), _store, settings, _time, _ => _library);
        _engine.ErrorOccurred += e => { lock (_errors) _errors.Add(e); };
        _engine.Start();
    }

    [Fact]
    public async Task ChordSelection_PlaysClipAfterPreRoll()
    {
        _library.AddFile("", "wall.wav");

        _engine.Post(new ChordPressed());
        _engine.Post(new DigitPressed(1, false));
        await _engine.FlushAsync();
        _time.Advance(PlaybackOptions.DefaultMargin);
        await _engine.FlushAsync();

        Assert.Equal(["ptt:press", "prepare:fake:/wall.wav", "play:fake:/wall.wav"], _log);
    }

    [Fact]
    public async Task BareChordTap_StopsPlayback()
    {
        _library.AddFile("", "wall.wav");
        _engine.Play("wall.wav");
        await SettleAsync();
        _time.Advance(PlaybackOptions.DefaultMargin);
        await _engine.FlushAsync();

        _engine.Post(new ChordPressed());
        _engine.Post(new ChordReleased());
        await _engine.FlushAsync();

        Assert.Equal(["stop", "ptt:release"], _log[^2..]);
    }

    [Fact]
    public async Task RetryMicRestore_ReachesTheMicMuter()
    {
        _engine.RetryMicRestore();
        await _engine.FlushAsync();

        Assert.Equal(["mic:retry"], _log);
    }

    [Fact]
    public async Task PlayingPathOutsideLibrary_ReportsError()
    {
        _engine.Play("../secret.wav");
        await _engine.FlushAsync();

        Assert.Single(_errors);
        Assert.Empty(_log);
    }

    [Fact]
    public async Task PlaybackFailure_IsReportedWithFileName()
    {
        _player.UnloadablePaths.Add("fake:/Trump/wall.wav");

        _engine.Play("Trump/wall.wav");
        await SettleAsync();

        Assert.Equal("Couldn't play wall.wav: The file no longer exists.", Assert.Single(_errors));
    }

    [Fact]
    public async Task Start_ConfiguresPlayerFromSettings()
    {
        await _engine.FlushAsync();

        Assert.Equal(0.5f, _player.Settings!.CableVolume);
    }

    [Fact]
    public async Task PersistentPathChange_IsSavedAfterDebounce()
    {
        var trump = _library.AddFolder("", "Trump");

        _engine.Post(new ChordPressed());
        _engine.Post(new DigitPressed(1, true));
        await _engine.FlushAsync();
        Assert.Equal(trump, _engine.Settings.NavigatorPersistentPath);
        Assert.Empty(_store.Saved);

        _time.Advance(QuickParrotEngine.SaveDelay);
        await _engine.FlushAsync();

        Assert.Equal(trump, Assert.Single(_store.Saved).NavigatorPersistentPath);
    }

    [Fact]
    public async Task RapidSettingsChanges_AreCoalescedIntoOneSave()
    {
        for (var i = 1; i <= 5; i++)
        {
            var volume = i / 10f;
            _engine.UpdateSettings(s => s with { MonitorVolume = volume });
        }

        await _engine.FlushAsync();
        _time.Advance(QuickParrotEngine.SaveDelay);
        await _engine.FlushAsync();

        Assert.Equal(0.5f, Assert.Single(_store.Saved).MonitorVolume);
        Assert.Equal(0.5f, _player.Settings!.MonitorVolume);
    }

    [Fact]
    public async Task ChangingLibraryRoot_ResetsPersistentPath()
    {
        _library.AddFolder("", "Trump");
        _engine.Post(new ChordPressed());
        _engine.Post(new DigitPressed(1, true));

        _engine.UpdateSettings(s => s with { LibraryRoot = "other" });
        await _engine.FlushAsync();

        Assert.Equal("", _engine.Settings.NavigatorPersistentPath);
    }

    [Fact]
    public async Task ChordSession_RaisesViewStateChangedOnWorker()
    {
        _library.AddFile("", "wall.wav");
        var states = new List<OverlayViewState?>();
        _engine.ViewStateChanged += s => { lock (states) states.Add(s); };

        _engine.Post(new ChordPressed());
        _engine.Post(new ChordReleased());
        await _engine.FlushAsync();

        Assert.Equal(2, states.Count);
        Assert.Equal("wall.wav", Assert.Single(states[0]!.WheelEntries).Name);
        Assert.Null(states[1]);
    }

    [Fact]
    public async Task EventsThatDontChangeViewState_DontRaiseViewStateChanged()
    {
        var raised = 0;
        _engine.ViewStateChanged += _ => Interlocked.Increment(ref raised);

        _engine.Post(new DigitPressed(1, false));
        _engine.Post(new ChordReleased());
        _engine.Stop();
        await _engine.FlushAsync();

        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task Dispose_ReleasesPushToTalkAndSavesPendingSettings()
    {
        _engine.Play("wall.wav");
        _engine.UpdateSettings(s => s with { CableVolume = 0.25f });
        await _engine.FlushAsync();

        _engine.Dispose();

        Assert.Equal("ptt:release", _log[^1]);
        Assert.Equal(0.25f, Assert.Single(_store.Saved).CableVolume);
    }

    [Fact]
    public async Task Dispose_SavesPendingSettings_EvenIfStoppingPlaybackThrows()
    {
        _engine.Play("wall.wav");
        await SettleAsync();
        _time.Advance(PlaybackOptions.DefaultMargin);
        _engine.UpdateSettings(s => s with { CableVolume = 0.25f });
        await _engine.FlushAsync();
        _player.ThrowOnStop = true;

        _engine.Dispose();

        Assert.Equal(0.25f, Assert.Single(_store.Saved).CableVolume);
        Assert.Contains(_errors, e => e.Contains("Stop failed."));
    }

    [Fact]
    public async Task SuppressPlayback_IgnoresDirectPlayRequests()
    {
        _library.AddFile("", "wall.wav");

        _engine.SuppressPlayback(true);
        _engine.Play("wall.wav");
        await SettleAsync();
        _time.Advance(PlaybackOptions.DefaultMargin);
        await _engine.FlushAsync();

        Assert.Empty(_log);
    }

    [Fact]
    public async Task SuppressPlayback_IgnoresChordSelectionButStillNavigates()
    {
        _library.AddFile("", "wall.wav");
        var states = new List<OverlayViewState?>();
        _engine.ViewStateChanged += s => { lock (states) states.Add(s); };

        _engine.SuppressPlayback(true);
        _engine.Post(new ChordPressed());
        _engine.Post(new DigitPressed(1, false));
        await SettleAsync();
        _time.Advance(PlaybackOptions.DefaultMargin);
        await _engine.FlushAsync();

        Assert.Empty(_log);
        Assert.NotEmpty(states); // chord navigation still updated the overlay
    }

    [Fact]
    public async Task SuppressPlayback_ClearedAfterwards_AllowsPlaybackAgain()
    {
        _library.AddFile("", "wall.wav");

        _engine.SuppressPlayback(true);
        _engine.SuppressPlayback(false);
        _engine.Play("wall.wav");
        await SettleAsync();
        _time.Advance(PlaybackOptions.DefaultMargin);
        await _engine.FlushAsync();

        Assert.Contains("play:fake:/wall.wav", _log);
    }

    public void Dispose() => _engine.Dispose();

    // A finished prepare is posted back to the worker by the item that started it, so needs a second pass.
    private async Task SettleAsync()
    {
        await _engine.FlushAsync();
        await _engine.FlushAsync();
    }
}
