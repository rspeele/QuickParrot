using Microsoft.Extensions.Time.Testing;
using QuickParrot.Core.Engine;
using QuickParrot.Core.Keyboard;
using QuickParrot.Core.Settings;
using QuickParrot.Core.Tests.Fakes;

namespace QuickParrot.Core.Tests;

public sealed class QuickParrotEngineFragmentsTests : IDisposable
{
    private readonly List<string> _log = [];
    private readonly List<string> _errors = [];
    private readonly List<long> _completedSessions = [];
    private readonly FakeTimeProvider _time = new();
    private readonly FakeFolderSource _library = new();
    private readonly FakeClipPlayer _player;
    private readonly QuickParrotEngine _engine;
    private readonly string _speaker;

    public QuickParrotEngineFragmentsTests()
    {
        var fragments = _library.AddFolder("", "Fragments");
        _speaker = _library.AddFolder(fragments, "Speaker");
        _library.AddFile(_speaker, "Hello.wav");
        _library.AddFile(_speaker, "world.wav");
        _library.AddFile("", "ordinary.wav");
        _player = new FakeClipPlayer(_log);
        _engine = new QuickParrotEngine(_player, new FakePushToTalk(_log), new FakeMicMuter(_log),
            new FakeSettingsStore(), new AppSettings
            {
                LibraryRoot = "root", PushToTalkEnabled = true, PreRollMilliseconds = 0,
            }, _time, _ => _library);
        _engine.ErrorOccurred += _errors.Add;
        _engine.FragmentSessionEnded += _completedSessions.Add;
        _engine.Start();
    }

    [Fact]
    public async Task EnterTapAppendsAndHoldPlaysQueuedPhraseWithoutAppendingCurrentResult()
    {
        await ComposeHello();
        Assert.Empty(_player.Phrases);
        _engine.Post(new SearchTextEntered("world"));
        _engine.Post(new FragmentEnterPressed());
        await _engine.FlushAsync();
        _time.Advance(TimeSpan.FromMilliseconds(450));
        await _engine.FlushAsync();
        Assert.Empty(_player.Phrases);
        Assert.Equal(0.9, _engine.ViewState!.FragmentHoldProgress);
        _time.Advance(TimeSpan.FromMilliseconds(50));
        await Settle();
        Assert.Equal(new[] { "fake:/Fragments/Speaker/Hello.wav" }, Assert.Single(_player.Phrases));
        Assert.Null(_engine.ViewState);
        Assert.Equal(42, Assert.Single(_completedSessions));
        _engine.Post(new FragmentEnterReleased());
        await _engine.FlushAsync();
        Assert.Single(_player.Phrases);
        Assert.Single(_log, line => line == "ptt:press");
    }

    [Fact]
    public async Task CancelledHoldCannotSubmitIntoNextSession()
    {
        await ComposeHello();
        _engine.Post(new FragmentEnterPressed());
        await _engine.FlushAsync();
        _time.Advance(TimeSpan.FromMilliseconds(300));
        _engine.Post(new ChordCancelled());
        _engine.Post(new ChordPressed());
        _engine.Post(new FragmentsPressed(43));
        await _engine.FlushAsync();
        _time.Advance(TimeSpan.FromSeconds(1));
        await _engine.FlushAsync();
        Assert.Empty(_player.Phrases);
        Assert.Empty(_engine.ViewState!.FragmentNames);
        Assert.Equal(0, _engine.ViewState.FragmentHoldProgress);
    }

