using QuickParrot.Core.Navigation;

namespace QuickParrot.Overlay.Tests;

internal static class ViewStates
{
    public static OverlayViewState Wheel(int count, string folder = "")
    {
        var entries = Enumerable.Range(1, count)
            .Select(n => new NumberedEntry(n, $"Entry {n}", n % 2 == 1, $"{folder}/Entry {n}"))
            .ToList();
        return new OverlayViewState(folder, OverlayLayoutKind.Wheel, entries, [], null, false);
    }

    public static OverlayViewState Grid(int count, string folder = "", int? zoomedColumn = null)
    {
        var columns = Enumerable.Range(0, Math.Min(count, 81))
            .Chunk(9)
            .Select((chunk, c) => new GridColumn(
                c + 1,
                chunk.Select((i, row) => new NumberedEntry(row + 1, $"Entry {i + 1}", i % 3 == 0, $"Entry {i + 1}")).ToList()))
            .ToList();
        return new OverlayViewState(folder, OverlayLayoutKind.Grid, [], columns, zoomedColumn, count > 81);
    }
}
