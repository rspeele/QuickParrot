namespace QuickParrot.Core.Library;

/// <summary>Folder-by-folder browsing of the library for the desktop UI, confined to its root.</summary>
public sealed class LibraryBrowser
{
    private readonly IFolderSource _source;

    public LibraryBrowser(IFolderSource source)
    {
        _source = source;
        Refresh();
    }

    public string CurrentPath { get; private set; } = "";

    public IReadOnlyList<FolderEntry> Entries { get; private set; } = [];

    public bool CanGoUp => CurrentPath.Length > 0;

    /// <summary>Enters a folder and returns null, or returns the clip's relative path for the caller to play.</summary>
    public string? Open(FolderEntry entry)
    {
        if (!entry.IsFolder)
            return entry.RelativePath;

        CurrentPath = entry.RelativePath;
        Refresh();
        return null;
    }

    public void GoUp()
    {
        if (!CanGoUp)
            return;

        CurrentPath = LibraryPathResolver.ParentOf(CurrentPath);
        Refresh();
    }

    /// <summary>Re-reads the current folder, falling back to the nearest ancestor that still exists.</summary>
    public void Refresh()
    {
        var entries = _source.GetEntries(CurrentPath);
        while (entries is null && CurrentPath.Length > 0)
        {
            CurrentPath = LibraryPathResolver.ParentOf(CurrentPath);
            entries = _source.GetEntries(CurrentPath);
        }

        Entries = entries ?? [];
    }
}
