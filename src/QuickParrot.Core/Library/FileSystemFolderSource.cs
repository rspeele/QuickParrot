namespace QuickParrot.Core.Library;

/// <summary>Reads the sound library from disk, confined to a root directory.</summary>
public sealed class FileSystemFolderSource : IFolderSource
{
    private static readonly string[] AudioExtensions = [".wav", ".mp3", ".m4a", ".aac", ".wma", ".flac"];

    private static readonly EnumerationOptions Options = new()
    {
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        IgnoreInaccessible = true,
    };

    private readonly string _root;

    public FileSystemFolderSource(string rootDirectory)
    {
        _root = Path.GetFullPath(rootDirectory);
    }

    public IReadOnlyList<FolderEntry>? GetEntries(string relativePath)
    {
        var fullPath = ResolvePath(relativePath);
        if (fullPath is null || !Directory.Exists(fullPath))
            return null;

        try
        {
            var entries = new List<FolderEntry>();
            foreach (var info in new DirectoryInfo(fullPath).EnumerateFileSystemInfos("*", Options))
            {
                var isFolder = info is DirectoryInfo;
                if (isFolder || IsAudioFile(info.Extension))
                    entries.Add(new FolderEntry(info.Name, isFolder, Combine(relativePath, info.Name)));
            }

            entries.Sort(FolderEntryOrdering.Comparer);
            return entries;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return []; // exists but unreadable: show it empty rather than treat it as gone
        }
    }

    public string? GetFullPath(string relativePath)
    {
        var fullPath = ResolvePath(relativePath);
        return fullPath is null || fullPath.Equals(_root, StringComparison.OrdinalIgnoreCase) ? null : fullPath;
    }

    public bool ClipExists(string relativePath) =>
        GetFullPath(relativePath) is { } fullPath && IsAudioFile(Path.GetExtension(fullPath)) && File.Exists(fullPath);

    // Resolves a "/"-separated relative path under the root, returning null if it would escape the root.
    private string? ResolvePath(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return _root;

        var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var combined = Path.GetFullPath(Path.Combine([_root, .. segments]));
        return LibraryPathResolver.RelativePathWithin(_root, combined) is null ? null : combined;
    }

    private static string Combine(string relativePath, string name) =>
        string.IsNullOrEmpty(relativePath) ? name : $"{relativePath}/{name}";

    private static bool IsAudioFile(string extension) =>
        AudioExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
}
