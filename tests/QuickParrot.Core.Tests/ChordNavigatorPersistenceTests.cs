using QuickParrot.Core.Keyboard;
using QuickParrot.Core.Navigation;
using QuickParrot.Core.Tests.Fakes;

namespace QuickParrot.Core.Tests;

public class ChordNavigatorPersistenceTests
{
    [Fact]
    public void SaveNavigation_PersistsThatFolder()
    {
        var source = new FakeFolderSource();
        var trump = source.AddFolder("", "Trump");
        var nav = new ChordNavigator(source);

        nav.Handle(new ChordPressed());
        nav.Handle(new DigitPressed(1, true));
        nav.Handle(new SaveNavigationPressed());
        nav.Handle(new ChordReleased());

        Assert.Equal(trump, nav.PersistentPath);
    }

    [Fact]
    public void NonShiftSelectFolder_DoesNotPersist()
    {
        var source = new FakeFolderSource();
        source.AddFolder("", "Trump");
        var nav = new ChordNavigator(source);

        nav.Handle(new ChordPressed());
        nav.Handle(new DigitPressed(1, false));
        nav.Handle(new ChordReleased());

        Assert.Equal("", nav.PersistentPath);
    }

    [Fact]
    public void PickingAFile_KeepsSavedFolder()
    {
        var source = new FakeFolderSource();
        var trump = source.AddFolder("", "Trump");
        source.AddFile(trump, "wall.wav");
        var nav = new ChordNavigator(source, persistentPath: trump);

        nav.Handle(new ChordPressed());
        nav.Handle(new DigitPressed(1, true));

        Assert.Equal(trump, nav.PersistentPath);
    }

    [Fact]
    public void NewSession_StartsAtPersistentPath()
    {
        var source = new FakeFolderSource();
        var trump = source.AddFolder("", "Trump");
        source.AddFile(trump, "wall.wav");
        var nav = new ChordNavigator(source, trump);

        nav.Handle(new ChordPressed());

        Assert.Equal(trump, nav.ViewState!.FolderPath);
    }

    [Fact]
    public void SaveNavigationAfterGoingUp_PersistsNewFolder()
    {
        var source = new FakeFolderSource();
        var trump = source.AddFolder("", "Trump");
        var nav = new ChordNavigator(source, trump);

        nav.Handle(new ChordPressed());
        nav.Handle(new DigitPressed(0, true));
        nav.Handle(new SaveNavigationPressed());

        Assert.Equal("", nav.PersistentPath);
    }

    [Fact]
    public void NonShiftUp_DoesNotPersist()
    {
        var source = new FakeFolderSource();
        var trump = source.AddFolder("", "Trump");
        var nav = new ChordNavigator(source, trump);

        nav.Handle(new ChordPressed());
        nav.Handle(new DigitPressed(0, false));

        Assert.Equal(trump, nav.PersistentPath);
    }

    [Fact]
    public void ZeroAtRoot_IsNoOp()
    {
        var source = new FakeFolderSource();
        var nav = new ChordNavigator(source);

        nav.Handle(new ChordPressed());
        nav.Handle(new DigitPressed(0, true));

        Assert.Equal("", nav.ViewState!.FolderPath);
        Assert.Equal("", nav.PersistentPath);
    }

    [Fact]
    public void ShiftZeroWhileZoomed_OnlyUnzooms_DoesNotChangePersistence()
    {
        var source = new FakeFolderSource();
        for (var i = 0; i < 15; i++)
            source.AddFile("", $"clip{i:D3}.wav");
        var nav = new ChordNavigator(source);

        nav.Handle(new ChordPressed());
        nav.Handle(new DigitPressed(1, false)); // zoom column 1
        nav.Handle(new DigitPressed(0, true)); // shift+0 while zoomed

        Assert.Null(nav.ViewState!.ZoomedColumn);
        Assert.Equal("", nav.ViewState!.FolderPath);
        Assert.Equal("", nav.PersistentPath);
    }

    [Fact]
    public void MissingPersistentPath_FallsBackToNearestExistingAncestor()
    {
        var source = new FakeFolderSource();
        var trump = source.AddFolder("", "Trump");
        var quotes = source.AddFolder(trump, "Quotes");
        var nav = new ChordNavigator(source, quotes);

        source.RemoveFolder(trump, "Quotes");

        nav.Handle(new ChordPressed());

        Assert.Equal(trump, nav.ViewState!.FolderPath);
        Assert.Equal(trump, nav.PersistentPath);
    }

    [Fact]
    public void MissingPersistentPath_FallsBackAllTheWayToRoot()
    {
        var source = new FakeFolderSource();
        var trump = source.AddFolder("", "Trump");
        var nav = new ChordNavigator(source, trump);

        source.RemoveFolder("", "Trump");

        nav.Handle(new ChordPressed());

        Assert.Equal("", nav.ViewState!.FolderPath);
        Assert.Equal("", nav.PersistentPath);
    }

    [Theory]
    [InlineData("Trump/", "Trump")]
    [InlineData("/Movies//Arnold/", "Movies/Arnold")]
    [InlineData(@"Movies\Arnold", "Movies/Arnold")]
    public void PersistentPath_IsNormalized(string input, string expected)
    {
        var nav = new ChordNavigator(new FakeFolderSource(), input);

        Assert.Equal(expected, nav.PersistentPath);
    }
}
