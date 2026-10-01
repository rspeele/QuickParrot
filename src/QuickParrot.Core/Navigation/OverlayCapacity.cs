namespace QuickParrot.Core.Navigation;

/// <summary>How many entries of one folder the chord overlay can show.</summary>
public static class OverlayCapacity
{
    public const int MaxEntries = 81;

    /// <summary>Set when a folder has more entries than the chord overlay can show.</summary>
    public static string? TruncationWarning(int entryCount) => entryCount > MaxEntries
        ? $"This folder has {entryCount} entries; the overlay only shows the first {MaxEntries}. Consider subfolders."
        : null;
}
