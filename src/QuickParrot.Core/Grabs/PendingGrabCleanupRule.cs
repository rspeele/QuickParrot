namespace QuickParrot.Core.Grabs;

/// <summary>Whether a pending grab should be deleted once its editor closes.</summary>
public static class PendingGrabCleanupRule
{
    /// <summary>Discarding always deletes it; otherwise it's deleted only once at least one clip was saved from it.</summary>
    public static bool ShouldDelete(bool discarded, int savedClipCount) => discarded || savedClipCount > 0;
}