    [Fact]
    public async Task DisposedHoldCallbackCannotSubmitNewPhraseAfterLibraryChange()
    {
        var time = new ManualTimeProvider();
        var player = new FakeClipPlayer([]);
        using var engine = new QuickParrotEngine(player, new FakePushToTalk([]), new FakeMicMuter([]),
            new FakeSettingsStore(), new AppSettings { LibraryRoot = "root" }, time, _ => _library);
        engine.Start();
        engine.Post(new ChordPressed());
        engine.Post(new FragmentsPressed());
        engine.Post(new FragmentSelectionPressed(1));
        engine.Post(new FragmentEnterPressed());
        await engine.FlushAsync();
        var stale = Assert.Single(time.Timers);
        engine.UpdateSettings(settings => settings.WithLibraryRoot("other"));
        engine.Post(new ChordPressed());
        engine.Post(new FragmentsPressed());
        engine.Post(new FragmentSelectionPressed(1));
        await engine.FlushAsync();
        Assert.True(stale.Disposed);
        time.Timestamp = time.TimestampFrequency;
        stale.FireEvenIfDisposed();
        await engine.FlushAsync();
        Assert.Empty(player.Phrases);
        Assert.Single(engine.ViewState!.FragmentNames);
        Assert.Equal(0, engine.ViewState.FragmentHoldProgress);
    }

    [Fact]
    public async Task EmptyPhraseHoldDoesNotAppendOnRelease()
    {
        _engine.Post(new ChordPressed());
        _engine.Post(new FragmentsPressed());
        _engine.Post(new FragmentEnterPressed());
        await _engine.FlushAsync();
        _time.Advance(TimeSpan.FromMilliseconds(500));
        await _engine.FlushAsync();
        _engine.Post(new FragmentEnterReleased());
        await _engine.FlushAsync();
        Assert.Empty(_engine.ViewState!.FragmentNames);
        Assert.Equal(0, _engine.ViewState.FragmentHoldProgress);
        Assert.Empty(_player.Phrases);
    }

    [Fact]
    public async Task MissingFragmentPreventsEntirePhrasePlayback()
    {
        await ComposeHello();
        _library.RemoveFile(_speaker, "Hello.wav");
        _engine.Post(new FragmentSubmitPressed());
        await Settle();
        Assert.Empty(_player.Phrases);
        Assert.Single(_errors);
        Assert.DoesNotContain("ptt:press", _log);
    }

    [Fact]
    public async Task SuppressedPhraseDoesNotPlay()
    {
        await ComposeHello();
        _engine.SuppressPlayback(true);
        _engine.Post(new FragmentSubmitPressed());
        await Settle();
        Assert.Empty(_player.Phrases);
        Assert.Null(_engine.ViewState);
    }

    [Fact]
    public async Task PhraseDoesNotReplaceLastIndividualClipForFavoriteAssignment()
    {
        _engine.Play("ordinary.wav");
        await Settle();
        await ComposeHello();
        _engine.Post(new FragmentSubmitPressed());
        await Settle();
        _engine.Post(new ChordPressed());
        _engine.Post(new FavoritePressed(1, true));
        _engine.Post(new FavoritePressed(1, false));
        await _engine.FlushAsync();
        Assert.Equal("ordinary.wav", _engine.Settings.Favorites[1]);
    }

    private async Task ComposeHello()
    {
        _engine.Post(new ChordPressed());
        _engine.Post(new FragmentsPressed(42));
        _engine.Post(new ChordReleased());
        _engine.Post(new SearchTextEntered("Hello"));
        _engine.Post(new FragmentEnterPressed());
        _engine.Post(new FragmentEnterReleased());
        await _engine.FlushAsync();
        Assert.Equal("Hello", Assert.Single(_engine.ViewState!.FragmentNames));
        Assert.Equal("", _engine.ViewState.SearchQuery);
    }

    private async Task Settle()
    {
        await _engine.FlushAsync();
        await _engine.FlushAsync();
    }

    public void Dispose() => _engine.Dispose();

    private sealed class ManualTimeProvider : TimeProvider
    {
        public long Timestamp { get; set; }
        public List<ManualTimer> Timers { get; } = [];
        public override long GetTimestamp() => Timestamp;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state);
            Timers.Add(timer);
            return timer;
        }
    }

    private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
    {
        public bool Disposed { get; private set; }
        public void FireEvenIfDisposed() => callback(state);
        public bool Change(TimeSpan dueTime, TimeSpan period) => !Disposed;
        public void Dispose() => Disposed = true;
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
