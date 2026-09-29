using QuickParrot.Core.Library;

namespace QuickParrot.Core.Tests;

public class FileSystemFolderSourceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("quickparrot-tests-").FullName;

    [Fact]
    public void ReturnsFoldersAndAudioFiles_IgnoresNonAudioAndHidden()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Trump"));
        File.WriteAllText(Path.Combine(_root, "clip.wav"), "");
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "");
        var hiddenFile = Path.Combine(_root, "hidden.mp3");
        File.WriteAllText(hiddenFile, "");
        File.SetAttributes(hiddenFile, FileAttributes.Hidden);

        var source = new FileSystemFolderSource(_root);
        var entries = source.GetEntries("");

        Assert.NotNull(entries);
        Assert.Equal(2, entries!.Count);
        Assert.Contains(entries, e => e.Name == "Trump" && e.IsFolder);
        Assert.Contains(entries, e => e.Name == "clip.wav" && !e.IsFolder);
    }

    [Fact]
    public void MissingFolder_ReturnsNull()
    {
        var source = new FileSystemFolderSource(_root);

        Assert.Null(source.GetEntries("DoesNotExist"));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("Trump/../..")]
    [InlineData(@"..\sibling")]
    public void PathEscapingRoot_ReturnsNull(string relativePath)
    {
        Directory.CreateDirectory(Path.Combine(_root, "Trump"));
        var source = new FileSystemFolderSource(_root);

        Assert.Null(source.GetEntries(relativePath));
    }

    [Fact]
    public void AbsolutePathOutsideRoot_ReturnsNull()
    {
        var source = new FileSystemFolderSource(_root);

        Assert.Null(source.GetEntries(Path.GetTempPath()));
    }

    [Fact]
    public void RootWithTrailingSeparator_StillResolvesChildren()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Trump"));
        File.WriteAllText(Path.Combine(_root, "Trump", "wall.wav"), "");
        var source = new FileSystemFolderSource(_root + Path.DirectorySeparatorChar);

        var entry = Assert.Single(source.GetEntries("Trump")!);
        Assert.Equal("Trump/wall.wav", entry.RelativePath);
    }

    [Fact]
    public void FileInsteadOfFolder_ReturnsNull()
    {
        File.WriteAllText(Path.Combine(_root, "clip.wav"), "");
        var source = new FileSystemFolderSource(_root);

        Assert.Null(source.GetEntries("clip.wav"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
