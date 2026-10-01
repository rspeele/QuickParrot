using System.Collections.Immutable;
using System.Runtime.InteropServices;
using QuickParrot.Core.Library;

namespace QuickParrot.Core.Navigation;

// Groups raw folder entries into the Wheel/Grid shape; shared by ChordNavigator's selection
// logic and its view-state building so they can never disagree.
internal sealed record NavigationLayout(
    OverlayLayoutKind Kind,
    ImmutableArray<FolderEntry> WheelEntries,
    ImmutableArray<ImmutableArray<FolderEntry>> GridColumns,
    bool Truncated);

internal static class LayoutBuilder
{
    private const int WheelMaxEntries = 9;
    private const int ColumnSize = 9;

    public static NavigationLayout Build(IReadOnlyList<FolderEntry> entries)
    {
        if (entries.Count <= WheelMaxEntries)
            return new NavigationLayout(OverlayLayoutKind.Wheel, [.. entries], [], false);

        var columns = entries
            .Take(OverlayCapacity.MaxEntries)
            .Chunk(ColumnSize)
            .Select(ImmutableCollectionsMarshal.AsImmutableArray)
            .ToImmutableArray();
        return new NavigationLayout(OverlayLayoutKind.Grid, [], columns, entries.Count > OverlayCapacity.MaxEntries);
    }
}
