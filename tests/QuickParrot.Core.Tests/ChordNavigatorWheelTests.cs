using QuickParrot.Core.Keyboard;
using QuickParrot.Core.Navigation;
using QuickParrot.Core.Tests.Fakes;

namespace QuickParrot.Core.Tests;

public class ChordNavigatorWheelTests
{
    [Fact]
    public void ChordPressed_ShowsWheelForRootFolder_FoldersFirst()
    {
        var source = new FakeFolderSource();
        source.AddFolder("", "Trump");
        source.AddFile("", "clip.wav");
        var nav = new ChordNavigator(source);

        nav.Handle(new ChordPressed());
        var view = nav.ViewState;

        Assert.NotNull(view);
        Assert.Equal(OverlayLayoutKind.Wheel, view!.Layout);
        Assert.Equal(2, view.WheelEntries.Count);
        Assert.Equal("Trump", view.WheelEntries[0].Name);
        Assert.True(view.WheelEntries[0].IsFolder);
        Assert.Equal("clip.wav", view.WheelEntries[1].Name);
    }

    [Fact]
    public void DigitSelectsFolder_NavigatesIntoIt()
    {
        var source = new FakeFolderSource();
        var trump = source.AddFolder("", "Trump");
        source.AddFile(trump, "wall.wav");
        var nav = new ChordNavigator(source);

        nav.Handle(new ChordPressed());
        nav.Handle(new DigitPressed(1, false));

        Assert.Equal(trump, nav.ViewState!.FolderPath);
        Assert.Equal("wall.wav", nav.ViewState!.WheelEntries[0].Name);
    }

    [Fact]
    public void DigitSelectsFile_EmitsPlayClip_AndHidesOverlay()
    {
        var source = new FakeFolderSource();
        var clip = source.AddFile("", "clip.wav");
        var nav = new ChordNavigator(source);

        nav.Handle(new ChordPressed());
        var actions = nav.Handle(new DigitPressed(1, false));

        var playClip = Assert.IsType<PlayClip>(Assert.Single(actions));
        Assert.Equal(clip, playClip.RelativePath);
        Assert.Null(nav.ViewState);
    }

    [Fact]
    public void OutOfRangeDigit_DoesNothing_ButStillCountsAsPressed()
    {
        var source = new FakeFolderSource();
        source.AddFile("", "clip.wav");
        var nav = new ChordNavigator(source);

        nav.Handle(new ChordPressed());
        var digitActions = nav.Handle(new DigitPressed(5, false));
        Assert.Empty(digitActions);

        var releaseActions = nav.Handle(new ChordReleased());
        Assert.Empty(releaseActions);
    }
}
