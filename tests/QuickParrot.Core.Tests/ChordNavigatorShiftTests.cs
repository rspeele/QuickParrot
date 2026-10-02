using QuickParrot.Core.Keyboard;
using QuickParrot.Core.Library;
using QuickParrot.Core.Navigation;
using QuickParrot.Core.Tests.Fakes;

namespace QuickParrot.Core.Tests;

public sealed class ChordNavigatorShiftTests
{
    [Fact]
    public void ShiftSavesCurrentFolder_ReleaseKeepsIt_AndNextSessionStartsThere()
    {
        var source = new FakeFolderSource();
        var folder = source.AddFolder("", "Quotes");
        var nav = new ChordNavigator(source);
        nav.Handle(new ChordPressed());
        Assert.False(nav.ViewState!.ShowSaveNavigationHint);
        nav.Handle(new DigitPressed(1, false));
        Assert.True(nav.ViewState!.ShowSaveNavigationHint);
        Assert.False(nav.ViewState.ShiftHeld);

        Assert.Equal([new PersistPath(folder)], nav.Handle(new ShiftChanged(true)));
        Assert.Equal(folder, nav.PersistentPath);
        Assert.True(nav.ViewState!.ShiftHeld);
        Assert.True(nav.ViewState.ShowSaveNavigationHint);
        var savedState = nav.ViewState;
        Assert.Empty(nav.Handle(new ShiftChanged(true)));
        Assert.Same(savedState, nav.ViewState);
        Assert.Empty(nav.Handle(new ShiftChanged(false)));
        Assert.False(nav.ViewState!.ShiftHeld);
        Assert.Equal(folder, nav.PersistentPath);

        nav.Handle(new ChordReleased());
        nav.Handle(new ChordPressed(true));
        Assert.Equal(folder, nav.ViewState!.FolderPath);
        Assert.True(nav.ViewState.ShiftHeld);
        Assert.False(nav.ViewState.ShowSaveNavigationHint);
    }

    [Fact]
    public void StartingPathStaysFrozen_WhenShiftNavigationChangesPersistentPath()
    {
        var source = new FakeFolderSource();
        var folder = source.AddFolder("", "Quotes");
        var nav = new ChordNavigator(source, folder);
        nav.Handle(new ChordPressed(true));

        Assert.Equal([new PersistPath("")], nav.Handle(new DigitPressed(0, true)));
        Assert.True(nav.ViewState!.ShowSaveNavigationHint);
        Assert.True(nav.ViewState.ShiftHeld);
        Assert.Equal([new PersistPath(folder)], nav.Handle(new DigitPressed(1, true)));
        Assert.False(nav.ViewState!.ShowSaveNavigationHint);
    }

    [Fact]
    public void ShiftChanges_DoNotReadSource_OrRemapDisplayedEntries()
    {
        var source = new ReadGuardSource();
        source.Folders.AddFile("", "b.wav");
        var nav = new ChordNavigator(source);
        nav.Handle(new ChordPressed());
        source.Folders.AddFile("", "a.wav");
        source.PreventReads = true;

        Assert.Empty(nav.Handle(new ShiftChanged(true)));
        Assert.Empty(nav.Handle(new ShiftChanged(false)));
        Assert.Equal(["b.wav"], nav.ViewState!.WheelEntries.Select(e => e.Name));
        Assert.Equal([new PlayClip("b.wav")], nav.Handle(new DigitPressed(1, false)));
    }

    [Fact]
    public void AssignMode_HidesHint_AndDoesNotSaveOnShiftOrNavigation()
    {
        var source = new FakeFolderSource();
        var folder = source.AddFolder("", "Quotes");
        source.AddFolder(folder, "More");
        var nav = new ChordNavigator(source);
        nav.Handle(new ChordPressed());
        nav.Handle(new DigitPressed(1, false));
        Assert.True(nav.ViewState!.ShowSaveNavigationHint);
        nav.Handle(new FavoritePressed(1, true));
        Assert.False(nav.ViewState!.ShowSaveNavigationHint);

        Assert.Empty(nav.Handle(new ShiftChanged(false)));
        Assert.Empty(nav.Handle(new ShiftChanged(true)));
        Assert.Empty(nav.Handle(new DigitPressed(1, true)));
        Assert.Equal("", nav.PersistentPath);
        Assert.False(nav.ViewState!.ShowSaveNavigationHint);
    }

    [Fact]
    public void ShiftChanges_AtStartingFolderKeepBareTapBehavior()
    {
        var nav = new ChordNavigator(new FakeFolderSource());
        nav.Handle(new ChordPressed());

        Assert.Empty(nav.Handle(new ShiftChanged(true)));
        Assert.Empty(nav.Handle(new ShiftChanged(false)));
        Assert.Equal([new StopPlayback()], nav.Handle(new ChordReleased()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InactiveOrSpentSessions_IgnoreShiftChanges(bool spent)
    {
        var source = new FakeFolderSource();
        source.AddFile("", "clip.wav");
        var nav = new ChordNavigator(source);
        if (spent)
        {
            nav.Handle(new ChordPressed());
            nav.Handle(new DigitPressed(1, false));
        }

        Assert.Empty(nav.Handle(new ShiftChanged(true)));
        Assert.Null(nav.ViewState);
        Assert.Equal("", nav.PersistentPath);
    }

    private sealed class ReadGuardSource : IFolderSource
    {
        public FakeFolderSource Folders { get; } = new();
        public bool PreventReads { get; set; }

        public IReadOnlyList<FolderEntry>? GetEntries(string path) =>
            PreventReads ? throw new InvalidOperationException("Unexpected source read") : Folders.GetEntries(path);

        public string? GetFullPath(string path) => Folders.GetFullPath(path);
        public bool ClipExists(string path) => Folders.ClipExists(path);
    }
}
