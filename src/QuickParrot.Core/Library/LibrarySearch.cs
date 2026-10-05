using System.Collections.Immutable;

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

    public static LibrarySearchResult[] CreateResults(IReadOnlyList<FolderEntry> clips)
    {
        var results = new List<LibrarySearchResult>();
        var variants = new Dictionary<string, List<FolderEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var clip in clips.Where(clip => !clip.IsFolder))
        {
            var stem = Path.GetFileNameWithoutExtension(clip.Name);
            var separator = stem.LastIndexOf('_');
            var numbered = separator > 0 && separator < stem.Length - 1 &&
                stem[(separator + 1)..].All(character => character is >= '0' and <= '9');
            if (!numbered)
            {
                results.Add(new LibrarySearchResult(clip.Name, stem, [clip]));
                continue;
            }

            var key = ParentPath(clip.RelativePath).Replace('\\', '/') + "/" + stem[..separator];
            if (!variants.TryGetValue(key, out var group))
                variants[key] = group = [];
            group.Add(clip);
        }

        foreach (var group in variants.Values)
        {
            var members = group.OrderBy(clip => clip.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(clip => clip.RelativePath, StringComparer.Ordinal)
                .DistinctBy(clip => clip.RelativePath.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase)
                .ToImmutableArray();
            var representative = members[0];
            var stem = Path.GetFileNameWithoutExtension(representative.Name);
            var name = members.Length > 1 ? stem[..stem.LastIndexOf('_')] : representative.Name;
            var phraseName = members.Length > 1 ? name : stem;
            results.Add(new LibrarySearchResult(name, phraseName, members));
        }
        return results.ToArray();
    }

    public static LibrarySearchResult[] Match(IReadOnlyList<FolderEntry> clips, string query) =>
        Match(CreateResults(clips), query);

    public static LibrarySearchResult[] Match(IReadOnlyList<LibrarySearchResult> results, string query)
    {
        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return results.Where(result =>
            {
                var searchableText = result.PhraseName + " " + ParentPath(result.RepresentativeClip.RelativePath);
                return words.All(word => searchableText.Contains(word, StringComparison.OrdinalIgnoreCase));
            })
            .OrderBy(result => result.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.RepresentativeClip.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.RepresentativeClip.RelativePath, StringComparer.Ordinal)
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
