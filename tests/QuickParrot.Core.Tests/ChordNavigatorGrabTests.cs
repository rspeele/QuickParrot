using QuickParrot.Core.Navigation;
using QuickParrot.Core.Tests.Fakes;

namespace QuickParrot.Core.Tests;

public class ChordNavigatorGrabTests
{
    private readonly FakeFolderSource _source = new();
    private readonly ChordNavigator _nav;

    public ChordNavigatorGrabTests()
    {
        _source.AddFile("", "clip.wav");
        _nav = new ChordNavigator(_source);
    }

    [Fact]
    public void Grab_EmitsGrabReplay_AndHidesTheOverlay()
    {
        _nav.Handle(new ChordPressed());

        var actions = _nav.Handle(new GrabPressed());

        Assert.IsType<GrabReplay>(Assert.Single(actions));
        Assert.Null(_nav.ViewState);
    }

    [Fact]
    public void ReleaseAfterGrab_DoesNotStopPlayback()
    {
        _nav.Handle(new ChordPressed());
        _nav.Handle(new GrabPressed());

        Assert.Empty(_nav.Handle(new ChordReleased()));
    }

    [Fact]
    public void AfterGrab_SessionIsSpent()
    {
        _nav.Handle(new ChordPressed());
        _nav.Handle(new GrabPressed());

        Assert.Empty(_nav.Handle(new DigitPressed(1, false)));
        Assert.Empty(_nav.Handle(new GrabPressed()));
    }

    [Fact]
    public void Grab_AfterPlayingClip_IsIgnored()
    {
        _nav.Handle(new ChordPressed());
        _nav.Handle(new DigitPressed(1, false));

        Assert.Empty(_nav.Handle(new GrabPressed()));
    }

    [Fact]
    public void Grab_WithoutSession_IsIgnored() => Assert.Empty(_nav.Handle(new GrabPressed()));

    [Fact]
    public void Grab_AfterNavigating_StillGrabs()
    {
        _source.AddFolder("", "Folder");
        _nav.Handle(new ChordPressed());
        _nav.Handle(new DigitPressed(1, false));

        Assert.IsType<GrabReplay>(Assert.Single(_nav.Handle(new GrabPressed())));
    }

    [Fact]
    public void NextChordPress_AfterGrab_StartsFresh()
    {
        _nav.Handle(new ChordPressed());
        _nav.Handle(new GrabPressed());
        _nav.Handle(new ChordReleased());

        _nav.Handle(new ChordPressed());

        Assert.NotNull(_nav.ViewState);
    }
}
