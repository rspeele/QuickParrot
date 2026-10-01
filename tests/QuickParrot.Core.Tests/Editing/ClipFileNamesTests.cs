using QuickParrot.Core.Editing;
using QuickParrot.Core.Tests.Fakes;

namespace QuickParrot.Core.Tests.Editing;

public class ClipFileNamesTests
{
    [Theory]
    [InlineData("I'll be back", "I'll be back")]
    [InlineData("  Arnold:  I'll be back?  ", "Arnold I'll be back")]
    [InlineData("\"Get to the choppa\"", "Get to the choppa")]
    [InlineData("“Smart quotes”", "Smart quotes")]
    [InlineData("a/b\\c|d*e<f>g", "a b c d e f g")]
    [InlineData("tabs\tand\nnewlines", "tabs and newlines")]
    [InlineData("trailing dots...", "trailing dots")]
    [InlineData("...leading", "leading")]
    [InlineData("", "Clip")]
    [InlineData("  ?? ** ", "Clip")]
    [InlineData(null, "Clip")]
    public void Sanitize_ProducesASafeStem(string? input, string expected)
    {
        Assert.Equal(expected, ClipFileNames.Sanitize(input));
    }

    [Theory]
    [InlineData("CON", "CON_")]
    [InlineData("con", "con_")]
    [InlineData("nul.backup", "nul.backup_")]
    [InlineData("COM1", "COM1_")]
    [InlineData("LPT9", "LPT9_")]
    [InlineData("CONSOLE", "CONSOLE")]
    [InlineData("COM10", "COM10")]
    public void Sanitize_AvoidsReservedDeviceNames(string input, string expected)
    {
        Assert.Equal(expected, ClipFileNames.Sanitize(input));
    }

    [Fact]
    public void Sanitize_LimitsLength_WithoutSplittingSurrogatesOrEndingInASpace()
    {
        var name = new string('a', ClipFileNames.MaxStemLength - 1) + "😀 tail";

        var stem = ClipFileNames.Sanitize(name);

        Assert.Equal(ClipFileNames.MaxStemLength - 1, stem.Length);
        Assert.False(char.IsHighSurrogate(stem[^1]));
        Assert.Equal("aaaa", ClipFileNames.Sanitize(new string('a', 4) + new string(' ', 200) + "b")[..4]);
        Assert.False(ClipFileNames.Sanitize(new string('a', 79) + " b").EndsWith(' '));
    }

    [Fact]
    public void Sanitize_DropsLoneSurrogatesInsteadOfThrowing()
    {
        Assert.Equal("Bad Name", ClipFileNames.Sanitize("Bad\ud83d Name\ude00"));
    }

    [Fact]
    public void UniqueFileName_NumbersCollisions()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Wow.mp3", "Wow (2).mp3" };

        Assert.Equal("Wow (3).mp3", ClipFileNames.UniqueFileName("Wow", ".mp3", taken.Contains));
        Assert.Equal("Wow.wav", ClipFileNames.UniqueFileName("Wow", ".wav", taken.Contains));
        Assert.Equal("wow (3).mp3", ClipFileNames.UniqueFileName("wow", ".mp3", taken.Contains));
    }
}

public class LibraryFolderListTests
{
    [Fact]
    public void ListsFoldersDepthFirst_WithTheRootFirst()
    {
        var source = new FakeFolderSource();
        var movies = source.AddFolder("", "Movies");
        source.AddFolder(movies, "Arnold");
        source.AddFolder("", "Games");
        source.AddFile("", "loose.mp3");

        var folders = LibraryFolderList.Build(source);

        Assert.Equal(["", "Games", "Movies", "Movies/Arnold"], folders.Select(f => f.RelativePath));
        Assert.Equal([0, 1, 1, 2], folders.Select(f => f.Depth));
        Assert.Equal("Movies › Arnold", folders[3].Display);
        Assert.Equal(LibraryFolderList.RootName, folders[0].Display);
    }

    [Fact]
    public void Build_StopsAtTheLimits()
    {
        var source = new FakeFolderSource();
        var path = "";
        for (var i = 0; i < 5; i++)
            path = source.AddFolder(path, $"Level{i}");

        Assert.Equal(3, LibraryFolderList.Build(source, maxDepth: 2).Count);
        Assert.Equal(4, LibraryFolderList.Build(source, maxFolders: 4).Count);
    }

    [Theory]
    [InlineData("Movies/Arnold", "Movies/Arnold")]
    [InlineData("movies/arnold", "Movies/Arnold")]
    [InlineData("Movies/Gone/Deeper", "Movies")]
    [InlineData("Nope", "")]
    [InlineData(null, "")]
    public void Find_FallsBackToTheNearestAncestor(string? requested, string expected)
    {
        var source = new FakeFolderSource();
        source.AddFolder(source.AddFolder("", "Movies"), "Arnold");

        Assert.Equal(expected, LibraryFolderList.Find(LibraryFolderList.Build(source), requested).RelativePath);
    }
}
