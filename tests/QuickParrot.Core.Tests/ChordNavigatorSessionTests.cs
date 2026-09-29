using QuickParrot.Core.Navigation;
using QuickParrot.Core.Tests.Fakes;

namespace QuickParrot.Core.Tests;

public class ChordNavigatorSessionTests
{
    [Fact]
    public void BareTap_EmitsStopPlayback()
    {
        var source = new FakeFolderSource();
        var nav = new ChordNavigator(source);

        nav.Handle(new ChordPressed());
        var actions = nav.Handle(new ChordReleased());

        Assert.IsType<StopPlayback>(Assert.Single(actions));
    }

    [Fact]
    public void ReleaseAfterNavigatingFolders_DoesNotStopPlayback()
    {
        var source = new FakeFolderSource();
        source.AddFolder("", "Trump");
        var nav = new ChordNavigator(source);

        nav.Handle(new ChordPressed());
        nav.Handle(new DigitPressed(1, false));
        var actions = nav.Handle(new ChordReleased());

        Assert.Empty(actions);
    }

    [Fact]
    public void ReleaseAfterPlayingClip_DoesNotStopPlayback()
    {
        var source = new FakeFolderSource();
        source.AddFile("", "clip.wav");
        var nav = new ChordNavigator(source);

        nav.Handle(new ChordPressed());
        nav.Handle(new DigitPressed(1, false));
        var actions = nav.Handle(new ChordReleased());

        Assert.Empty(actions);
    }

    [Fact]
    public void SpentSession_IgnoresFurtherDigitsUntilRelease()
    {
        var source = new FakeFolderSource();
        source.AddFile("", "clip.wav");
        source.AddFile("", "clip2.wav");
        var nav = new ChordNavigator(source);

        nav.Handle(new ChordPressed());
        nav.Handle(new DigitPressed(1, false)); // plays a clip, session becomes spent
        var actions = nav.Handle(new DigitPressed(2, false));

        Assert.Empty(actions);
        Assert.Null(nav.ViewState);
    }

    [Fact]
    public void ChordPressed_WhileActive_IsIgnored()
    {
        var source = new FakeFolderSource();
        source.AddFolder("", "Trump");
        var nav = new ChordNavigator(source);

        nav.Handle(new ChordPressed());
        nav.Handle(new DigitPressed(1, false));
        nav.Handle(new ChordPressed()); // auto-repeat must not reset the session

        Assert.Equal("Trump", nav.ViewState!.FolderPath);
    }

    [Fact]
    public void DigitsWhileInactive_AreIgnored()
    {
        var source = new FakeFolderSource();
        source.AddFile("", "clip.wav");
        var nav = new ChordNavigator(source);

        var actions = nav.Handle(new DigitPressed(1, false));

        Assert.Empty(actions);
        Assert.Null(nav.ViewState);
    }

    [Fact]
    public void ChordReleased_WhileInactive_EmitsNothing()
    {
        var source = new FakeFolderSource();
        var nav = new ChordNavigator(source);

        var actions = nav.Handle(new ChordReleased());

        Assert.Empty(actions);
    }

    [Fact]
    public void EntriesAreReReadFromSource_AfterEachDigit()
    {
        var source = new FakeFolderSource();
        var nav = new ChordNavigator(source);

        nav.Handle(new ChordPressed());
        Assert.Empty(nav.ViewState!.WheelEntries);

        source.AddFile("", "clip.wav"); // library changes on disk mid-session
        nav.Handle(new DigitPressed(5, false));

        Assert.Single(nav.ViewState!.WheelEntries);
    }

    [Fact]
    public void Digit_SelectsWhatWasDisplayed_EvenIfDiskChangedSince()
    {
        var source = new FakeFolderSource();
        source.AddFile("", "b.wav");
        var nav = new ChordNavigator(source);

        nav.Handle(new ChordPressed());
        source.AddFile("", "a.wav"); // would sort into slot 1 on a fresh read
        var actions = nav.Handle(new DigitPressed(1, false));

        Assert.Equal("b.wav", Assert.IsType<PlayClip>(Assert.Single(actions)).RelativePath);
    }

    [Fact]
    public void NewSessionAfterSpentSession_StartsFresh()
    {
        var source = new FakeFolderSource();
        var trump = source.AddFolder("", "Trump");
        source.AddFile(trump, "wall.wav");
        var nav = new ChordNavigator(source);

        nav.Handle(new ChordPressed());
        nav.Handle(new DigitPressed(1, false));
        nav.Handle(new DigitPressed(1, false));
        nav.Handle(new ChordReleased());
        nav.Handle(new ChordPressed());

        Assert.Equal("", nav.ViewState!.FolderPath);
        Assert.IsType<StopPlayback>(Assert.Single(nav.Handle(new ChordReleased())));
    }
}
