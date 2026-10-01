using Microsoft.Extensions.Time.Testing;
using QuickParrot.Core.Engine;
using QuickParrot.Core.Favorites;
using QuickParrot.Core.Keyboard;
using QuickParrot.Core.Navigation;
using QuickParrot.Core.Playback;
using QuickParrot.Core.Settings;
using QuickParrot.Core.Tests.Fakes;

namespace QuickParrot.Core.Tests;

public sealed class QuickParrotEngineFavoritesTests : IDisposable
{
    private readonly List<string> _log = [];
    private readonly FakeTimeProvider _time = new();
    private readonly Dictionary<string, FakeFolderSource> _libraries = new() { ["root"] = new FakeFolderSource() };
    private readonly FakeSettingsStore _store = new();
    private readonly List<FavoriteNotice> _notices = [];
    private readonly QuickParrotEngine _engine;

    public QuickParrotEngineFavoritesTests()
    {
        Library.AddFolder("", "Movies");
        Library.AddFile("Movies", "Wilhelm scream.wav");
        Library.AddFile("", "boom.wav");
        _engine = CreateEngine(new AppSettings
        {
            LibraryRoot = "root",
            Favorites = FavoriteSlots.Empty.With(1, "boom.wav").With(2, "gone.wav"),
        });
    }

    private FakeFolderSource Library => _libraries["root"];

    private QuickParrotEngine CreateEngine(AppSettings settings)
    {
        var engine = new QuickParrotEngine(
            new FakeClipPlayer(_log), new FakePushToTalk(_log), new FakeMicMuter(_log), _store, settings, _time,
            root => _libraries.TryGetValue(root, out var library) ? library : new FakeFolderSource());
        engine.FavoritesNotice += n => { lock (_notices) _notices.Add(n); };
        engine.Start();
        return engine;
    }

    [Fact]
    public async Task ChordFKey_PlaysTheFavorite()
    {
        _engine.Post(new ChordPressed());
        _engine.Post(new FavoritePressed(1, false));
        await SettleAsync();

        Assert.Contains("prepare:fake:/boom.wav", _log);
        Assert.Empty(_notices);
    }

    [Fact]
    public async Task ChordlessFKey_PlaysTheFavorite()
    {
        _engine.Post(new ChordlessFavoritePressed(1));
        await SettleAsync();

        Assert.Contains("prepare:fake:/boom.wav", _log);
    }

    [Fact]
    public async Task PlayingAnEmptySlot_SaysSo()
    {
        _engine.PlayFavorite(3);
        await SettleAsync();

        Assert.Equal([FavoriteNotice.SlotEmpty(3)], _notices);
        Assert.Empty(_log);
    }

    [Fact]
    public async Task PlayingAMissingClip_SaysSo()
    {
        _engine.PlayFavorite(2);
        await SettleAsync();

        Assert.Equal([FavoriteNotice.SlotMissing(2)], _notices);
        Assert.Empty(_log);
    }

    [Fact]
    public async Task AssignFromTheOverlay_SavesAndConfirms()
    {
        _engine.Post(new ChordPressed());
        _engine.Post(new FavoritePressed(3, true));
        _engine.Post(new DigitPressed(1, false)); // Movies
        _engine.Post(new DigitPressed(1, false)); // Wilhelm scream
        await _engine.FlushAsync();

        Assert.Equal("Movies/Wilhelm scream.wav", _engine.Settings.Favorites[3]);
        Assert.Equal([new FavoriteNotice("F3 → Wilhelm scream", false)], _notices);
        Assert.Empty(_log); // assigned, not played
        Assert.Null(_engine.ViewState);

        _time.Advance(QuickParrotEngine.SaveDelay);
        await _engine.FlushAsync();
        Assert.Equal("Movies/Wilhelm scream.wav", Assert.Single(_store.Saved).Favorites[3]);
    }

    [Fact]
    public async Task AssignModeStrip_ShowsNamesMissingFlagsAndLastPlayed()
    {
        _engine.Play("Movies/Wilhelm scream.wav");
        _engine.Post(new ChordPressed());
        _engine.Post(new FavoritePressed(4, true));
        await _engine.FlushAsync();

        var favorites = _engine.ViewState!.Favorites!;
        Assert.Equal(new FavoriteSlotView(1, "boom", false, null), favorites.Slots[0]);
        Assert.Equal(new FavoriteSlotView(2, "gone", true, null), favorites.Slots[1]);
        Assert.Equal("Wilhelm scream", favorites.LastPlayedName);
        Assert.Equal("B", favorites.ChordKeyName);
    }

    [Fact]
    public async Task SameFKeyAgain_AssignsTheLastPlayedClip_AndUpdatesTheStrip()
    {
        _engine.Play("Movies/Wilhelm scream.wav");
        _engine.Post(new ChordPressed());
        _engine.Post(new FavoritePressed(4, true));
        _engine.Post(new FavoritePressed(4, false));
        await _engine.FlushAsync();

        Assert.Equal("Movies/Wilhelm scream.wav", _engine.Settings.Favorites[4]);
        Assert.Equal("Wilhelm scream", _engine.ViewState!.Favorites!.Slots[3].Name);
        Assert.Contains(new FavoriteNotice("F4 → Wilhelm scream", false), _notices);
    }

    [Fact]
    public async Task LastPlayed_IncludesFavoritesAndChordPicks()
    {
        _engine.PlayFavorite(1);
        _engine.Post(new ChordPressed());
        _engine.Post(new FavoritePressed(5, true));
        _engine.Post(new FavoritePressed(5, false));
        await _engine.FlushAsync();

        Assert.Equal("boom.wav", _engine.Settings.Favorites[5]);
    }

