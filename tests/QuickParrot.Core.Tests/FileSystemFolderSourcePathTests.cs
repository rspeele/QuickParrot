using QuickParrot.Core.Library;

namespace QuickParrot.Core.Tests;

// GetFullPath is pure string work, so these need no files on disk.
public class FileSystemFolderSourcePathTests
{
    private const string Root = @"C:\Sounds";

    private readonly FileSystemFolderSource _source = new(Root);

    [Theory]
    [InlineData("clip.wav", @"C:\Sounds\clip.wav")]
    [InlineData("Movies/Arnold/be back.mp3", @"C:\Sounds\Movies\Arnold\be back.mp3")]
    [InlineData("Movies/../clip.wav", @"C:\Sounds\clip.wav")]
    public void MapsRelativePathUnderRoot(string relativePath, string expected)
    {
        Assert.Equal(expected, _source.GetFullPath(relativePath));
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("../Other/clip.wav")]
    [InlineData(@"..\Other\clip.wav")]
    [InlineData("Movies/../../clip.wav")]
    [InlineData(@"C:\Windows\Media\chimes.wav")]
    [InlineData(@"\server\share\clip.wav")]
    [InlineData("Movies/..")]
    public void RefusesPathsOutsideTheRootOrTheRootItself(string relativePath)
    {
        Assert.Null(_source.GetFullPath(relativePath));
    }

    [Fact]
    public void SiblingFolderSharingThePrefix_IsRefused()
    {
        Assert.Null(_source.GetFullPath("../Sounds2/clip.wav"));
    }
}
