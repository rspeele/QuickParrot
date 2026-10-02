using Microsoft.Extensions.Time.Testing;
using QuickParrot.Core.Engine;
using QuickParrot.Core.Keyboard;
using QuickParrot.Core.Navigation;
using QuickParrot.Core.Settings;
using QuickParrot.Core.Tests.Fakes;

namespace QuickParrot.Core.Tests;

public sealed class QuickParrotEngineSaveNavigationTests
{
    [Fact]
    public async Task ConfirmationExpiresAfterOneSecond_WithoutMoreInput()
    {
        var time = new FakeTimeProvider();
        var library = new FakeFolderSource();
        var folder = library.AddFolder("", "Quotes");
        var states = new List<OverlayViewState?>();
        using var engine = CreateEngine(library, time, states);
        engine.Post(new ChordPressed());
        engine.Post(new DigitPressed(1, false));
        engine.Post(new SaveNavigationPressed());
        await engine.FlushAsync();
        Assert.Equal(folder, engine.Settings.NavigatorPersistentPath);
        Assert.True(states[^1]!.SaveNavigationConfirmed);
        time.Advance(TimeSpan.FromMilliseconds(999));
        await engine.FlushAsync();
        Assert.True(states[^1]!.SaveNavigationConfirmed);
        time.Advance(TimeSpan.FromMilliseconds(1));
        await engine.FlushAsync();
        Assert.False(states[^1]!.SaveNavigationConfirmed);
        Assert.False(states[^1]!.ShowSaveNavigationHint);
    }

    [Fact]
    public async Task RepeatedSaveRestartsTheFullConfirmationDuration()
    {
        var time = new FakeTimeProvider();
        var states = new List<OverlayViewState?>();
        using var engine = CreateEngine(new FakeFolderSource(), time, states);
        engine.Post(new ChordPressed());
        engine.Post(new SaveNavigationPressed());
        await engine.FlushAsync();
        time.Advance(TimeSpan.FromMilliseconds(700));
        engine.Post(new SaveNavigationPressed());
        await engine.FlushAsync();
        time.Advance(TimeSpan.FromMilliseconds(300));
        await engine.FlushAsync();
        Assert.True(states[^1]!.SaveNavigationConfirmed);
        time.Advance(TimeSpan.FromMilliseconds(700));
        await engine.FlushAsync();
        Assert.False(states[^1]!.ShowSaveNavigationHint);
    }

    [Fact]
    public async Task NavigationClearsConfirmation_AndReturningToSavedFolderKeepsHintHidden()
    {
        var time = new FakeTimeProvider();
        var library = new FakeFolderSource();
        library.AddFolder("", "Quotes");
        var states = new List<OverlayViewState?>();
        using var engine = CreateEngine(library, time, states);
        engine.Post(new ChordPressed());
        engine.Post(new SaveNavigationPressed());
        engine.Post(new DigitPressed(1, false));
        await engine.FlushAsync();
        Assert.True(states[^1]!.ShowSaveNavigationHint);
        Assert.False(states[^1]!.SaveNavigationConfirmed);
        var count = states.Count;
        time.Advance(QuickParrotEngine.SaveNavigationConfirmationDuration);
        await engine.FlushAsync();
        Assert.Equal(count, states.Count);
        engine.Post(new DigitPressed(0, false));
        await engine.FlushAsync();
        Assert.False(states[^1]!.ShowSaveNavigationHint);
    }

    [Fact]
    public async Task ChangingSaveBindingRepublishesTheConfiguredKey()
    {
        var time = new FakeTimeProvider();
        var states = new List<OverlayViewState?>();
        using var engine = CreateEngine(new FakeFolderSource(), time, states);
        engine.Post(new ChordPressed());
        await engine.FlushAsync();
        Assert.Equal(ScanKey.DefaultSaveNavigationKey, states[^1]!.SaveNavigationKey);
        var key = new ScanKey(0x4E, false);
        engine.UpdateSettings(s => s.WithSaveNavigationKey(key).Settings);
        await engine.FlushAsync();
        Assert.Equal(key, states[^1]!.SaveNavigationKey);
        Assert.Equal(2, states.Count);
    }

    [Theory]
    [InlineData("repeat")]
    [InlineData("session")]
    [InlineData("library")]
    public async Task AlreadyQueuedOldExpiryCannotHideANewerConfirmation(string change)
    {
        var time = new ManualTimeProvider();
        var states = new List<OverlayViewState?>();
        using var engine = CreateEngine(new FakeFolderSource(), time, states);
        engine.Post(new ChordPressed());
        engine.Post(new SaveNavigationPressed());
        await engine.FlushAsync();
        var oldTimer = time.Timers[^1];
        if (change == "session")
        {
            engine.Post(new ChordReleased());
            engine.Post(new ChordPressed());
        }
        else if (change == "library")
        {
            engine.UpdateSettings(s => s.WithLibraryRoot("other"));
            engine.Post(new ChordPressed());
        }
        engine.Post(new SaveNavigationPressed());
        await engine.FlushAsync();
        var freshTimer = time.Timers[^1];
        Assert.True(oldTimer.Disposed);
        oldTimer.FireEvenIfDisposed();
        await engine.FlushAsync();
        Assert.True(states[^1]!.SaveNavigationConfirmed);
        freshTimer.FireEvenIfDisposed();
        await engine.FlushAsync();
        Assert.False(states[^1]!.ShowSaveNavigationHint);
    }

    [Fact]
    public async Task DisposeCancelsTimer_AndLateCallbackDoesNotPublish()
    {
        var time = new ManualTimeProvider();
        var states = new List<OverlayViewState?>();
        using var engine = CreateEngine(new FakeFolderSource(), time, states);
        engine.Post(new ChordPressed());
        engine.Post(new SaveNavigationPressed());
        await engine.FlushAsync();
        var timer = time.Timers[^1];
        engine.Dispose();
        var count = states.Count;
        Assert.True(timer.Disposed);
        timer.FireEvenIfDisposed();
        await engine.FlushAsync();
        Assert.Equal(count, states.Count);
    }

    private static QuickParrotEngine CreateEngine(FakeFolderSource library, TimeProvider time, List<OverlayViewState?> states)
    {
        var log = new List<string>();
        var engine = new QuickParrotEngine(new FakeClipPlayer(log), new FakePushToTalk(log), new FakeMicMuter(log),
            new FakeSettingsStore(), new AppSettings { LibraryRoot = "root" }, time, _ => library);
        engine.ViewStateChanged += states.Add;
        engine.Start();
        return engine;
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        public List<ManualTimer> Timers { get; } = [];

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
