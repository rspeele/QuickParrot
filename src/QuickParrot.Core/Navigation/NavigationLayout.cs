using QuickParrot.Core.Library;

namespace QuickParrot.Core.Navigation;

// Groups raw folder entries into the Wheel/Grid shape; shared by ChordNavigator's selection
// logic and its view-state building so they can never disagree.
internal sealed record NavigationLayout(
    OverlayLayoutKind Kind,
    IReadOnlyList<FolderEntry> WheelEntries,
    IReadOnlyList<IReadOnlyList<FolderEntry>> GridColumns,
    bool Truncated);

internal static class LayoutBuilder
{
    private const int WheelMaxEntries = 9;
    private const int ColumnSize = 9;
    private const int GridMaxEntries = 81;

    public static NavigationLayout Build(IReadOnlyList<FolderEntry> entries)
    {
        if (entries.Count <= WheelMaxEntries)
            return new NavigationLayout(OverlayLayoutKind.Wheel, entries, [], false);

        var truncated = entries.Count > GridMaxEntries;
        var limited = truncated ? entries.Take(GridMaxEntries).ToList() : entries;

        var columns = new List<IReadOnlyList<FolderEntry>>();
        for (var i = 0; i < limited.Count; i += ColumnSize)
            columns.Add(limited.Skip(i).Take(ColumnSize).ToList());

        return new NavigationLayout(OverlayLayoutKind.Grid, [], columns, truncated);
    }
}
