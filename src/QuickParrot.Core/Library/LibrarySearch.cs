namespace QuickParrot.Core.Library;

public static class LibrarySearch
{
    public static IReadOnlyList<FolderEntry> Collect(IFolderSource source)
    {
        var clips = new List<FolderEntry>();
        var folders = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        folders.Push("");
        while (folders.TryPop(out var folder))
        {
            if (!visited.Add(folder))
                continue;
            foreach (var entry in source.GetEntries(folder) ?? [])
            {
                if (entry.IsFolder)
                    folders.Push(entry.RelativePath);
                else
                    clips.Add(entry);
            }
        }
        return clips;
    }

    public static FolderEntry[] Match(IReadOnlyList<FolderEntry> clips, string query)
    {
        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return clips.Where(clip => words.All(word =>
                Path.GetFileNameWithoutExtension(clip.Name).Contains(word, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(clip => clip.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(clip => clip.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(clip => clip.RelativePath, StringComparer.Ordinal)
            .Take(9).ToArray();
    }
}
