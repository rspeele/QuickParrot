namespace QuickParrot.Core.Library;

public static class LibraryClips
{
    // Bounds the walk, since a junction or symlink can loop a folder back onto itself.
    public const int MaxDepth = 8;
    public const int MaxFolders = 2000;

    /// <summary>Every clip's relative path, folder by folder in the library's own order, up to <paramref name="max"/>.</summary>
    public static IReadOnlyList<string> List(IFolderSource source, int max)
    {
        var clips = new List<string>();
        var folders = 0;
        Collect(source, "", 0, max, clips, ref folders);
        return clips;
    }

    private static void Collect(IFolderSource source, string folder, int depth, int max, List<string> clips, ref int folders)
    {
        if (++folders > MaxFolders)
            return;

        foreach (var entry in source.GetEntries(folder) ?? [])
        {
            if (clips.Count >= max || folders > MaxFolders)
                return;

            if (!entry.IsFolder)
                clips.Add(entry.RelativePath);
            else if (depth < MaxDepth)
                Collect(source, entry.RelativePath, depth + 1, max, clips, ref folders);
        }
    }
}
