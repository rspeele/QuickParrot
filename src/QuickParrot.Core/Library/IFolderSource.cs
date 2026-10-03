namespace QuickParrot.Core.Library;

/// <summary>
/// Provides the contents of a folder in the sound library. Relative paths use "" for the root
/// and "/" as a separator regardless of platform.
/// </summary>
public interface IFolderSource
{
    /// <summary>Returns the entries of the folder at <paramref name="relativePath"/>, or null if it doesn't exist.</summary>
    IReadOnlyList<FolderEntry>? GetEntries(string relativePath);

    /// <summary>Maps a relative clip path to an absolute file path, or null if it's empty or would escape the root.</summary>
    string? GetFullPath(string relativePath);

    /// <summary>Whether <paramref name="relativePath"/> is an audio file inside the library (not a folder).</summary>
    bool ClipExists(string relativePath);

    IReadOnlyList<FolderEntry> GetAllClips() => LibrarySearch.Collect(this);
}
