namespace QuickParrot.Core.Library;

/// <summary>
/// Provides the contents of a folder in the sound library. Relative paths use "" for the root
/// and "/" as a separator regardless of platform.
/// </summary>
public interface IFolderSource
{
    /// <summary>Returns the entries of the folder at <paramref name="relativePath"/>, or null if it doesn't exist.</summary>
    IReadOnlyList<FolderEntry>? GetEntries(string relativePath);
}