    [Fact]
    public async Task LastPlayed_WithNothingPlayed_IsAnError()
    {
        _engine.Post(new ChordPressed());
        _engine.Post(new FavoritePressed(4, true));
        _engine.Post(new FavoritePressed(4, false));
        await _engine.FlushAsync();

        Assert.Equal([FavoriteNotice.NothingPlayed], _notices);
        Assert.Null(_engine.Settings.Favorites[4]);
    }

    [Fact]
    public async Task LastPlayed_DeletedSince_IsAnError()
    {
        _engine.Play("boom.wav");
        await _engine.FlushAsync();
        Library.RemoveFile("", "boom.wav");

        _engine.Post(new ChordPressed());
        _engine.Post(new FavoritePressed(4, true));
        _engine.Post(new FavoritePressed(4, false));
        await _engine.FlushAsync();

        Assert.Contains(FavoriteNotice.LastPlayedMissing, _notices);
        Assert.Null(_engine.Settings.Favorites[4]);
    }

    [Fact]
    public async Task ClearKey_ClearsTheSlot_AndStaysInAssignMode()
    {
        _engine.Post(new ChordPressed());
        _engine.Post(new FavoritePressed(1, true));
        _engine.Post(new FavoriteClearPressed());
        await _engine.FlushAsync();

        Assert.Null(_engine.Settings.Favorites[1]);
        Assert.Equal([FavoriteNotice.Cleared(1)], _notices);
        Assert.True(_engine.ViewState!.Favorites!.Slots[0].IsEmpty);
    }

    [Fact]
    public async Task ClearingAnEmptySlot_ChangesNothing()
    {
        var changes = 0;
        _engine.SettingsChanged += _ => changes++;

        _engine.ClearFavorite(7);
        await _engine.FlushAsync();

        Assert.Equal([FavoriteNotice.AlreadyEmpty(7)], _notices);
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task ReleasingInAssignMode_ChangesNothing_AndDoesntStop()
    {
        _engine.Post(new ChordPressed());
        _engine.Post(new FavoritePressed(1, true));
        _engine.Post(new ChordReleased());
        await _engine.FlushAsync();

        Assert.Equal("boom.wav", _engine.Settings.Favorites[1]);
        Assert.Empty(_notices);
        Assert.DoesNotContain("stop", _log);
    }

    [Theory]
    [InlineData("Movies")]
    [InlineData("nope.wav")]
    [InlineData("../outside.wav")]
    public async Task AssigningSomethingOtherThanALibraryClip_IsRefused(string path)
    {
        _engine.AssignFavorite(3, path);
        await _engine.FlushAsync();

        Assert.Equal([FavoriteNotice.NotAClip], _notices);
        Assert.Null(_engine.Settings.Favorites[3]);
    }

    [Fact]
    public async Task AssignFromTheApp_NormalizesThePath()
    {
        _engine.AssignFavorite(12, @"Movies\Wilhelm scream.wav");
        await _engine.FlushAsync();

        Assert.Equal("Movies/Wilhelm scream.wav", _engine.Settings.Favorites[12]);
    }

    [Fact]
    public async Task AfterTheLibraryRootChanges_OldFavoritesAreMissing_AndLastPlayedIsForgotten()
    {
        _libraries["other"] = new FakeFolderSource();
        _engine.Play("boom.wav");
        _engine.UpdateSettings(s => s with { LibraryRoot = "other" });
        _engine.PlayFavorite(1);
        _engine.Post(new ChordPressed());
        _engine.Post(new FavoritePressed(4, true));
        await SettleAsync();

        Assert.Contains(FavoriteNotice.SlotMissing(1), _notices);
        var favorites = _engine.ViewState!.Favorites!;
        Assert.True(favorites.Slots[0].Missing);
        Assert.Null(favorites.LastPlayedName);
        Assert.Equal("boom.wav", _engine.Settings.Favorites[1]); // kept, in case the old library comes back
    }

    [Fact]
    public async Task FKeyChordAndPushToTalk_ShowAsUnavailable()
    {
        _engine.UpdateSettings(s => s with
        {
            ChordKey = ScanKey.ForFunctionKey(9),
            PushToTalkBinding = PushToTalkBinding.FromKey(ScanKey.ForFunctionKey(10)),
        });
        _engine.Post(new ChordPressed());
        _engine.Post(new FavoritePressed(1, true));
        await _engine.FlushAsync();

        var favorites = _engine.ViewState!.Favorites!;
        Assert.Equal("chord key", favorites.Slots[8].UnavailableReason);
        Assert.Equal("PTT key", favorites.Slots[9].UnavailableReason);
        Assert.Equal("F9", favorites.ChordKeyName);
    }

    [Fact]
    public async Task WithoutALibrary_FavoritesSayToChooseOne()
    {
        _engine.UpdateSettings(s => s with { LibraryRoot = null });
        _engine.Post(new ChordlessFavoritePressed(1));
        _engine.PlayFavorite(1);
        await _engine.FlushAsync();

        Assert.Equal([FavoriteNotice.NoLibrary, FavoriteNotice.NoLibrary], _notices);
    }

    public void Dispose() => _engine.Dispose();

    private async Task SettleAsync()
    {
        await _engine.FlushAsync();
        await _engine.FlushAsync();
    }
}
