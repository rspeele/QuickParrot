namespace QuickParrot.Core.Library;

// Shared so every IFolderSource implementation (real or fake) orders entries the same way.
public static class FolderEntryOrdering
{
    public static readonly IComparer<FolderEntry> Comparer = System.Collections.Generic.Comparer<FolderEntry>.Create(Compare);

    public static int Compare(FolderEntry a, FolderEntry b)
    {
        if (a.IsFolder != b.IsFolder)
            return a.IsFolder ? -1 : 1;

        return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
    }
}
