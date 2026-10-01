using QuickParrot.Core.Library;

namespace QuickParrot.Core.Tests;

// Pure string work: no files need to exist on disk.
public class LibraryPathResolverTests
{
    private const string Root = @"C:\Sounds";

    [Theory]
    [InlineData("clip.wav", @"C:\Sounds\clip.wav")]
    [InlineData("Movies/Arnold", @"C:\Sounds\Movies\Arnold")]
    [InlineData("", @"C:\Sounds")]
    public void FullPath_JoinsTheRelativePathUnderTheRoot(string relativePath, string expected)
    {
        Assert.Equal(expected, LibraryPathResolver.FullPath(Root, relativePath));
    }

    [Theory]
    [InlineData(@"C:\Sounds\clip.wav", "clip.wav")]
    [InlineData(@"C:\Sounds\Movies\Arnold", "Movies/Arnold")]
    [InlineData(@"c:\sounds\MOVIES", "MOVIES")]
    [InlineData(@"C:\Sounds\", "")]
    [InlineData(@"C:\Sounds\Movies\", "Movies")]
    public void RelativePathWithin_MapsAFullPathBackToRelative(string fullPath, string expected)
    {
        Assert.Equal(expected, LibraryPathResolver.RelativePathWithin(Root, fullPath));
    }

    [Fact]
    public void RelativePathWithin_TheRootItself_IsTheEmptyPath()
    {
        Assert.Equal("", LibraryPathResolver.RelativePathWithin(Root, Root));
        Assert.Equal("", LibraryPathResolver.RelativePathWithin(Root + @"\", Root));
    }

    [Theory]
    [InlineData(@"C:\Other\clip.wav")]
    [InlineData(@"C:\Sounds2\clip.wav")] // sibling sharing the root's name as a prefix
    [InlineData(@"C:\Sounds2")]
    [InlineData(@"D:\Sounds\clip.wav")]
    public void RelativePathWithin_OutsideTheRoot_ReturnsNull(string fullPath)
    {
        Assert.Null(LibraryPathResolver.RelativePathWithin(Root, fullPath));
    }
}
