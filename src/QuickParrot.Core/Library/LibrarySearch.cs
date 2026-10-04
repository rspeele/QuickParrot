namespace QuickParrot.Core.Library;

public static class LibrarySearch
{
    public const int FolderColorCount = 8;

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
        return clips.Where(clip =>
            {
                var searchableText = Path.GetFileNameWithoutExtension(clip.Name) + " " + ParentPath(clip.RelativePath);
                return words.All(word => searchableText.Contains(word, StringComparison.OrdinalIgnoreCase));
            })
            .OrderBy(clip => clip.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(clip => clip.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(clip => clip.RelativePath, StringComparer.Ordinal)
            .Take(9).ToArray();
    }

    public static LibrarySearchFolderContext? GetFolderContext(string relativePath)
    {
        var folders = ParentPath(relativePath).Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return folders.Length == 0 ? null : new LibrarySearchFolderContext(folders[0], folders[^1]);
    }

    public static IReadOnlyDictionary<string, LibrarySearchFolderContext?> CreateFolderContexts(IReadOnlyList<FolderEntry> clips)
    {
        var contexts = new Dictionary<string, LibrarySearchFolderContext?>(StringComparer.Ordinal);
        foreach (var clip in clips)
            contexts[clip.RelativePath] = GetFolderContext(clip.RelativePath);

        var colors = contexts.Values.OfType<LibrarySearchFolderContext>()
            .Select(context => context.TopFolder)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(name => name, StringComparer.Ordinal)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select((name, index) => (Name: name, Color: index % FolderColorCount))
            .ToDictionary(folder => folder.Name, folder => folder.Color, StringComparer.OrdinalIgnoreCase);
        foreach (var path in contexts.Keys)
            if (contexts[path] is { } context)
                contexts[path] = context with { ColorIndex = colors[context.TopFolder] };
        return contexts;
    }

    private static string ParentPath(string relativePath)
    {
        var separator = relativePath.LastIndexOfAny(['/', '\\']);
        return separator < 0 ? "" : relativePath[..separator];
    }
}

public sealed record LibrarySearchFolderContext(string TopFolder, string ParentFolder)
{
    public int? ColorIndex { get; init; }
}
