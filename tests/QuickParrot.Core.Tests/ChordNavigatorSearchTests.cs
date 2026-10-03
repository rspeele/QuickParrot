using QuickParrot.Core.Keyboard;
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
        var elsewhere = _source.AddFolder("", "Elsewhere");
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
