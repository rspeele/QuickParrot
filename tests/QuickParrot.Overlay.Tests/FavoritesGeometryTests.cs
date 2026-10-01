using System.Drawing;
using QuickParrot.Core.Favorites;
using QuickParrot.Core.Navigation;

namespace QuickParrot.Overlay.Tests;

public sealed class FavoritesGeometryTests
{
    private static FavoritesPanel Panel(int target = 3, string? lastPlayed = "Bruh", string chord = "B") => new(
        Enumerable.Range(1, FavoriteSlots.Count).Select(s => s switch
        {
            1 => new FavoriteSlotView(1, "Airhorn", false, null),
            2 => new FavoriteSlotView(2, "Gone", true, null),
            4 => new FavoriteSlotView(4, "Wilhelm", false, "chord key"),
            _ => new FavoriteSlotView(s, null, false, null),
        }).ToList(),
        target, lastPlayed, chord);

    private static OverlayLayout Compute(OverlayViewState state, float scale = 1, SmallFolderLayout small = SmallFolderLayout.List) =>
        OverlayLayoutGeometry.Compute(state with { SmallFolderLayout = small }, scale);

    // Folder parts are drawn relative to the folder origin.
    private static RectangleF OnCanvas(RectangleF r, OverlayLayout layout) =>
        r with { X = r.X + layout.FolderOrigin.X, Y = r.Y + layout.FolderOrigin.Y };

    [Fact]
    public void WithoutFavorites_TheLayoutIsUnchanged()
    {
        var layout = Compute(ViewStates.Wheel(5));

        Assert.Null(layout.Favorites);
    }

    [Theory]
    [InlineData(OverlayLayoutKind.Wheel, SmallFolderLayout.List)]
    [InlineData(OverlayLayoutKind.Wheel, SmallFolderLayout.Ring)]
    [InlineData(OverlayLayoutKind.Grid, SmallFolderLayout.List)]
    public void StripSitsAboveTheFolderView_EverythingInsideTheCanvas(OverlayLayoutKind kind, SmallFolderLayout small)
    {
        var folder = kind == OverlayLayoutKind.Wheel ? ViewStates.Wheel(9, "Folder") : ViewStates.Grid(81, "Folder", 4);
        var layout = Compute(folder with { Favorites = Panel() }, 1.5f, small);
        var canvas = new RectangleF(PointF.Empty, layout.CanvasSize);
        var strip = layout.Favorites!;

        Assert.True(canvas.Contains(strip.Panel.Bounds));
        Assert.All(layout.Panels, p => Assert.True(canvas.Contains(OnCanvas(p.Bounds, layout))));
        Assert.All(layout.Items, i => Assert.True(canvas.Contains(OnCanvas(i.Bounds, layout))));
        Assert.True(layout.Panels.Min(p => OnCanvas(p.Bounds, layout).Top) > strip.Panel.Bounds.Bottom);
        Assert.True(OnCanvas(layout.Title.Bounds, layout).Top > strip.Panel.Bounds.Bottom);
    }

    [Fact]
    public void FolderView_IsTheSameLayout_JustMovedDown()
    {
        var plain = Compute(ViewStates.Wheel(4, "Folder"));
        var assigning = Compute(ViewStates.Wheel(4, "Folder") with { Favorites = Panel() });

        Assert.Equal(PointF.Empty, plain.FolderOrigin);
        Assert.True(assigning.FolderOrigin.Y > 0);
        Assert.Equal(plain.Items, assigning.Items);
        Assert.Equal(plain.Panels, assigning.Panels);
        Assert.Equal((plain.Title, plain.Subtitle, plain.Hint), (assigning.Title, assigning.Subtitle, assigning.Hint));
    }

