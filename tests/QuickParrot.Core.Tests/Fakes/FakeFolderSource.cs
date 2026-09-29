using QuickParrot.Core.Library;

namespace QuickParrot.Core.Tests.Fakes;

// In-memory IFolderSource for fast, IO-free tests. Folders are declared by relative path;
// entries are derived from direct children and sorted with the same ordering as the real source.
public sealed class FakeFolderSource : IFolderSource
{
    private readonly Dictionary<string, List<FolderEntry>> _children = new();

    public FakeFolderSource()
    {
        _children[""] = [];
    }

    /// <summary>Declares a subfolder under <paramref name="parentPath"/> and returns its own path.</summary>
    public string AddFolder(string parentPath, string name)
    {
        var path = Combine(parentPath, name);
        _children[parentPath].Add(new FolderEntry(name, true, path));
        _children.TryAdd(path, []);
        return path;
    }

    /// <summary>Declares an audio file under <paramref name="parentPath"/> and returns its own path.</summary>
    public string AddFile(string parentPath, string name)
    {
        var path = Combine(parentPath, name);
        _children[parentPath].Add(new FolderEntry(name, false, path));
        return path;
    }

    /// <summary>Removes a previously declared folder so tests can simulate it disappearing.</summary>
    public void RemoveFolder(string parentPath, string name)
    {
        var path = Combine(parentPath, name);
        _children[parentPath].RemoveAll(e => e.RelativePath == path);
        _children.Remove(path);
    }

    public void RemoveFile(string parentPath, string name)
    {
        var path = Combine(parentPath, name);
        _children[parentPath].RemoveAll(e => e.RelativePath == path);
    }

    public IReadOnlyList<FolderEntry>? GetEntries(string relativePath)
    {
        if (!_children.TryGetValue(relativePath, out var entries))
            return null;

        var sorted = new List<FolderEntry>(entries);
        sorted.Sort(FolderEntryOrdering.Comparer);
        return sorted;
    }

    private static string Combine(string parentPath, string name) =>
        string.IsNullOrEmpty(parentPath) ? name : $"{parentPath}/{name}";
}
