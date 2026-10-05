using QuickParrot.Core.Keyboard;
using QuickParrot.Core.Library;
using QuickParrot.Core.Navigation;
using QuickParrot.Core.Tests.Fakes;

namespace QuickParrot.Core.Tests;

public sealed class ChordNavigatorFragmentTests
{
    private readonly FakeFolderSource _source = new();

    [Fact]
    public void OrdinaryNavigationAndSearchExcludeOnlyTopLevelFragments()
    {
        var fragments = _source.AddFolder("", "fRaGmEnTs");
        var speaker = _source.AddFolder(fragments, "Speaker");
        _source.AddFile(speaker, "Hidden.wav");
        var quotes = _source.AddFolder("", "Quotes");
        var nested = _source.AddFolder(quotes, "Fragments");
        var normal = _source.AddFile(nested, "Visible.wav");
        var navigator = new ChordNavigator(_source);
        navigator.Handle(new ChordPressed());
        Assert.Equal("Quotes", Assert.Single(navigator.ViewState!.WheelEntries).Name);
        navigator.Handle(new DigitPressed(1, false));
        Assert.Equal("Fragments", Assert.Single(navigator.ViewState!.WheelEntries).Name);
        navigator.Handle(new SearchPressed());
        Assert.Equal("Visible.wav", Assert.Single(navigator.ViewState!.WheelEntries).Name);
        Assert.Equal(new PlayClip(normal), Assert.Single(navigator.Handle(new SearchSelectionPressed(1))));
        Assert.Equal(2, ((IFolderSource)_source).GetAllClips().Count);
        Assert.Equal(2, LibraryView.Root(_source).Entries.Count);
    }

    [Theory]
    [InlineData("Fragments")]
    [InlineData("Fragments/Speaker")]
    [InlineData("fragments/Speaker/Missing")]
    public void PersistedFragmentPathFallsBackAndPersistsRoot(string path)
    {
        var fragments = _source.AddFolder("", "Fragments");
        _source.AddFolder(fragments, "Speaker");
        var navigator = new ChordNavigator(_source, path);
        Assert.Equal(new PersistPath(""), Assert.Single(navigator.Handle(new ChordPressed())));
        Assert.Equal("", navigator.PersistentPath);
        Assert.Equal("", navigator.ViewState!.FolderPath);
    }

    [Fact]
    public void FragmentSearchIncludesAllSpeakersAndNestedClipsButExcludesRootClips()
    {
        var root = _source.AddFolder("", "FRAGMENTS");
        _source.AddFile(root, "Unassigned.wav");
        var first = _source.AddFolder(root, "Alice");
        var deep = _source.AddFolder(first, "Greetings");
        _source.AddFile(deep, "Hello.wav");
        var second = _source.AddFolder(root, "Bob");
        _source.AddFile(second, "Goodbye.wav");
        _source.AddFile("", "Ordinary.wav");
        var navigator = Start();
        Assert.True(navigator.ViewState!.IsFragmentSearch);
        Assert.Null(navigator.ViewState.FragmentSpeaker);
        Assert.Equal(["Goodbye.wav", "Hello.wav"], navigator.ViewState.WheelEntries.Select(entry => entry.Name));
        Assert.Equal(new LibrarySearchFolderContext("Alice", "Greetings") { ColorIndex = 0 },
            navigator.ViewState.WheelEntries[1].FolderContext);
        navigator.Handle(new ChordReleased());
        Assert.NotNull(navigator.ViewState);
    }

    [Fact]
    public void AppendingClearsQueryAndLocksTheFirstSpeakerAcrossNestedFolders()
    {
        var (alice, bob) = Speakers();
        _source.AddFile(alice, "Hello.wav");
        var deeper = _source.AddFolder(alice, "Words");
        _source.AddFile(deeper, "World.wav");
        _source.AddFile(bob, "Hello.wav");
        var navigator = Start();
        navigator.Handle(new SearchTextEntered("alice hello"));
        Assert.Empty(navigator.Handle(new FragmentSelectionPressed(1)));
        Assert.Equal("", navigator.ViewState!.SearchQuery);
        Assert.Equal("Alice", navigator.ViewState.FragmentSpeaker);
        Assert.Equal(["Hello"], navigator.ViewState.FragmentNames.ToArray());
        Assert.Equal(2, navigator.ViewState.WheelEntries.Length);
        navigator.Handle(new SearchTextEntered("world"));
        Assert.Equal("World.wav", Assert.Single(navigator.ViewState.WheelEntries).Name);
        navigator.Handle(new FragmentSelectionPressed(1));
        Assert.Equal(["Hello", "World"], navigator.ViewState.FragmentNames.ToArray());
    }

    [Fact]
    public void SubmitPlaysOnlyQueuedClipsInOrderAndReopeningStartsFresh()
    {
        var (alice, _) = Speakers();
        var hello = _source.AddFile(alice, "Hello.wav");
        var world = _source.AddFile(alice, "World.wav");
        _source.AddFile(alice, "Unselected.wav");
        var navigator = Start();
        Append(navigator, "hello");
        Append(navigator, "world");
        navigator.Handle(new SearchTextEntered("unselected"));
        var phrase = Assert.IsType<PlayPhrase>(Assert.Single(navigator.Handle(new FragmentSubmitPressed())));
        Assert.Equal([hello, world], phrase.RelativePaths);
        Assert.Null(navigator.ViewState);
        Assert.Empty(navigator.Handle(new FragmentSelectionPressed(1)));
        Assert.Empty(navigator.Handle(new ChordReleased()));
        navigator.Handle(new ChordPressed());
        navigator.Handle(new FragmentsPressed());
        Assert.Empty(navigator.ViewState!.FragmentNames);
        Assert.Null(navigator.ViewState.FragmentSpeaker);
        Assert.Equal("", navigator.ViewState.SearchQuery);
    }

