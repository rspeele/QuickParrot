using QuickParrot.Core.Keyboard;
using QuickParrot.Core.Library;
using QuickParrot.Core.Navigation;
using QuickParrot.Core.Tests.Fakes;

namespace QuickParrot.Core.Tests;

public sealed class ChordNavigatorSaveNavigationTests
{
    [Fact]
    public void SavingShowsConfirmation_ExpiryHidesIt_AndNextSessionStartsThere()
    {
        var source = new FakeFolderSource();
        var folder = source.AddFolder("", "Quotes");
        var nav = new ChordNavigator(source);
        nav.Handle(new ChordPressed());
        Assert.False(nav.ViewState!.ShowSaveNavigationHint);
        nav.Handle(new DigitPressed(1, false));
        Assert.True(nav.ViewState!.ShowSaveNavigationHint);

        Assert.Equal([new PersistPath(folder)], nav.Handle(new SaveNavigationPressed()));
        Assert.Equal(folder, nav.PersistentPath);
        Assert.True(nav.ViewState!.SaveNavigationConfirmed);
        Assert.True(nav.ViewState.ShowSaveNavigationHint);
        nav.ExpireSaveNavigationConfirmation();
        Assert.False(nav.ViewState!.SaveNavigationConfirmed);
        Assert.False(nav.ViewState.ShowSaveNavigationHint);
        nav.Handle(new ChordReleased());
        nav.Handle(new ChordPressed());
        Assert.Equal(folder, nav.ViewState!.FolderPath);
        Assert.False(nav.ViewState.ShowSaveNavigationHint);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NavigatingDoesNotSave_EvenWithShift_AndReturningToSavedFolderHidesHint(bool shift)
    {
        var source = new FakeFolderSource();
        var folder = source.AddFolder("", "Quotes");
        var nav = new ChordNavigator(source, folder);
        nav.Handle(new ChordPressed());
        Assert.Empty(nav.Handle(new DigitPressed(0, shift)));
        Assert.True(nav.ViewState!.ShowSaveNavigationHint);
        Assert.Equal(folder, nav.PersistentPath);
        Assert.Empty(nav.Handle(new DigitPressed(1, shift)));
        Assert.False(nav.ViewState!.ShowSaveNavigationHint);
    }

    [Fact]
    public void SaveAndExpiry_DoNotReadSource_OrRemapDisplayedEntries()
    {
        var source = new ReadGuardSource();
        source.Folders.AddFile("", "b.wav");
        var nav = new ChordNavigator(source);
        nav.Handle(new ChordPressed());
        source.Folders.AddFile("", "a.wav");
        source.PreventReads = true;
        Assert.Empty(nav.Handle(new SaveNavigationPressed()));
        nav.ExpireSaveNavigationConfirmation();
        Assert.Equal(["b.wav"], nav.ViewState!.WheelEntries.Select(e => e.Name));
        Assert.Equal([new PlayClip("b.wav")], nav.Handle(new DigitPressed(1, false)));
    }

    [Fact]
    public void AssignMode_HidesHint_AndIgnoresSaving()
    {
        var source = new FakeFolderSource();
        source.AddFolder("", "Quotes");
        var nav = new ChordNavigator(source);
        nav.Handle(new ChordPressed());
        nav.Handle(new DigitPressed(1, false));
        nav.Handle(new FavoritePressed(1, true));
        Assert.Empty(nav.Handle(new SaveNavigationPressed()));
        Assert.Equal("", nav.PersistentPath);
        Assert.False(nav.ViewState!.ShowSaveNavigationHint);
        Assert.False(nav.ViewState.SaveNavigationConfirmed);
    }

    [Fact]
    public void SavingAtStartingFolderConfirms_AndReleaseDoesNotStopPlayback()
    {
        var nav = new ChordNavigator(new FakeFolderSource());
        nav.Handle(new ChordPressed());
        Assert.Empty(nav.Handle(new SaveNavigationPressed()));
        Assert.True(nav.ViewState!.SaveNavigationConfirmed);
        Assert.Empty(nav.Handle(new SaveNavigationPressed()));
        Assert.Empty(nav.Handle(new ChordReleased()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InactiveOrSpentSessions_IgnoreSaving(bool spent)
    {
        var source = new FakeFolderSource();
        source.AddFile("", "clip.wav");
        var nav = new ChordNavigator(source);
        if (spent)
        {
            nav.Handle(new ChordPressed());
            nav.Handle(new DigitPressed(1, false));
        }

        Assert.Empty(nav.Handle(new SaveNavigationPressed()));
        nav.ExpireSaveNavigationConfirmation();
        Assert.Null(nav.ViewState);
        Assert.Equal("", nav.PersistentPath);
    }

    [Fact]
    public void NavigationClearsFlash_AndShowsTheUnsavedHint()
    {
        var source = new FakeFolderSource();
        var folder = source.AddFolder("", "Quotes");
        var nav = new ChordNavigator(source);
        nav.Handle(new ChordPressed());
        nav.Handle(new SaveNavigationPressed());
        nav.Handle(new DigitPressed(1, false));
        Assert.False(nav.ViewState!.SaveNavigationConfirmed);
        Assert.True(nav.ViewState.ShowSaveNavigationHint);
        Assert.Equal(folder, nav.ViewState.FolderPath);
        nav.Handle(new DigitPressed(0, false));
        Assert.False(nav.ViewState!.ShowSaveNavigationHint);
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
