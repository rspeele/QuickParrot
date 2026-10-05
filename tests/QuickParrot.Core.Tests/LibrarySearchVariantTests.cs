using QuickParrot.Core.Library;

namespace QuickParrot.Core.Tests;

public sealed class LibrarySearchVariantTests
{
    [Fact]
    public void VariantsGroupWithinCaseInsensitiveParentAndBaseAcrossAudioExtensions()
    {
        FolderEntry[] clips =
        [
            new("word_2.wav", false, "quotes/word_2.wav"),
            new("Word_1.mp3", false, "Quotes/Word_1.mp3"),
            new("Word_3.flac", false, "QUOTES/Word_3.flac"),
        ];
        var result = Assert.Single(LibrarySearch.Match(clips, "quotes word"));
        Assert.Equal("Word", result.Name);
        Assert.Equal("Word", result.PhraseName);
        Assert.Equal(clips[1], result.RepresentativeClip);
        Assert.Equal(3, result.Clips.Length);
        var reversed = Assert.Single(LibrarySearch.Match(clips.Reverse().ToArray(), ""));
        Assert.Equal(result.Name, reversed.Name);
        Assert.Equal(result.Clips.ToArray(), reversed.Clips.ToArray());
    }

    [Fact]
    public void SeparateFoldersAndSpeakersNeverShareVariants()
    {
        FolderEntry[] clips =
        [
            new("Word_1.mp3", false, "Fragments/Alice/Word_1.mp3"),
            new("Word_2.mp3", false, "Fragments/Alice/Word_2.mp3"),
            new("Word_1.mp3", false, "Fragments/Bob/Word_1.mp3"),
            new("Word_2.mp3", false, "Fragments/Bob/Word_2.mp3"),
            new("Word_1.mp3", false, "Fragments/Alice/Nested/Word_1.mp3"),
            new("Word_2.mp3", false, "Fragments/Alice/Nested/Word_2.mp3"),
        ];
        var results = LibrarySearch.Match(clips, "word");
        Assert.Equal(3, results.Length);
        Assert.All(results, result => Assert.Equal(2, result.Clips.Length));
        Assert.Equal(2, LibrarySearch.Match(clips, "alice").Length);
        Assert.Single(LibrarySearch.Match(clips, "alice nested"));
        Assert.Single(LibrarySearch.Match(clips, "bob"));
    }

    [Fact]
    public void UnnumberedClipStaysSeparateAndSingleNumberedClipKeepsItsName()
    {
        FolderEntry[] clips =
        [
            new("Word_1.mp3", false, "Word_1.mp3"),
            new("Word_2.mp3", false, "Word_2.mp3"),
            new("Word.mp3", false, "Word.mp3"),
            new("Alone_7.mp3", false, "Alone_7.mp3"),
        ];
        var results = LibrarySearch.Match(clips, "");
        Assert.Equal(["Alone_7.mp3", "Word", "Word.mp3"], results.Select(result => result.Name));
        Assert.Equal("Alone_7", results[0].PhraseName);
        Assert.Equal(2, results[1].Clips.Length);
        Assert.Single(results[2].Clips);
    }

    [Theory]
    [InlineData("_", "__")]
    [InlineData("_1a", "_2a")]
    [InlineData("_-1", "_-2")]
    [InlineData("_1.5", "_2.5")]
    [InlineData("_١", "_٢")]
    [InlineData("1", "2")]
    public void OnlyTrailingUnderscoreAsciiDigitsFormVariants(string firstSuffix, string secondSuffix)
    {
        var first = new FolderEntry($"Word{firstSuffix}.mp3", false, $"Word{firstSuffix}.mp3");
        var second = new FolderEntry($"Word{secondSuffix}.mp3", false, $"Word{secondSuffix}.mp3");
        var results = LibrarySearch.Match([first, second], "word");
        Assert.Equal(2, results.Length);
        Assert.All(results, result => Assert.Single(result.Clips));
    }

    [Fact]
    public void DottedAndUnderscoredBaseNamesRemainIntactAndSuffixIsNotSearchable()
    {
        FolderEntry[] clips =
        [
            new("well.done_today_0001.mp3", false, "Greetings/well.done_today_0001.mp3"),
            new("well.done_today_99999999999999999999999999.wav", false,
                "Greetings/well.done_today_99999999999999999999999999.wav"),
        ];
        var result = Assert.Single(LibrarySearch.Match(clips, "well.done_today greetings"));
        Assert.Equal("well.done_today", result.Name);
        Assert.Equal(result.Name, result.PhraseName);
        Assert.Empty(LibrarySearch.Match(clips, "_0001"));
        Assert.Empty(LibrarySearch.Match(clips, "mp3"));
    }

    [Fact]
    public void PathSeparatorAndCaseAliasesDoNotCountAsAdditionalVariants()
    {
        FolderEntry[] clips =
        [
            new("Word_1.mp3", false, "Folder/Word_1.mp3"),
            new("word_1.mp3", false, "folder\\word_1.mp3"),
        ];
        var result = Assert.Single(LibrarySearch.Match(clips, ""));
        Assert.Equal("Word_1.mp3", result.Name);
        Assert.Single(result.Clips);
        Assert.Equal(clips[0], result.SelectClip(_ => throw new InvalidOperationException()));
    }

    [Fact]
    public void GroupsCollapseBeforeSortingAndNineResultLimit()
    {
        var clips = Enumerable.Range(1, 20).Select(index =>
            new FolderEntry($"Alpha_{index}.mp3", false, $"Alpha_{index}.mp3"))
            .Concat(Enumerable.Range(1, 10).Select(index =>
                new FolderEntry($"Bravo{index:00}.wav", false, $"Bravo{index:00}.wav")))
            .Reverse().ToArray();
        var results = LibrarySearch.Match(clips, "");
        Assert.Equal(9, results.Length);
        Assert.Equal("Alpha", results[0].Name);
        Assert.Equal(20, results[0].Clips.Length);
        Assert.Equal(Enumerable.Range(1, 8).Select(index => $"Bravo{index:00}.wav"),
            results.Skip(1).Select(result => result.Name));
    }

    [Fact]
    public void SelectingEveryRandomIndexCanReachEveryRealVariant()
    {
        FolderEntry[] clips =
        [
            new("Word_1.mp3", false, "Word_1.mp3"),
            new("Word_2.mp3", false, "Word_2.mp3"),
            new("Word_3.mp3", false, "Word_3.mp3"),
        ];
        var result = Assert.Single(LibrarySearch.Match(clips, ""));
        for (var index = 0; index < clips.Length; index++)
        {
            var chosen = result.SelectClip(count =>
            {
                Assert.Equal(clips.Length, count);
                return index;
            });
            Assert.Equal(clips[index], chosen);
        }
    }
}
