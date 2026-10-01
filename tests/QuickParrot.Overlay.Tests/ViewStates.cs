using System.Collections.Immutable;
using QuickParrot.Core.Favorites;
using QuickParrot.Core.Navigation;

namespace QuickParrot.Overlay.Tests;

internal static class ViewStates
{
    public static OverlayViewState Wheel(int count, string folder = "")
    {
        var entries = Enumerable.Range(1, count)
            .Select(n => new NumberedEntry(n, $"Entry {n}", n % 2 == 1))
            .ToImmutableArray();
        return new OverlayViewState(folder, OverlayLayoutKind.Wheel, entries, [], null, false);
    }

    /// <summary>F1 assigned, F2 missing, F3 unavailable, the rest empty.</summary>
    public static FavoritesPanel Favorites(int target) => new(
        Enumerable.Range(1, 12)
            .Select(s => new FavoriteSlotView(s, s <= 2 ? $"Clip {s}" : null, s == 2, s == 3 ? "chord key" : null))
            .ToImmutableArray(),
        target, "Clip 1", "B");

    public static OverlayViewState Grid(int count, string folder = "", int? zoomedColumn = null)
    {
        var columns = Enumerable.Range(0, Math.Min(count, 81))
            .Chunk(9)
            .Select((chunk, c) => new GridColumn(
                c + 1,
                chunk.Select((i, row) => new NumberedEntry(row + 1, $"Entry {i + 1}", i % 3 == 0)).ToImmutableArray()))
            .ToImmutableArray();
        return new OverlayViewState(folder, OverlayLayoutKind.Grid, [], columns, zoomedColumn, count > 81);
    }
}
