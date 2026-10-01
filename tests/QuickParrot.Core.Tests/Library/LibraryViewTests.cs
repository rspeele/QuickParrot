using QuickParrot.Core.Library;
using QuickParrot.Core.Tests.Fakes;

namespace QuickParrot.Core.Tests.Library;

public class LibraryViewTests
{
    [Fact]
    public void OpeningFolder_ReturnsAViewOfIt()
    {
        var source = new FakeFolderSource();
        var trump = source.AddFolder("", "Trump");
        source.AddFile(trump, "wall.wav");
        var root = LibraryView.Root(source);

        var view = root.Open(source, root.Entries[0]);

        Assert.Equal(trump, view.Path);
        Assert.Equal("wall.wav", Assert.Single(view.Entries).Name);
        Assert.True(view.CanGoUp);
        Assert.Equal("", root.Path); // the original is untouched
    }

    [Fact]
    public void OpeningFile_LeavesTheViewAsItIs()
    {
        var source = new FakeFolderSource();
        source.AddFile("", "wall.wav");
        var root = LibraryView.Root(source);

        Assert.Same(root, root.Open(source, root.Entries[0]));
    }

    [Fact]
    public void GoUp_StopsAtRoot()
    {
        var source = new FakeFolderSource();
        var movies = source.AddFolder("", "Movies");
        var arnold = source.AddFolder(movies, "Arnold");
        var root = LibraryView.Root(source);
        var view = root.Open(source, root.Entries[0]).Open(source, new FolderEntry("Arnold", true, arnold));

        var up = view.GoUp(source);
        Assert.Equal(movies, up.Path);
        var top = up.GoUp(source).GoUp(source);

        Assert.Equal("", top.Path);
        Assert.False(top.CanGoUp);
    }

    [Fact]
    public void Refresh_FallsBackWhenCurrentFolderDisappears()
    {
        var source = new FakeFolderSource();
        var movies = source.AddFolder("", "Movies");
        var arnold = source.AddFolder(movies, "Arnold");
        var view = LibraryView.Root(source).Open(source, new FolderEntry("Arnold", true, arnold));

        source.RemoveFolder(movies, "Arnold");
        var refreshed = view.Refresh(source);

        Assert.Equal(movies, refreshed.Path);
        Assert.Empty(refreshed.Entries);
    }

    [Fact]
    public void Refresh_PicksUpNewEntries()
    {
        var source = new FakeFolderSource();
        var view = LibraryView.Root(source);

        source.AddFile("", "new.wav");

        Assert.Empty(view.Entries);
        Assert.Equal("new.wav", Assert.Single(view.Refresh(source).Entries).Name);
    }
}
