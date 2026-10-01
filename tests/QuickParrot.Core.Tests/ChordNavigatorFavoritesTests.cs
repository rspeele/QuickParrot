using System.Collections.Immutable;
using QuickParrot.Core.Favorites;
using QuickParrot.Core.Keyboard;
using QuickParrot.Core.Navigation;
using QuickParrot.Core.Tests.Fakes;

namespace QuickParrot.Core.Tests;

public class ChordNavigatorFavoritesTests
{
    private readonly FakeFolderSource _source = new();
    private readonly List<int> _panelRequests = [];
    private readonly ChordNavigator _nav;
    private string? _lastPlayedName = "Bruh";

    public ChordNavigatorFavoritesTests()
    {
        _source.AddFolder("", "Trump");
        _source.AddFile("Trump", "wall.wav");
        _source.AddFile("", "boom.wav");
        _nav = new ChordNavigator(_source);
    }

    // Answers NeedFavoritesPanel the way the engine does.
    private IReadOnlyList<NavigationAction> Handle(ChordEvent evt)
    {
        var actions = _nav.Handle(evt);
        foreach (var need in actions.OfType<NeedFavoritesPanel>())
            _nav.ShowFavorites(BuildPanel(need.Slot));
        return actions;
    }

    private FavoritesPanel BuildPanel(int target)
    {
        _panelRequests.Add(target);
        var slots = Enumerable.Range(1, FavoriteSlots.Count)
            .Select(s => new FavoriteSlotView(s, s == 1 ? "airhorn" : null, false, null))
            .ToImmutableArray();
        return new FavoritesPanel(slots, target, _lastPlayedName, "B");
    }

    private IReadOnlyList<NavigationAction> EnterAssignMode(int slot)
    {
        Handle(new ChordPressed());
        return Handle(new FavoritePressed(slot, true));
    }

    [Fact]
    public void FKey_PlaysThatFavorite_AndSpendsTheSession()
    {
        Handle(new ChordPressed());

        Assert.Equal([new PlayFavorite(3)], Handle(new FavoritePressed(3, false)));
        Assert.Null(_nav.ViewState);
        Assert.Empty(Handle(new DigitPressed(1, false)));
        Assert.Empty(Handle(new ChordReleased())); // not a bare-tap stop
    }

    [Fact]
    public void FKeyWithoutASession_IsIgnored() =>
        Assert.Empty(Handle(new FavoritePressed(3, false)));

    [Fact]
    public void NormalSessions_CarryNoFavorites()
    {
        Handle(new ChordPressed());

        Assert.Null(_nav.ViewState!.Favorites);
        Assert.Empty(_panelRequests);
    }

    [Fact]
    public void ShiftFKey_EntersAssignMode_ShowingTheStrip()
    {
        Assert.Equal([new NeedFavoritesPanel(3)], EnterAssignMode(3));

        var favorites = _nav.ViewState!.Favorites!;
        Assert.Equal(3, favorites.TargetSlot);
        Assert.Equal("airhorn", favorites.Slots[0].Name);
        Assert.Equal("Bruh", favorites.LastPlayedName);
        Assert.Equal("B", favorites.ChordKeyName);
        Assert.Equal("", _nav.ViewState.FolderPath);
        Assert.Equal([3], _panelRequests);
    }

    [Fact]
    public void AssignMode_PickingAFile_AssignsInsteadOfPlaying_AndSpendsTheSession()
    {
        EnterAssignMode(3);
        Handle(new DigitPressed(1, false)); // into Trump, as usual

        Assert.Equal("Trump", _nav.ViewState!.FolderPath);
        Assert.NotNull(_nav.ViewState.Favorites);
        Assert.Equal([new AssignFavorite(3, "Trump/wall.wav")], Handle(new DigitPressed(1, false)));
        Assert.Null(_nav.ViewState);
        Assert.Empty(Handle(new ChordReleased()));
    }

    [Fact]
    public void AssignMode_SameFKeyAgain_AssignsTheLastPlayed_AndStays()
    {
        EnterAssignMode(3);

        Assert.Equal([new AssignLastPlayedFavorite(3)], Handle(new FavoritePressed(3, false)));
        Assert.Equal([new AssignLastPlayedFavorite(3)], Handle(new FavoritePressed(3, true)));
        Assert.Equal(3, _nav.ViewState!.Favorites!.TargetSlot);
    }

    [Fact]
    public void AssignMode_AnotherFKey_Retargets_WithoutReloading()
    {
        EnterAssignMode(3);

        Assert.Empty(Handle(new FavoritePressed(7, false)));
        Assert.Equal(7, _nav.ViewState!.Favorites!.TargetSlot);
        Assert.Equal([new AssignFavorite(7, "boom.wav")], Handle(new DigitPressed(2, false)));
        Assert.Equal([3], _panelRequests);
    }

    [Fact]
    public void AssignMode_ClearKey_ClearsTheTarget_AndStays()
    {
        EnterAssignMode(3);

        Assert.Equal([new ClearFavorite(3)], Handle(new FavoriteClearPressed()));
        Assert.NotNull(_nav.ViewState);
        Handle(new FavoritePressed(5, false));
        Assert.Equal([new ClearFavorite(5)], Handle(new FavoriteClearPressed()));
    }

