using QuickParrot.Core.Keyboard;
using QuickParrot.Core.Navigation;
using QuickParrot.Core.Tests.Fakes;

namespace QuickParrot.Core.Tests;

public class ChordNavigatorChangeSignalTests
{
    [Fact]
    public void SaveNavigation_EmitsPersistPath()
    {
        var source = new FakeFolderSource();
        var trump = source.AddFolder("", "Trump");
        var nav = new ChordNavigator(source);

        Assert.Empty(nav.Handle(new ChordPressed()));
        Assert.Empty(nav.Handle(new DigitPressed(1, true)));
        Assert.Equal([new PersistPath(trump)], nav.Handle(new SaveNavigationPressed()));
        Assert.Empty(nav.Handle(new DigitPressed(0, true)));
        Assert.Equal([new PersistPath("")], nav.Handle(new SaveNavigationPressed()));
    }

    [Fact]
    public void NonShiftNavigation_DoesNotEmit()
    {
        var source = new FakeFolderSource();
        source.AddFolder("", "Trump");
        var nav = new ChordNavigator(source);

        Assert.Empty(nav.Handle(new ChordPressed()));
        Assert.Empty(nav.Handle(new DigitPressed(1, false)));
        Assert.Empty(nav.Handle(new ChordReleased()));
    }

    [Fact]
    public void ShiftPickingAFile_DoesNotSaveItsFolder()
    {
        var source = new FakeFolderSource();
        var trump = source.AddFolder("", "Trump");
        var wall = source.AddFile(trump, "wall.wav");
        var nav = new ChordNavigator(source);

        nav.Handle(new ChordPressed());
        nav.Handle(new DigitPressed(1, false));

        Assert.Equal([new PlayClip(wall)], nav.Handle(new DigitPressed(1, true)));
        Assert.Equal("", nav.PersistentPath);
    }

    [Fact]
    public void PersistingTheSameFolder_DoesNotEmit()
    {
        var source = new FakeFolderSource();
        var trump = source.AddFolder("", "Trump");
        var wall = source.AddFile(trump, "wall.wav");
        var nav = new ChordNavigator(source, "Trump/");

        nav.Handle(new ChordPressed());

        Assert.Equal([new PlayClip(wall)], nav.Handle(new DigitPressed(1, true)));
    }

    [Fact]
    public void FallbackFromMissingFolder_Emits()
    {
        var source = new FakeFolderSource();
        var trump = source.AddFolder("", "Trump");
        var nav = new ChordNavigator(source, trump);
        source.RemoveFolder("", "Trump");

        Assert.Equal([new PersistPath("")], nav.Handle(new ChordPressed()));
    }
}
