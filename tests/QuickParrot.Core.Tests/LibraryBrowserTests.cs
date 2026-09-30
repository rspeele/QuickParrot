using QuickParrot.Core.Library;
using QuickParrot.Core.Tests.Fakes;

namespace QuickParrot.Core.Tests;

public class LibraryBrowserTests
{
    [Fact]
    public void OpeningFolder_NavigatesIntoIt()
    {
        var source = new FakeFolderSource();
        var trump = source.AddFolder("", "Trump");
        source.AddFile(trump, "wall.wav");
        var browser = new LibraryBrowser(source);

        var toPlay = browser.Open(browser.Entries[0]);

        Assert.Null(toPlay);
        Assert.Equal(trump, browser.CurrentPath);
        Assert.Equal("wall.wav", Assert.Single(browser.Entries).Name);
        Assert.True(browser.CanGoUp);
    }

    [Fact]
    public void OpeningFile_ReturnsItsPathToPlay()
    {
        var source = new FakeFolderSource();
        var clip = source.AddFile("", "wall.wav");
        var browser = new LibraryBrowser(source);

        Assert.Equal(clip, browser.Open(browser.Entries[0]));
        Assert.Equal("", browser.CurrentPath);
    }

    [Fact]
    public void GoUp_StopsAtRoot()
    {
        var source = new FakeFolderSource();
        var movies = source.AddFolder("", "Movies");
        source.AddFolder(movies, "Arnold");
        var browser = new LibraryBrowser(source);
        browser.Open(browser.Entries[0]);
        browser.Open(browser.Entries[0]);

        browser.GoUp();
        Assert.Equal(movies, browser.CurrentPath);
        browser.GoUp();
        browser.GoUp();

        Assert.Equal("", browser.CurrentPath);
        Assert.False(browser.CanGoUp);
    }

    [Fact]
    public void Refresh_FallsBackWhenCurrentFolderDisappears()
    {
        var source = new FakeFolderSource();
        var movies = source.AddFolder("", "Movies");
        source.AddFolder(movies, "Arnold");
        var browser = new LibraryBrowser(source);
        browser.Open(browser.Entries[0]);
        browser.Open(browser.Entries[0]);

        source.RemoveFolder(movies, "Arnold");
        browser.Refresh();

        Assert.Equal(movies, browser.CurrentPath);
        Assert.Empty(browser.Entries);
    }

    [Fact]
    public void OverlayTruncationWarning_SetWhenMoreThan81Entries()
    {
        var source = new FakeFolderSource();
        for (var i = 0; i < 85; i++)
            source.AddFile("", $"clip{i}.wav");
        var browser = new LibraryBrowser(source);

        Assert.Equal(
            "This folder has 85 entries; the overlay only shows the first 81. Consider subfolders.",
            browser.OverlayTruncationWarning);
    }

    [Fact]
    public void OverlayTruncationWarning_NullAt81OrFewerEntries()
    {
        var source = new FakeFolderSource();
        for (var i = 0; i < 81; i++)
            source.AddFile("", $"clip{i}.wav");
        var browser = new LibraryBrowser(source);

        Assert.Null(browser.OverlayTruncationWarning);
    }
}