    [Fact]
    public void ClearKey_OutsideAssignMode_DoesNothing()
    {
        Handle(new ChordPressed());

        Assert.Empty(Handle(new FavoriteClearPressed()));
        Assert.Equal([new StopPlayback()], Handle(new ChordReleased())); // still a bare tap
    }

    [Fact]
    public void ReleasingTheChordInAssignMode_CancelsWithoutStopping()
    {
        EnterAssignMode(3);

        Assert.Empty(Handle(new ChordReleased()));
        Assert.Null(_nav.ViewState);

        Handle(new ChordPressed()); // the next session starts fresh
        Assert.Null(_nav.ViewState!.Favorites);
        Assert.Equal([new PlayFavorite(3)], Handle(new FavoritePressed(3, false)));
    }

    [Fact]
    public void AssigningSlot_FollowsAssignMode_AndShowFavoritesReplacesTheStrip()
    {
        Assert.Null(_nav.AssigningSlot);
        EnterAssignMode(3);
        Assert.Equal(3, _nav.AssigningSlot);
        var before = _nav.ViewState;
        _lastPlayedName = "Nope";

        _nav.ShowFavorites(BuildPanel(3));

        Assert.NotSame(before, _nav.ViewState);
        Assert.Equal("Nope", _nav.ViewState!.Favorites!.LastPlayedName);
        Handle(new DigitPressed(2, false)); // assigns boom.wav, spending the session
        Assert.Null(_nav.AssigningSlot);
    }

    [Fact]
    public void AssignMode_ShowsNoStripUntilOneIsSupplied()
    {
        _nav.Handle(new ChordPressed());

        Assert.Equal([new NeedFavoritesPanel(2)], _nav.Handle(new FavoritePressed(2, true)));
        Assert.Null(_nav.ViewState!.Favorites);

        _nav.ShowFavorites(BuildPanel(2));
        Assert.Equal(2, _nav.ViewState!.Favorites!.TargetSlot);
    }

    [Fact]
    public void RetargetingBeforeTheStripArrives_AsksAgain_AndIgnoresTheStaleStrip()
    {
        _nav.Handle(new ChordPressed());
        _nav.Handle(new FavoritePressed(2, true));

        Assert.Equal([new NeedFavoritesPanel(5)], _nav.Handle(new FavoritePressed(5, false)));
        _nav.ShowFavorites(BuildPanel(2));
        Assert.Null(_nav.ViewState!.Favorites);

        _nav.ShowFavorites(BuildPanel(5));
        Assert.Equal(5, _nav.ViewState!.Favorites!.TargetSlot);
    }

    [Fact]
    public void ShowFavorites_OutsideAssignMode_IsIgnored()
    {
        _nav.ShowFavorites(BuildPanel(3));
        Assert.Null(_nav.ViewState);

        Handle(new ChordPressed());
        _nav.ShowFavorites(BuildPanel(3));
        Assert.Null(_nav.ViewState!.Favorites);
    }

    [Fact]
    public void ChordlessFavorite_PlaysWithoutStartingASession()
    {
        Assert.Equal([new PlayFavorite(4)], Handle(new ChordlessFavoritePressed(4)));
        Assert.Null(_nav.ViewState);
    }

    [Fact]
    public void OutOfRangeSlots_AreIgnored()
    {
        Assert.Empty(Handle(new ChordlessFavoritePressed(13)));
        Handle(new ChordPressed());
        Assert.Empty(Handle(new FavoritePressed(0, false)));
    }

    [Fact]
    public void AssignMode_ShiftDigitIntoFolder_DoesNotPersist()
    {
        EnterAssignMode(3);

        Handle(new DigitPressed(1, true)); // shift+1, as if Shift from Shift+F3 is still held

        Assert.Equal("Trump", _nav.ViewState!.FolderPath);
        Assert.Equal("", _nav.PersistentPath);
    }

    [Fact]
    public void AssignMode_ShiftZero_DoesNotPersist()
    {
        _source.AddFolder("Trump", "Sub");
        EnterAssignMode(3);
        Handle(new DigitPressed(1, false)); // into Trump
        Handle(new DigitPressed(1, false)); // into Trump/Sub (folders sort before files)

        Handle(new DigitPressed(0, true)); // shift+0 back up to Trump

        Assert.Equal("Trump", _nav.ViewState!.FolderPath);
        Assert.Equal("", _nav.PersistentPath);
    }

    [Fact]
    public void AssignMode_ShiftDigit_PickingAFile_Assigns_AndDoesNotPersist()
    {
        EnterAssignMode(3);
        Handle(new DigitPressed(1, false)); // into Trump

        Assert.Equal([new AssignFavorite(3, "Trump/wall.wav")], Handle(new DigitPressed(1, true)));
        Assert.Equal("", _nav.PersistentPath);
    }

    [Fact]
    public void NormalSession_ShiftDigit_StillPersists()
    {
        Handle(new ChordPressed());

        Handle(new DigitPressed(1, true)); // shift+1 into Trump, outside assign mode

        Assert.Equal("Trump", _nav.PersistentPath);
    }
}
