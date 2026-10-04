using QuickParrot.Core.Keyboard;
using QuickParrot.Core.Library;
using QuickParrot.Core.Navigation;
using QuickParrot.Core.Tests.Fakes;

namespace QuickParrot.Core.Tests;

public sealed class ChordNavigatorSearchTests
{
    private readonly FakeFolderSource _source = new();

    [Fact]
    public void SearchSurvivesReleaseAndFindsAndTermsAcrossEntireLibrary()
    {
        var current = _source.AddFolder("", "Current");
        var elsewhere = _source.AddFolder("", "Distant");
        _source.AddFile(current, "Number one.wav");
        var match = _source.AddFile(elsewhere, "He was Number One.wav");
        _source.AddFile(elsewhere, "One.wav");
        var navigator = Start(current);
        Assert.Empty(navigator.Handle(new ChordReleased()));
        navigator.Handle(new SearchTextEntered("oNe  HE"));
        Assert.Equal("oNe  HE", navigator.ViewState!.SearchQuery);
        Assert.Equal("He was Number One.wav", Assert.Single(navigator.ViewState.WheelEntries).Name);
        Assert.Equal(new PlayClip(match), Assert.Single(navigator.Handle(new SearchSelectionPressed(1))));
        Assert.Null(navigator.ViewState);
        Assert.Empty(navigator.Handle(new ChordReleased()));
        Assert.Equal(current, navigator.PersistentPath);
    }

    [Fact]
    public void SearchReachesClipsPastNavigationCapacityAndDeepFolders()
    {
        for (var i = 0; i < 100; i++)
            _source.AddFile("", $"a{i:000}.wav");
        var folder = "";
        for (var i = 0; i < 15; i++)
            folder = _source.AddFolder(folder, $"depth{i}");
        var deep = _source.AddFile(folder, "Needle.wav");
        var navigator = Start();
        navigator.Handle(new SearchTextEntered("NEEDLE"));
        Assert.Single(navigator.ViewState!.WheelEntries);
        Assert.Equal(new PlayClip(deep), Assert.Single(navigator.Handle(new SearchSelectionPressed(1))));
    }

    [Fact]
    public void FolderSearchPublishesContextAndSelectsTheDisplayedClip()
    {
        var top = _source.AddFolder("", "Quotes");
        var middle = _source.AddFolder(top, "Shared");
        var parent = _source.AddFolder(middle, "Show");
        var clip = _source.AddFile(parent, "One.wav");
        _source.AddFile("", "One.wav");
        var navigator = Start();
        navigator.Handle(new SearchTextEntered("shared one"));
        var result = Assert.Single(navigator.ViewState!.WheelEntries);
        Assert.Equal("One.wav", result.Name);
        Assert.Equal(new LibrarySearchFolderContext("Quotes", "Show") { ColorIndex = 0 }, result.FolderContext);
        Assert.Equal(1, result.Number);
        Assert.False(result.IsFolder);
        Assert.Equal(new PlayClip(clip), Assert.Single(navigator.Handle(new SearchSelectionPressed(result.Number))));
    }

    [Fact]
    public void RootSearchResultsAndOrdinaryNavigationHaveNoContext()
    {
        _source.AddFile("", "Root.wav");
        var top = _source.AddFolder("", "Quotes");
        _source.AddFile(top, "Other.wav");
        var navigator = new ChordNavigator(_source, top);
        navigator.Handle(new ChordPressed());
        Assert.Null(Assert.Single(navigator.ViewState!.WheelEntries).FolderContext);
        navigator.Handle(new SearchPressed());
        navigator.Handle(new SearchTextEntered("root"));
        Assert.Null(Assert.Single(navigator.ViewState!.WheelEntries).FolderContext);
    }

    [Fact]
    public void FolderColorsAndMetadataUseTheWholeSearchSnapshotAndSurviveQueryChanges()
    {
        for (var index = 0; index < 12; index++)
        {
            var folder = _source.AddFolder("", $"Folder{index:00}");
            _source.AddFile(folder, $"A{index:00}.wav");
        }
        var targetFolder = _source.AddFolder("", "Zulu");
        _source.AddFile(targetFolder, "Target.wav");
        var navigator = Start();
        Assert.DoesNotContain(navigator.ViewState!.WheelEntries, entry => entry.Name == "Target.wav");
        navigator.Handle(new SearchTextEntered("target"));
        var context = Assert.Single(navigator.ViewState!.WheelEntries).FolderContext;
        Assert.Equal(12 % LibrarySearch.FolderColorCount, context!.ColorIndex);
        var newFolder = _source.AddFolder("", "Aardvark");
        _source.AddFile(newFolder, "Target new.wav");
        navigator.Handle(new SearchBackspacePressed());
        Assert.Same(context, Assert.Single(navigator.ViewState!.WheelEntries).FolderContext);
        navigator.Handle(new SearchTextEntered("t"));
        Assert.Same(context, Assert.Single(navigator.ViewState!.WheelEntries).FolderContext);
    }

    [Fact]
    public void TopNineAreDeterministicAndSelectionUsesDisplayedSnapshot()
    {
        var second = _source.AddFolder("", "z");
        var first = _source.AddFolder("", "a");
        for (var i = 12; i >= 1; i--)
            _source.AddFile(second, $"Sound {i:00}.wav");
        var winner = _source.AddFile(first, "Sound 01.wav");
        var navigator = Start();
        Assert.Equal(9, navigator.ViewState!.WheelEntries.Length);
        Assert.Equal(Enumerable.Range(1, 9), navigator.ViewState.WheelEntries.Select(e => e.Number));
        _source.AddFile("", "A newly added clip.wav");
        Assert.Equal(new PlayClip(winner), Assert.Single(navigator.Handle(new SearchSelectionPressed(1))));
    }

    [Fact]
    public void BackspaceRemovesTextElementsAndDigitsAreReserved()
    {
        var navigator = Start();
        navigator.Handle(new SearchTextEntered("b12 e\u0301😀"));
        Assert.Equal("b e\u0301😀", navigator.ViewState!.SearchQuery);
        navigator.Handle(new SearchBackspacePressed());
        Assert.Equal("b e\u0301", navigator.ViewState!.SearchQuery);
        navigator.Handle(new SearchBackspacePressed());
        Assert.Equal("b ", navigator.ViewState!.SearchQuery);
    }

    [Fact]
    public void CancelAndEmptySelectionCloseWithoutPlayingOrStopping()
    {
        var navigator = Start();
        Assert.Empty(navigator.Handle(new ChordCancelled()));
        Assert.Null(navigator.ViewState);
        Assert.Empty(navigator.Handle(new ChordReleased()));
        navigator.Handle(new ChordPressed());
        navigator.Handle(new SearchPressed());
        Assert.Empty(navigator.Handle(new SearchSelectionPressed(1)));
        Assert.Null(navigator.ViewState);
    }

    private ChordNavigator Start(string path = "")
    {
        var navigator = new ChordNavigator(_source, path);
        navigator.Handle(new ChordPressed());
        navigator.Handle(new SearchPressed());
        return navigator;
    }
}
