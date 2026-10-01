using System.Text;

namespace QuickParrot.Core.Editing;

/// <summary>Turns a typed or AI-suggested name into a safe Windows file name, and finds one that's free.</summary>
public static class ClipFileNames
{
    public const int MaxStemLength = 80;
    public const string Fallback = "Clip";

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };

    /// <summary>
    /// A file name stem (no extension): invalid characters become spaces, whitespace collapses, quotes and trailing
    /// dots go, reserved device names get a suffix, and the result is at most <see cref="MaxStemLength"/> characters.
    /// </summary>
    public static string Sanitize(string? name)
    {
        var builder = new StringBuilder(name?.Length ?? 0);
        var pendingSpace = false;
        foreach (var ch in WithoutLoneSurrogates(name ?? "").Normalize(NormalizationForm.FormC))
        {
            if (ch is '"' or '“' or '”')
                continue;

            if (char.IsWhiteSpace(ch) || IsInvalid(ch))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
                builder.Append(' ');

            builder.Append(ch);
            pendingSpace = false;
        }

        var stem = Truncate(builder.ToString(), MaxStemLength).TrimEnd(' ', '.').TrimStart('.', ' ');
        if (stem.Length == 0)
            return Fallback;

        var firstPart = stem.Split('.')[0].TrimEnd(' ');
        return ReservedNames.Contains(firstPart) ? stem + "_" : stem;
    }

    /// <summary>
    /// "stem.ext", or "stem (2).ext", "stem (3).ext", … — the first that <paramref name="isTaken"/> rejects. Matching is
    /// left to the callback, so tests need no file system.
    /// </summary>
    public static string UniqueFileName(string stem, string extension, Func<string, bool> isTaken)
    {
        for (var n = 1; ; n++)
        {
            var candidate = Candidate(stem, extension, n);
            if (!isTaken(candidate))
                return candidate;
        }
    }

    public static string Candidate(string stem, string extension, int n) =>
        n <= 1 ? stem + extension : $"{stem} ({n}){extension}";

    // string.Normalize throws on unpaired surrogates (e.g. from a mangled AI reply), so they're dropped first.
    private static string WithoutLoneSurrogates(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                builder.Append(text[i]).Append(text[++i]);
            else if (!char.IsSurrogate(text[i]))
                builder.Append(text[i]);
        }

        return builder.ToString();
    }

    // Never splits a surrogate pair.
    private static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength)
            return text;

        var cut = char.IsHighSurrogate(text[maxLength - 1]) ? maxLength - 1 : maxLength;
        return text[..cut];
    }

    private static bool IsInvalid(char ch) => ch < 32 || ch is '<' or '>' or ':' or '/' or '\\' or '|' or '?' or '*' or '\u007f';
}