    [Fact]
    public void FolderView_IsCenteredUnderTheStrip()
    {
        var layout = Compute(ViewStates.Wheel(4) with { Favorites = Panel() });
        var stripCenter = layout.Favorites!.Panel.Bounds.Left + layout.Favorites.Panel.Bounds.Width / 2;
        var item = OnCanvas(layout.Items[0].Bounds, layout);
        var itemCenter = item.Left + item.Width / 2;

        Assert.Equal(stripCenter, itemCenter, 1f);
    }

    [Fact]
    public void TwelveSlots_InARow_LeftToRight_InsideTheStrip()
    {
        var strip = Compute(ViewStates.Wheel(2) with { Favorites = Panel() }, 2).Favorites!;

        Assert.Equal(Enumerable.Range(1, 12).Select(n => $"F{n}"), strip.Slots.Select(s => s.KeyLabel));
        Assert.All(strip.Slots, s => Assert.True(strip.Panel.Bounds.Contains(s.Bounds)));
        Assert.All(strip.Slots, s => Assert.Equal(strip.Slots[0].Bounds.Y, s.Bounds.Y));
        for (var i = 1; i < strip.Slots.Count; i++)
            Assert.True(strip.Slots[i].Bounds.Left > strip.Slots[i - 1].Bounds.Right);

        Assert.True(strip.Instructions.Bounds.Bottom <= strip.Slots[0].Bounds.Top);
    }

    [Fact]
    public void Slots_ReflectTheirState()
    {
        var slots = Compute(ViewStates.Wheel(2) with { Favorites = Panel(target: 3) }).Favorites!.Slots;

        Assert.Equal(("F1", "Airhorn", FavoriteSlotState.Assigned, (string?)null), (slots[0].KeyLabel, slots[0].Name, slots[0].State, slots[0].Tag));
        Assert.Equal((FavoriteSlotState.Missing, "missing"), (slots[1].State, slots[1].Tag));
        Assert.Equal((FavoriteSlotState.Empty, "empty"), (slots[2].State, slots[2].Name));
        Assert.Equal((FavoriteSlotState.Unavailable, "(chord key)"), (slots[3].State, slots[3].Name));
        Assert.Equal(["F3"], slots.Where(s => s.Target).Select(s => s.KeyLabel));
        Assert.True(slots[1].TagBounds.Left >= slots[1].KeyBounds.Right);
        Assert.True(slots[0].TagBounds.IsEmpty);
        Assert.All(slots, s => Assert.True(s.Bounds.Contains(s.NameBounds) && s.Bounds.Contains(s.KeyBounds)));
    }

    [Fact]
    public void Banner_NamesTheTarget_LastPlayed_AndChordKey()
    {
        var strip = Compute(ViewStates.Wheel(2) with { Favorites = Panel(3, "Bruh", "Numpad -") }).Favorites!;

        Assert.Equal("Assigning F3", strip.Title.Text);
        Assert.Equal("Pick a clip · F3: last played (Bruh) · Del: clear · Release Numpad - to cancel", strip.Instructions.Text);
    }

    [Fact]
    public void Banner_OmitsLastPlayed_WhenNothingHasPlayed() =>
        Assert.Equal(
            "Pick a clip · Del: clear · Release B to cancel",
            FavoritesGeometry.InstructionsFor(Panel(lastPlayed: null)));

    [Fact]
    public void Banner_TruncatesALongLastPlayedName()
    {
        var text = FavoritesGeometry.InstructionsFor(Panel(lastPlayed: new string('x', 80)));

        Assert.Contains($"({new string('x', FavoritesGeometry.MaxLastPlayedChars - 1)}…)", text);
    }

    [Fact]
    public void Scale_FitsWideStripOnSmallMonitors()
    {
        var state = ViewStates.Wheel(9) with { Favorites = Panel() };
        var monitor = new Size(1280, 720);

        var layout = OverlayLayoutGeometry.ComputeForMonitor(state, monitor, 96);

        Assert.True(layout.CanvasSize.Width <= monitor.Width && layout.CanvasSize.Height <= monitor.Height);
    }
}
