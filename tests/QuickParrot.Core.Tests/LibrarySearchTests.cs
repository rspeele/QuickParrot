using QuickParrot.Core.Library;

namespace QuickParrot.Core.Tests;

public sealed class LibrarySearchTests
{
    [Fact]
    public void FolderColorsAreDeterministicCaseInsensitiveAndSkipRootClips()
    {
        FolderEntry[] clips =
        [
            new("Clip.wav", false, "zulu/Clip.wav"),
            new("Root.wav", false, "Root.wav"),
            new("Clip.wav", false, "Bravo/Clip.wav"),
            new("Another.wav", false, "bravo/Another.wav"),
            new("Clip.wav", false, "Alpha/Clip.wav"),
        ];
        var contexts = LibrarySearch.CreateFolderContexts(clips);
        var reversed = LibrarySearch.CreateFolderContexts(clips.Reverse().ToArray());
        foreach (var clip in clips)
            Assert.Equal(contexts[clip.RelativePath], reversed[clip.RelativePath]);
        Assert.Null(contexts["Root.wav"]);
        Assert.Equal(0, contexts["Alpha/Clip.wav"]!.ColorIndex);
        Assert.Equal(1, contexts["Bravo/Clip.wav"]!.ColorIndex);
        Assert.Equal(contexts["Bravo/Clip.wav"]!.ColorIndex, contexts["bravo/Another.wav"]!.ColorIndex);
        Assert.Equal(2, contexts["zulu/Clip.wav"]!.ColorIndex);
    }

    [Fact]
    public void FolderColorsWrapWhenThePaletteIsExhausted()
    {
        var clips = Enumerable.Range(0, LibrarySearch.FolderColorCount * 2 + 1)
            .Select(index => new FolderEntry("Clip.wav", false, $"Folder{index:00}/Clip.wav")).ToArray();
        var contexts = LibrarySearch.CreateFolderContexts(clips);
        Assert.Equal(LibrarySearch.FolderColorCount,
            contexts.Values.Select(context => context!.ColorIndex).Distinct().Count());
        Assert.Equal(contexts[clips[0].RelativePath]!.ColorIndex,
            contexts[clips[LibrarySearch.FolderColorCount].RelativePath]!.ColorIndex);
        Assert.Equal(contexts[clips[0].RelativePath]!.ColorIndex, contexts[clips[^1].RelativePath]!.ColorIndex);
    }

    [Theory]
    [InlineData("spoken")]
    [InlineData("category")]
    [InlineData("shared")]
    [InlineData("parent")]
    [InlineData("SPOKEN CATEGORY shared PARENT")]
    [InlineData(" parent  spoken ")]
    public void MatchesFilenameAndEveryRelativeFolder(string query)
    {
        var clip = new FolderEntry("Spoken.wav", false, "Category/Shared/Parent/Spoken.wav");
        Assert.Equal(clip, Assert.Single(LibrarySearch.Match([clip], query)).RepresentativeClip);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wav")]
    [InlineData("spoken absent")]
    public void EveryWordMustMatchAndExtensionIsExcluded(string query)
    {
        var clip = new FolderEntry("Spoken.wav", false, "Category/Shared/Parent/Spoken.wav");
        Assert.Empty(LibrarySearch.Match([clip], query));
    }

    [Fact]
    public void ExtensionTextStillMatchesWhenPresentInFolderName()
    {
        var rootClip = new FolderEntry("Spoken.wav", false, "Spoken.wav");
        var folderClip = new FolderEntry("Spoken.mp3", false, "Wav clips/Spoken.mp3");
        Assert.Equal(folderClip, Assert.Single(LibrarySearch.Match([rootClip, folderClip], "wav")).RepresentativeClip);
    }

    [Theory]
    [InlineData("Clip.wav", null, null)]
    [InlineData("Top/Clip.wav", "Top", "Top")]
    [InlineData("Top/Parent/Clip.wav", "Top", "Parent")]
    [InlineData("Top/Middle/Parent/Clip.wav", "Top", "Parent")]
    [InlineData("Top/One/Two/Parent/Clip.wav", "Top", "Parent")]
    [InlineData("Same/Same/Clip.wav", "Same", "Same")]
    [InlineData("Same/Middle/Same/Clip.wav", "Same", "Same")]
    [InlineData("Top\\Middle\\Parent\\Clip.wav", "Top", "Parent")]
    public void FolderContextShowsTopAndParentByLevel(string relativePath, string? topFolder, string? parentFolder)
    {
        var expected = topFolder is null ? null : new LibrarySearchFolderContext(topFolder, parentFolder!);
        Assert.Equal(expected, LibrarySearch.GetFolderContext(relativePath));
    }
}
