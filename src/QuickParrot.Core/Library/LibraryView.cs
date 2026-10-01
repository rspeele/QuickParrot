namespace QuickParrot.Core.Library;

/// <summary>One folder of the library as shown on the Library tab; every move returns a new view.</summary>
public sealed record LibraryView(string Path, IReadOnlyList<FolderEntry> Entries)
{
    public bool CanGoUp => Path.Length > 0;

    /// <summary>The root folder's contents.</summary>
    public static LibraryView Root(IFolderSource source) => At(source, "");

    /// <summary>Enters <paramref name="folder"/>; files aren't navigable, so they leave the view as it is.</summary>
    public LibraryView Open(IFolderSource source, FolderEntry folder) =>
        folder.IsFolder ? At(source, folder.RelativePath) : this;

    public LibraryView GoUp(IFolderSource source) => CanGoUp ? At(source, LibraryPathResolver.ParentOf(Path)) : this;

    /// <summary>Re-reads the folder, falling back to the nearest ancestor that still exists.</summary>
    public LibraryView Refresh(IFolderSource source) => At(source, Path);

    private static LibraryView At(IFolderSource source, string path)
    {
        var entries = source.GetEntries(path);
        while (entries is null && path.Length > 0)
        {
            path = LibraryPathResolver.ParentOf(path);
            entries = source.GetEntries(path);
        }

        return new LibraryView(path, entries ?? []);
    }
}
