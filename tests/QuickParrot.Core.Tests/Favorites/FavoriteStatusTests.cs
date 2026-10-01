using QuickParrot.Core.Favorites;
using QuickParrot.Core.Keyboard;

namespace QuickParrot.Core.Tests.Favorites;

public class FavoriteStatusTests
{
    private static readonly ScanKey F3 = ScanKey.ForFunctionKey(3);
    private static readonly ScanKey F5 = ScanKey.ForFunctionKey(5);

    [Fact]
    public void Describe_NamesClips_AndFlagsMissingOnes()
    {
        var favorites = FavoriteSlots.Empty.With(1, "Trump/wall.wav").With(2, "gone.wav");

        var views = FavoriteStatus.Describe(favorites, path => path == "Trump/wall.wav", ScanKey.DefaultChordKey, PushToTalkBinding.Default);

        Assert.Equal(12, views.Length);
        Assert.Equal(new FavoriteSlotView(1, "wall", false, null), views[0]);
        Assert.Equal(new FavoriteSlotView(2, "gone", true, null), views[1]);
        Assert.Equal(new FavoriteSlotView(3, null, false, null), views[2]);
        Assert.Equal("F12", views[11].KeyName);
    }

    [Fact]
    public void Describe_WithoutALibrary_MarksEveryClipMissing()
    {
        var views = FavoriteStatus.Describe(
            FavoriteSlots.Empty.With(4, "a.wav"), null, ScanKey.DefaultChordKey, PushToTalkBinding.Default);

        Assert.True(views[3].Missing);
        Assert.False(views[0].Missing); // empty slots are never "missing"
    }

    [Fact]
    public void FKeysUsedAsChordOrPushToTalk_AreUnavailable()
    {
        var views = FavoriteStatus.Describe(FavoriteSlots.Empty, _ => true, F3, PushToTalkBinding.FromKey(F5));

        Assert.Equal("chord key", views[2].UnavailableReason);
        Assert.Equal("PTT key", views[4].UnavailableReason);
        Assert.All(views.Where(v => v.Slot is not (3 or 5)), v => Assert.Null(v.UnavailableReason));
    }

    [Fact]
    public void MousePushToTalk_LeavesEveryFKeyAvailable() =>
        Assert.Null(FavoriteStatus.UnavailableReason(5, ScanKey.DefaultChordKey, PushToTalkBinding.FromMouse(PushToTalkMouseButton.X1)));

    [Theory]
    [InlineData(null, false, null, "(empty)")]
    [InlineData("Bruh", false, null, "Bruh")]
    [InlineData("Bruh", true, null, "Bruh (missing)")]
    [InlineData(null, false, "chord key", "(empty) (F3 is the chord key)")]
    public void DisplayText(string? name, bool missing, string? unavailable, string expected) =>
        Assert.Equal(expected, new FavoriteSlotView(3, name, missing, unavailable).DisplayText);

    [Fact]
    public void ChordlessMask_IsTheAssignedSlots_OnlyWhenEnabled()
    {
        var favorites = FavoriteSlots.Empty.With(2, "a.wav").With(5, "b.wav");

        Assert.Equal(0b10010, FavoriteStatus.ChordlessMask(favorites, favoritesWithoutChord: true));
        Assert.Equal(0, FavoriteStatus.ChordlessMask(favorites, favoritesWithoutChord: false));
    }

    [Fact]
    public void Notices_ReadNaturally()
    {
        Assert.Equal(new FavoriteNotice("F3 → Wilhelm scream", false), FavoriteNotice.Assigned(3, "Movies/Wilhelm scream.wav"));
        Assert.Equal(new FavoriteNotice("F3 cleared", false), FavoriteNotice.Cleared(3));
        Assert.Equal(new FavoriteNotice("F3 is empty", true), FavoriteNotice.SlotEmpty(3));
        Assert.Equal(new FavoriteNotice("F3's clip was moved or deleted", true), FavoriteNotice.SlotMissing(3));
    }
}

public class FullscreenRuleTests
{
    private static readonly PixelRect Monitor = new(0, 0, 1920, 1080);
    private static readonly PixelRect SecondMonitor = new(1920, 0, 4480, 1440);

    [Fact]
    public void ExactlyCoveringTheMonitor_IsAGame() =>
        Assert.True(FullscreenRule.IsFullscreenGame(Monitor, Monitor, "UnrealWindow", ownWindow: false, hasTitleBar: false));

    [Fact]
    public void OverhangingTheMonitor_IsAGame() =>
        Assert.True(FullscreenRule.IsFullscreenGame(new PixelRect(-8, -8, 1928, 1088), Monitor, "SDL_app", false, false));

    // A maximized browser overhangs a monitor with no (or an auto-hidden) taskbar, but F5 must stay its own.
    [Fact]
    public void AWindowWithATitleBar_IsNotAGame_EvenCoveringTheMonitor() =>
        Assert.False(FullscreenRule.IsFullscreenGame(new PixelRect(-8, -8, 1928, 1088), Monitor, "Chrome_WidgetWin_1", false, hasTitleBar: true));

    [Fact]
    public void OnASecondMonitor_ComparesAgainstThatMonitor()
    {
        Assert.True(FullscreenRule.IsFullscreenGame(SecondMonitor, SecondMonitor, "SDL_app", false, false));
        Assert.False(FullscreenRule.IsFullscreenGame(Monitor, SecondMonitor, "SDL_app", false, false));
    }

    [Theory]
    [InlineData(0, 0, 1920, 1040)] // a maximized window above the taskbar
    [InlineData(100, 100, 1200, 800)]
    [InlineData(0, 0, 0, 0)]
    public void SmallerWindows_AreNotGames(int left, int top, int right, int bottom) =>
        Assert.False(FullscreenRule.IsFullscreenGame(new PixelRect(left, top, right, bottom), Monitor, "Notepad", false, false));

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("Shell_TrayWnd")]
    public void TheDesktopAndShell_AreNotGames(string className) =>
        Assert.False(FullscreenRule.IsFullscreenGame(Monitor, Monitor, className, false, false));

    [Fact]
    public void QuickParrotsOwnWindows_AreNotGames() =>
        Assert.False(FullscreenRule.IsFullscreenGame(Monitor, Monitor, "HwndWrapper", ownWindow: true, hasTitleBar: false));

    [Fact]
    public void AnEmptyMonitor_CoversNothing() =>
        Assert.False(FullscreenRule.CoversMonitor(Monitor, default));
}
