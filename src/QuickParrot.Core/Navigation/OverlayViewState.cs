using System.Collections.Immutable;
using QuickParrot.Core.Favorites;

namespace QuickParrot.Core.Navigation;

public enum OverlayLayoutKind
{
    Wheel,
    Grid,
}

/// <summary>How a <see cref="OverlayLayoutKind.Wheel"/> folder (9 or fewer entries) is drawn.</summary>
public enum SmallFolderLayout
{
    List,
    Ring,
}

public sealed record NumberedEntry(int Number, string Name, bool IsFolder);

public sealed record GridColumn(int Number, ImmutableArray<NumberedEntry> Entries);

/// <summary>The favorites strip shown while assigning <see cref="TargetSlot"/>.</summary>
/// <param name="Slots">All twelve slots, F1 first.</param>
/// <param name="LastPlayedName">What pressing the target's F-key again would assign, or null if nothing has played.</param>
/// <param name="ChordKeyName">For the "release B to cancel" hint.</param>
public sealed record FavoritesPanel(
    ImmutableArray<FavoriteSlotView> Slots, int TargetSlot, string? LastPlayedName, string ChordKeyName);

/// <summary>
/// Everything a renderer needs to draw the overlay, with no further logic required.
/// For <see cref="OverlayLayoutKind.Wheel"/>, <see cref="WheelEntries"/> is populated and
/// <see cref="GridColumns"/> is empty; for <see cref="OverlayLayoutKind.Grid"/> it's the reverse.
/// <see cref="Favorites"/> is set only while assigning a favorite.
/// </summary>
/// <param name="SmallFolderLayout">The user's setting, applied by the engine; the navigator leaves the default.</param>
public sealed record OverlayViewState(
    string FolderPath,
    OverlayLayoutKind Layout,
    ImmutableArray<NumberedEntry> WheelEntries,
    ImmutableArray<GridColumn> GridColumns,
    int? ZoomedColumn,
    bool Truncated,
    FavoritesPanel? Favorites = null,
    SmallFolderLayout SmallFolderLayout = SmallFolderLayout.List,
    bool ShowSaveNavigationHint = false,
    bool ShiftHeld = false);
