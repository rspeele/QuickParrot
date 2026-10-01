namespace QuickParrot.Core.Library;

/// <summary>Converts between a library's root-relative paths ("/"-separated, "" for the root) and full filesystem paths.</summary>
public static class LibraryPathResolver
{
    /// <summary>The full path of <paramref name="relativePath"/> under <paramref name="root"/>.</summary>
    public static string FullPath(string root, string relativePath) =>
        relativePath.Length == 0 ? root : Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// <paramref name="fullPath"/>'s "/"-separated path relative to <paramref name="root"/> ("" for the root itself),
    /// or null if it's outside the root (case-insensitive, ignoring trailing separators).
    /// </summary>
    public static string? RelativePathWithin(string root, string fullPath)
    {
        var rootFull = TrimTrailingSeparators(Path.GetFullPath(root));
        var targetFull = TrimTrailingSeparators(Path.GetFullPath(fullPath));

        if (targetFull.Equals(rootFull, StringComparison.OrdinalIgnoreCase))
            return "";

        var rootWithSeparator = rootFull + Path.DirectorySeparatorChar;
        if (!targetFull.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            return null;

        return targetFull[rootWithSeparator.Length..]
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    /// <summary>The folder containing <paramref name="relativePath"/>; "" for a top-level item (or the root itself).</summary>
    public static string ParentOf(string relativePath)
    {
        var separator = relativePath.LastIndexOf('/');
        return separator < 0 ? "" : relativePath[..separator];
    }

    private static string TrimTrailingSeparators(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
