using QuickParrot.Core.Favorites;
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
        _nav = new ChordNavigator(_source, "", target =>
        {
            _panelRequests.Add(target);
            var slots = Enumerable.Range(1, FavoriteSlots.Count)
                .Select(s => new FavoriteSlotView(s, s == 1 ? "airhorn" : null, false, null))
                .ToList();
            return new FavoritesPanel(slots, target, _lastPlayedName, "B");
        });
    }

    private IReadOnlyList<NavigationAction> EnterAssignMode(int slot)
    {
        _nav.Handle(new ChordPressed());
        return _nav.Handle(new FavoritePressed(slot, true));
    }

    [Fact]
    public void FKey_PlaysThatFavorite_AndSpendsTheSession()
    {
        _nav.Handle(new ChordPressed());

        Assert.Equal([new PlayFavorite(3)], _nav.Handle(new FavoritePressed(3, false)));
        Assert.Null(_nav.ViewState);
        Assert.Empty(_nav.Handle(new DigitPressed(1, false)));
        Assert.Empty(_nav.Handle(new ChordReleased())); // not a bare-tap stop
    }

    [Fact]
    public void FKeyWithoutASession_IsIgnored() =>
        Assert.Empty(_nav.Handle(new FavoritePressed(3, false)));

    [Fact]
    public void NormalSessions_CarryNoFavorites()
    {
        _nav.Handle(new ChordPressed());

        Assert.Null(_nav.ViewState!.Favorites);
        Assert.Empty(_panelRequests);
    }

    [Fact]
    public void ShiftFKey_EntersAssignMode_ShowingTheStrip()
    {
        Assert.Empty(EnterAssignMode(3));

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
        _nav.Handle(new DigitPressed(1, false)); // into Trump, as usual

        Assert.Equal("Trump", _nav.ViewState!.FolderPath);
        Assert.NotNull(_nav.ViewState.Favorites);
        Assert.Equal([new AssignFavorite(3, "Trump/wall.wav")], _nav.Handle(new DigitPressed(1, false)));
        Assert.Null(_nav.ViewState);
        Assert.Empty(_nav.Handle(new ChordReleased()));
    }

    [Fact]
    public void AssignMode_SameFKeyAgain_AssignsTheLastPlayed_AndStays()
    {
        EnterAssignMode(3);

        Assert.Equal([new AssignLastPlayedFavorite(3)], _nav.Handle(new FavoritePressed(3, false)));
        Assert.Equal([new AssignLastPlayedFavorite(3)], _nav.Handle(new FavoritePressed(3, true)));
        Assert.Equal(3, _nav.ViewState!.Favorites!.TargetSlot);
    }

    [Fact]
    public void AssignMode_AnotherFKey_Retargets_WithoutReloading()
    {
        EnterAssignMode(3);

        Assert.Empty(_nav.Handle(new FavoritePressed(7, false)));
        Assert.Equal(7, _nav.ViewState!.Favorites!.TargetSlot);
        Assert.Equal([new AssignFavorite(7, "boom.wav")], _nav.Handle(new DigitPressed(2, false)));
        Assert.Equal([3], _panelRequests);
    }

    [Fact]
    public void AssignMode_ClearKey_ClearsTheTarget_AndStays()
    {
        EnterAssignMode(3);

        Assert.Equal([new ClearFavorite(3)], _nav.Handle(new FavoriteClearPressed()));
        Assert.NotNull(_nav.ViewState);
        _nav.Handle(new FavoritePressed(5, false));
        Assert.Equal([new ClearFavorite(5)], _nav.Handle(new FavoriteClearPressed()));
    }

    [Fact]
    public void ClearKey_OutsideAssignMode_DoesNothing()
    {
        _nav.Handle(new ChordPressed());

        Assert.Empty(_nav.Handle(new FavoriteClearPressed()));
        Assert.Equal([new StopPlayback()], _nav.Handle(new ChordReleased())); // still a bare tap
    }

    [Fact]
    public void ReleasingTheChordInAssignMode_CancelsWithoutStopping()
    {
        EnterAssignMode(3);

        Assert.Empty(_nav.Handle(new ChordReleased()));
        Assert.Null(_nav.ViewState);

        _nav.Handle(new ChordPressed()); // the next session starts fresh
        Assert.Null(_nav.ViewState!.Favorites);
        Assert.Equal([new PlayFavorite(3)], _nav.Handle(new FavoritePressed(3, false)));
    }

    [Fact]
    public void RefreshFavorites_ReloadsTheStripWhileAssigning()
    {
        EnterAssignMode(3);
        var before = _nav.ViewState;
        _lastPlayedName = "Nope";

        _nav.RefreshFavorites();

        Assert.NotSame(before, _nav.ViewState);
        Assert.Equal("Nope", _nav.ViewState!.Favorites!.LastPlayedName);
        Assert.Equal([3, 3], _panelRequests);
    }

    [Fact]
    public void RefreshFavorites_OutsideAssignMode_DoesNothing()
    {
        _nav.RefreshFavorites();
        _nav.Handle(new ChordPressed());
        _nav.RefreshFavorites();

        Assert.Empty(_panelRequests);
        Assert.Null(_nav.ViewState!.Favorites);
    }

    [Fact]
    public void ChordlessFavorite_PlaysWithoutStartingASession()
    {
        Assert.Equal([new PlayFavorite(4)], _nav.Handle(new ChordlessFavoritePressed(4)));
        Assert.Null(_nav.ViewState);
    }

    [Fact]
    public void OutOfRangeSlots_AreIgnored()
    {
        Assert.Empty(_nav.Handle(new ChordlessFavoritePressed(13)));
        _nav.Handle(new ChordPressed());
        Assert.Empty(_nav.Handle(new FavoritePressed(0, false)));
    }

    [Fact]
    public void AssignMode_ShiftDigitIntoFolder_DoesNotPersist()
    {
        EnterAssignMode(3);

        _nav.Handle(new DigitPressed(1, true)); // shift+1, as if Shift from Shift+F3 is still held

        Assert.Equal("Trump", _nav.ViewState!.FolderPath);
        Assert.Equal("", _nav.PersistentPath);
    }

    [Fact]
    public void AssignMode_ShiftZero_DoesNotPersist()
    {
        _source.AddFolder("Trump", "Sub");
        EnterAssignMode(3);
        _nav.Handle(new DigitPressed(1, false)); // into Trump
        _nav.Handle(new DigitPressed(1, false)); // into Trump/Sub (folders sort before files)

        _nav.Handle(new DigitPressed(0, true)); // shift+0 back up to Trump

        Assert.Equal("Trump", _nav.ViewState!.FolderPath);
        Assert.Equal("", _nav.PersistentPath);
    }

    [Fact]
    public void AssignMode_ShiftDigit_PickingAFile_Assigns_AndDoesNotPersist()
    {
        EnterAssignMode(3);
        _nav.Handle(new DigitPressed(1, false)); // into Trump

        Assert.Equal([new AssignFavorite(3, "Trump/wall.wav")], _nav.Handle(new DigitPressed(1, true)));
        Assert.Equal("", _nav.PersistentPath);
    }

    [Fact]
    public void NormalSession_ShiftDigit_StillPersists()
    {
        _nav.Handle(new ChordPressed());

        _nav.Handle(new DigitPressed(1, true)); // shift+1 into Trump, outside assign mode

        Assert.Equal("Trump", _nav.PersistentPath);
    }

    [Fact]
    public void WithoutAProvider_AssignModeShowsEmptySlots()
    {
        var nav = new ChordNavigator(_source);
        nav.Handle(new ChordPressed());
        nav.Handle(new FavoritePressed(2, true));

        var favorites = nav.ViewState!.Favorites!;
        Assert.Equal(12, favorites.Slots.Count);
        Assert.All(favorites.Slots, s => Assert.True(s.IsEmpty));
    }
}
