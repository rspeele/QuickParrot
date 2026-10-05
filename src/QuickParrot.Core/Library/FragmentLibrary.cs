namespace QuickParrot.Core.Library;

public static class FragmentLibrary
{
    public const string FolderName = "Fragments";
    public const string RootClipHint = "Put fragment clips inside a speaker folder under Fragments to use them in a phrase.";

    public static bool IsFragmentPath(string relativePath)
    {
        var segments = Segments(relativePath);
        return segments.Length > 0 && segments[0].Equals(FolderName, StringComparison.OrdinalIgnoreCase);
    }

    public static string? GetSpeaker(string relativeClipPath)
    {
        var segments = Segments(relativeClipPath);
        return segments.Length >= 3 && segments[0].Equals(FolderName, StringComparison.OrdinalIgnoreCase)
            ? segments[1] : null;
    }

    public static IReadOnlyDictionary<string, LibrarySearchFolderContext?> CreateFolderContexts(IReadOnlyList<FolderEntry> clips)
    {
        var stripped = clips.Select(clip => clip with { RelativePath = string.Join('/', Segments(clip.RelativePath).Skip(1)) }).ToArray();
        var contexts = LibrarySearch.CreateFolderContexts(stripped);
        return clips.Select((clip, index) => (clip.RelativePath, Context: contexts[stripped[index].RelativePath]))
            .ToDictionary(item => item.RelativePath, item => item.Context, StringComparer.Ordinal);
    }

    private static string[] Segments(string relativePath) =>
        relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
}
