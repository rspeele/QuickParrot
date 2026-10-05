using QuickParrot.Core.Keyboard;
using QuickParrot.Core.Library;
using QuickParrot.Core.Navigation;
using QuickParrot.Core.Tests.Fakes;

namespace QuickParrot.Core.Tests;

public sealed class ChordNavigatorVariantTests
{
    private readonly FakeFolderSource _source = new();

    [Fact]
    public void NormalSearchRollsAtSelectionAndRetainsRealFolderContextAndSnapshot()
    {
        var folder = _source.AddFolder("", "Quotes");
        _source.AddFile(folder, "well.done_1.mp3");
        var second = _source.AddFile(folder, "well.done_2.mp3");
        var rolls = 0;
        var navigator = new ChordNavigator(_source, randomIndex: count =>
        {
            rolls++;
            Assert.Equal(2, count);
            return 1;
        });
        Open(navigator, fragments: false);
        var result = Assert.Single(navigator.ViewState!.WheelEntries);
        Assert.Equal("well.done", result.Name);
        Assert.Equal("well.done", result.DisplayName);
        Assert.Equal(new LibrarySearchFolderContext("Quotes", "Quotes") { ColorIndex = 0 }, result.FolderContext);
        navigator.Handle(new SearchTextEntered("quotes well.done"));
        navigator.Handle(new SearchBackspacePressed());
        Assert.Equal(0, rolls);
        _source.AddFile(folder, "well.done_3.mp3");
        Assert.Equal(new PlayClip(second), Assert.Single(navigator.Handle(new SearchSelectionPressed(1))));
        Assert.Equal(1, rolls);
    }

    [Fact]
    public void ReopeningNormalSearchRollsAgainForTheSameResult()
    {
        var first = _source.AddFile("", "Word_1.mp3");
        var second = _source.AddFile("", "Word_2.mp3");
        var choices = new Queue<int>([0, 1]);
        var navigator = new ChordNavigator(_source, randomIndex: _ => choices.Dequeue());
        Open(navigator, fragments: false);
        Assert.Equal(new PlayClip(first), Assert.Single(navigator.Handle(new SearchSelectionPressed(1))));
        Open(navigator, fragments: false);
        Assert.Equal(new PlayClip(second), Assert.Single(navigator.Handle(new SearchSelectionPressed(1))));
        Assert.Empty(choices);
    }

    [Fact]
    public void FragmentAppendRollsEachTimeAndQueuedPathsAndBaseNamesSurviveUntilSubmit()
    {
        var fragments = _source.AddFolder("", "Fragments");
        var alice = _source.AddFolder(fragments, "Alice");
        var greetings = _source.AddFolder(alice, "Greetings");
        var first = _source.AddFile(greetings, "well.done_1.mp3");
        var second = _source.AddFile(greetings, "well.done_2.mp3");
        var bob = _source.AddFolder(fragments, "Bob");
        _source.AddFile(bob, "well.done_1.mp3");
        _source.AddFile(bob, "well.done_2.mp3");
        var choices = new Queue<int>([1, 0]);
        var navigator = new ChordNavigator(_source, randomIndex: count =>
        {
            Assert.Equal(2, count);
            return choices.Dequeue();
        });
        Open(navigator, fragments: true);
        Assert.Equal(2, navigator.ViewState!.WheelEntries.Length);
        navigator.Handle(new SearchTextEntered("alice well.done"));
        navigator.Handle(new FragmentHoldProgressChanged(.5));
        Assert.Equal(2, choices.Count);
        Assert.Empty(navigator.Handle(new FragmentSelectionPressed(1)));
        Assert.Equal("Alice", navigator.ViewState.FragmentSpeaker);
        var result = Assert.Single(navigator.ViewState.WheelEntries);
        Assert.Equal("well.done", result.DisplayName);
        Assert.Equal(new LibrarySearchFolderContext("Alice", "Greetings") { ColorIndex = 0 }, result.FolderContext);
        Assert.Equal("", navigator.ViewState.SearchQuery);
        navigator.Handle(new FragmentSelectionPressed(1));
        Assert.Equal(["well.done", "well.done"], navigator.ViewState.FragmentNames.ToArray());
        Assert.Empty(choices);
        navigator.Handle(new SearchTextEntered("bob"));
        Assert.Empty(navigator.ViewState.WheelEntries);
        var phrase = Assert.IsType<PlayPhrase>(Assert.Single(navigator.Handle(new FragmentSubmitPressed())));
        Assert.Equal([second, first], phrase.RelativePaths);
    }

    [Fact]
    public void FolderNavigationAndFavoriteAssignmentStillSelectIndividualNumberedFiles()
    {
        var first = _source.AddFile("", "Word_1.mp3");
        _source.AddFile("", "Word_2.mp3");
        var navigator = new ChordNavigator(_source, randomIndex: _ => throw new InvalidOperationException());
        navigator.Handle(new ChordPressed());
        Assert.Equal(["Word_1.mp3", "Word_2.mp3"], navigator.ViewState!.WheelEntries.Select(entry => entry.Name));
        Assert.Equal(new PlayClip(first), Assert.Single(navigator.Handle(new DigitPressed(1, false))));
        navigator.Handle(new ChordReleased());
        navigator.Handle(new ChordPressed());
        navigator.Handle(new FavoritePressed(1, true));
        Assert.Equal(new AssignFavorite(1, first), Assert.Single(navigator.Handle(new DigitPressed(1, false))));
    }

    [Fact]
    public void SingletonAndInvalidSelectionsDoNotRoll()
    {
        _source.AddFile("", "Word_1.mp3");
        var navigator = new ChordNavigator(_source, randomIndex: _ => throw new InvalidOperationException());
        Open(navigator, fragments: false);
        Assert.Empty(navigator.Handle(new SearchSelectionPressed(9)));
        Open(navigator, fragments: false);
        Assert.Equal(new PlayClip("Word_1.mp3"), Assert.Single(navigator.Handle(new SearchSelectionPressed(1))));
    }

    private static void Open(ChordNavigator navigator, bool fragments)
    {
        navigator.Handle(new ChordPressed());
        navigator.Handle(fragments ? new FragmentsPressed() : new SearchPressed());
    }
}
