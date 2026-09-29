namespace QuickParrot.Core.Navigation;

public enum OverlayLayoutKind
{
    Wheel,
    Grid,
}

public sealed record NumberedEntry(int Number, string Name, bool IsFolder, string RelativePath);

public sealed record GridColumn(int Number, IReadOnlyList<NumberedEntry> Entries);

/// <summary>
/// Everything a renderer needs to draw the overlay, with no further logic required.
/// For <see cref="OverlayLayoutKind.Wheel"/>, <see cref="WheelEntries"/> is populated and
/// <see cref="GridColumns"/> is empty; for <see cref="OverlayLayoutKind.Grid"/> it's the reverse.
/// </summary>
public sealed record OverlayViewState(
    string FolderPath,
    OverlayLayoutKind Layout,
    IReadOnlyList<NumberedEntry> WheelEntries,
    IReadOnlyList<GridColumn> GridColumns,
    int? ZoomedColumn,
    bool Truncated);