    [Fact]
    public void DoubleEmptyBackspaceRemovesOneAndLastRemovalUnlocksSpeaker()
    {
        var (alice, bob) = Speakers();
        _source.AddFile(alice, "Hello.wav");
        _source.AddFile(bob, "Goodbye.wav");
        var navigator = Start();
        Append(navigator, "hello");
        Append(navigator, "hello");
        navigator.Handle(new FragmentBackspacePressed());
        Assert.Equal(2, navigator.ViewState!.FragmentNames.Length);
        navigator.Handle(new FragmentBackspacePressed());
        Assert.Single(navigator.ViewState.FragmentNames);
        Assert.Equal("Alice", navigator.ViewState.FragmentSpeaker);
        navigator.Handle(new FragmentBackspacePressed());
        navigator.Handle(new FragmentBackspacePressed());
        Assert.Empty(navigator.ViewState.FragmentNames);
        Assert.Null(navigator.ViewState.FragmentSpeaker);
        Assert.Equal(2, navigator.ViewState.WheelEntries.Length);
    }

    [Fact]
    public void BackspaceRepeatCannotRemoveQueuedFragmentsOrArmEmptyDeletion()
    {
        var (alice, _) = Speakers();
        _source.AddFile(alice, "Hello.wav");
        var navigator = Start();
        Append(navigator, "hello");
        navigator.Handle(new SearchTextEntered("e\u0301😀"));
        navigator.Handle(new FragmentBackspacePressed());
        Assert.Equal("e\u0301", navigator.ViewState!.SearchQuery);
        navigator.Handle(new FragmentBackspacePressed(true));
        Assert.Equal("", navigator.ViewState.SearchQuery);
        navigator.Handle(new FragmentBackspacePressed(true));
        navigator.Handle(new FragmentBackspacePressed());
        Assert.Single(navigator.ViewState.FragmentNames);
        navigator.Handle(new FragmentBackspacePressed(true));
        Assert.Single(navigator.ViewState.FragmentNames);
        navigator.Handle(new FragmentBackspacePressed());
        Assert.Empty(navigator.ViewState.FragmentNames);
    }

    [Fact]
    public void TypingOrSelectionBreaksTheEmptyBackspacePair()
    {
        var (alice, _) = Speakers();
        _source.AddFile(alice, "Hello.wav");
        var navigator = Start();
        Append(navigator, "hello");
        navigator.Handle(new FragmentBackspacePressed());
        navigator.Handle(new SearchTextEntered("x"));
        navigator.Handle(new FragmentBackspacePressed());
        navigator.Handle(new FragmentBackspacePressed());
        Assert.Single(navigator.ViewState!.FragmentNames);
        navigator.Handle(new FragmentSelectionPressed(1));
        navigator.Handle(new FragmentBackspacePressed());
        Assert.Equal(2, navigator.ViewState.FragmentNames.Length);
    }

    [Fact]
    public void CancelDiscardsQueuedFragmentsAndEmptySubmitKeepsSearchOpen()
    {
        var (alice, _) = Speakers();
        _source.AddFile(alice, "Hello.wav");
        var navigator = Start();
        Assert.Empty(navigator.Handle(new FragmentSubmitPressed()));
        Assert.NotNull(navigator.ViewState);
        Append(navigator, "hello");
        Assert.Empty(navigator.Handle(new ChordCancelled()));
        Assert.Null(navigator.ViewState);
        navigator.Handle(new ChordPressed());
        navigator.Handle(new FragmentsPressed());
        Assert.Empty(navigator.ViewState!.FragmentNames);
        Assert.Null(navigator.ViewState.FragmentSpeaker);
    }

    [Fact]
    public void ProgressUpdatesDoNotAppendAndInvalidSelectionsKeepSearchOpen()
    {
        var (alice, _) = Speakers();
        _source.AddFile(alice, "Hello.wav");
        var navigator = Start();
        navigator.Handle(new FragmentHoldProgressChanged(.75));
        Assert.Equal(.75, navigator.ViewState!.FragmentHoldProgress);
        Assert.Empty(navigator.ViewState.FragmentNames);
        Assert.Empty(navigator.Handle(new FragmentSelectionPressed(9)));
        Assert.NotNull(navigator.ViewState);
    }

    private (string Alice, string Bob) Speakers()
    {
        var fragments = _source.AddFolder("", "Fragments");
        return (_source.AddFolder(fragments, "Alice"), _source.AddFolder(fragments, "Bob"));
    }

    private ChordNavigator Start()
    {
        var navigator = new ChordNavigator(_source);
        navigator.Handle(new ChordPressed());
        navigator.Handle(new FragmentsPressed());
        return navigator;
    }

    private static void Append(ChordNavigator navigator, string query)
    {
        navigator.Handle(new SearchTextEntered(query));
        navigator.Handle(new FragmentSelectionPressed(1));
    }
}
