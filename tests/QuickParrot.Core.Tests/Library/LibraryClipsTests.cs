using QuickParrot.Core.Library;
using QuickParrot.Core.Tests.Fakes;

namespace QuickParrot.Core.Tests.Library;

public class LibraryClipsTests
{
    [Fact]
    public void List_WalksEveryFolder()
    {
        var source = new FakeFolderSource();
        var memes = source.AddFolder("", "memes");
        source.AddFile(memes, "bruh.wav");
        source.AddFile(source.AddFolder(memes, "old"), "wow.mp3");
        source.AddFile("", "wall.wav");

        var clips = LibraryClips.List(source, 100);

        Assert.Equal(["memes/bruh.wav", "memes/old/wow.mp3", "wall.wav"], clips.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void List_StopsAtTheLimit()
    {
        var source = new FakeFolderSource();
        for (var i = 0; i < 5; i++)
            source.AddFile("", $"{i}.wav");

        Assert.Equal(3, LibraryClips.List(source, 3).Count);
    }

    [Fact]
    public void List_StopsDescendingPastTheMaximumDepth()
    {
        var source = new FakeFolderSource();
        var folder = "";
        for (var depth = 0; depth <= LibraryClips.MaxDepth + 1; depth++)
        {
            source.AddFile(folder, $"{depth}.wav");
            folder = source.AddFolder(folder, "deeper");
        }

        Assert.Equal(LibraryClips.MaxDepth + 1, LibraryClips.List(source, 100).Count);
    }

    [Fact]
    public void List_OfAnEmptyLibrary_IsEmpty() => Assert.Empty(LibraryClips.List(new FakeFolderSource(), 10));
}
