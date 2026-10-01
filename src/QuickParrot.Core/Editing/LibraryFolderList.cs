using QuickParrot.Core.Library;

namespace QuickParrot.Core.Editing;

/// <param name="RelativePath">"/"-separated, "" for the library root.</param>
public sealed record LibraryFolder(string RelativePath)
{
    public string Display => RelativePath.Length == 0 ? LibraryFolderList.RootName : RelativePath.Replace("/", " › ");
}

/// <summary>The library's folders as a flat, depth-first list of paths for a destination picker.</summary>
public static class LibraryFolderList
{
    public const string RootName = "Library (top level)";

    public static IReadOnlyList<LibraryFolder> Build(IFolderSource source, int maxFolders = 2000, int maxDepth = 10)
    {
        var folders = new List<LibraryFolder> { new("") };
        Add(source, "", 1, folders, maxFolders, maxDepth);
        return folders;
    }

    /// <summary>The folder with <paramref name="relativePath"/>, else its nearest listed ancestor, else the root.</summary>
    public static LibraryFolder Find(IReadOnlyList<LibraryFolder> folders, string? relativePath)
    {
        var path = (relativePath ?? "").Trim('/');
        while (true)
        {
            var match = folders.FirstOrDefault(f => string.Equals(f.RelativePath, path, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;

            if (path.Length == 0)
                return folders[0];

            path = LibraryPathResolver.ParentOf(path);
        }
    }

    /// <summary>
    /// The listed folder for a full path picked in a folder dialog, or null (keep the current one) if it's outside
    /// <paramref name="root"/>; the warning explains either a refusal or a fallback to a listed ancestor.
    /// </summary>
    public static (LibraryFolder? Folder, string? Warning) ResolvePicked(string root, IReadOnlyList<LibraryFolder> folders, string pickedFullPath)
    {
        if (LibraryPathResolver.RelativePathWithin(root, pickedFullPath) is not { } relative)
            return (null, $"Pick a folder inside your library ({root}).");

        var found = Find(folders, relative);
        return found.RelativePath.Equals(relative, StringComparison.OrdinalIgnoreCase)
            ? (found, null)
            : (found, $"That folder isn't listed (hidden or nested too deep), so clips will go to {found.Display}.");
    }

    private static void Add(IFolderSource source, string path, int depth, List<LibraryFolder> folders, int maxFolders, int maxDepth)
    {
        if (depth > maxDepth)
            return;

        foreach (var entry in source.GetEntries(path) ?? [])
        {
            if (!entry.IsFolder)
                continue;
            if (folders.Count >= maxFolders)
                return;

            folders.Add(new LibraryFolder(entry.RelativePath));
            Add(source, entry.RelativePath, depth + 1, folders, maxFolders, maxDepth);
        }
    }
}
