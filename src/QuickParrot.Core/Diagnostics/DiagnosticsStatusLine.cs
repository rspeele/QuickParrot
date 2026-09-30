namespace QuickParrot.Core.Diagnostics;

/// <summary>
/// Keeps the diagnostics summary in a shared status line: appended to whatever else is showing when it changes,
/// removed once the problems are fixed, and left alone while it stays the same.
/// </summary>
public sealed class DiagnosticsStatusLine
{
    public const string Separator = " | ";

    private string? _summary;

    /// <summary>The new status text, or null to leave <paramref name="current"/> as it is.</summary>
    public string? Update(string current, DiagnosticsReport report)
    {
        var summary = report.StatusLine;
        if (summary == _summary)
            return null;

        var previous = _summary;
        _summary = summary;
        var rest = previous is not null && current.EndsWith(previous, StringComparison.Ordinal)
            ? current[..^previous.Length]
            : current;
        if (rest.EndsWith(Separator, StringComparison.Ordinal))
            rest = rest[..^Separator.Length];

        if (summary is null)
            return rest == current ? null : rest;

        return rest.Length == 0 ? summary : rest + Separator + summary;
    }
}
