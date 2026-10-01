namespace QuickParrot.Core.Common;

/// <summary>The main window's status line: the latest message, plus the audio-setup summary while there are problems.</summary>
public sealed record StatusLine(string Message, string? DiagnosticsSummary)
{
    public const string Separator = " | ";

    public static readonly StatusLine Empty = new("", null);

    public string Display => DiagnosticsSummary switch
    {
        null => Message,
        var summary when Message.Length == 0 => summary,
        var summary => Message + Separator + summary,
    };

    /// <summary>Puts <paramref name="replacement"/> back only if <paramref name="expected"/> is still showing.</summary>
    public StatusLine Restore(string expected, string replacement) =>
        Message == expected ? this with { Message = replacement } : this;
}
