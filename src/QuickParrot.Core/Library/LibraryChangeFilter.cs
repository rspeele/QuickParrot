using System.Text.RegularExpressions;

namespace QuickParrot.Core.Library;

/// <summary>Recognizes filesystem events that shouldn't trigger a library refresh, e.g. the app's own temp files.</summary>
public static partial class LibraryChangeFilter
{
    /// <summary>True for a name like ".3fa2b1c4d5e6f7a8b9c0d1e2f3a4b5c6.tmp" — see ClipEncoder's temp file naming.</summary>
    public static bool IsIgnorableTempFile(string? fileName) =>
        fileName is not null && TempFilePattern().IsMatch(fileName);

    [GeneratedRegex(@"^\.[0-9a-fA-F]{32}\.tmp$", RegexOptions.IgnoreCase)]
    private static partial Regex TempFilePattern();
}
