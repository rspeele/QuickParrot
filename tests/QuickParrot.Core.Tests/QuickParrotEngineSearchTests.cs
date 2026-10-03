using Microsoft.Extensions.Time.Testing;
using QuickParrot.Core.Engine;
using QuickParrot.Core.Keyboard;
using QuickParrot.Core.Navigation;
using QuickParrot.Core.Settings;
using QuickParrot.Core.Tests.Fakes;

namespace QuickParrot.Core.Tests;

public sealed class QuickParrotEngineSearchTests
{
    [Fact]
    public async Task SearchPublishesAfterReleaseAndSelectionPlaysThroughNormalController()
    {
        var log = new List<string>();
        var source = new FakeFolderSource();
        var folder = source.AddFolder("", "Elsewhere");
        source.AddFile(folder, "Number One.wav");
        var player = new FakeClipPlayer(log);
        var time = new FakeTimeProvider();
        var settings = new AppSettings
        {
            LibraryRoot = "root", SmallFolderLayout = SmallFolderLayout.Ring, PushToTalkEnabled = true,
        };
        using var engine = new QuickParrotEngine(player, new FakePushToTalk(log), new FakeMicMuter(log),
            new FakeSettingsStore(), settings, time, _ => source);
        var published = new List<OverlayViewState?>();
        engine.ViewStateChanged += published.Add;
        engine.Start();
        engine.Post(new ChordPressed());
        engine.Post(new SearchPressed());
        engine.Post(new ChordReleased());
        engine.Post(new SearchTextEntered("ONE"));
        await engine.FlushAsync();
        Assert.Equal("ONE", published[^1]!.SearchQuery);
        Assert.Single(published[^1]!.WheelEntries);
        engine.Post(new SearchSelectionPressed(1));
        await engine.FlushAsync();
        await engine.FlushAsync();
        time.Advance(TimeSpan.FromMilliseconds(500));
        await engine.FlushAsync();
        Assert.Null(published[^1]);
        Assert.Contains("play:fake:/Elsewhere/Number One.wav", log);
        Assert.DoesNotContain("stop", log);
    }
}
