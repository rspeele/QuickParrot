using System.Text.RegularExpressions;

namespace QuickParrot.Core.Library;

/// <summary>
/// Names for clips while they're being written into the library: not an audio extension, so the library skips them,
/// and recognizable, so the library watcher doesn't refresh for them.
/// </summary>
public static partial class ClipTempFile
{
    /// <summary>A fresh name like ".3fa2b1c4d5e6f7a8b9c0d1e2f3a4b5c6.tmp".</summary>
    public static string NewName() => $".{Guid.NewGuid():N}.tmp";

    public static bool IsMatch(string? fileName) => fileName is not null && Pattern().IsMatch(fileName);

    [GeneratedRegex(@"^\.[0-9a-fA-F]{32}\.tmp$", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();
}
