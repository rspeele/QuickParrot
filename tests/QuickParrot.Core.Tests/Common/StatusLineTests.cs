using QuickParrot.Core.Common;

namespace QuickParrot.Core.Tests.Common;

public class StatusLineTests
{
    private const string Summary = "Audio setup: 1 problem — see Diagnostics";

    [Fact]
    public void Display_IsTheSummaryAlone_WithNoMessage()
    {
        Assert.Equal(Summary, (StatusLine.Empty with { DiagnosticsSummary = Summary }).Display);
    }

    [Fact]
    public void Display_AppendsTheSummaryToTheMessage()
    {
        Assert.Equal("Hotkeys off. | " + Summary, new StatusLine("Hotkeys off.", Summary).Display);
    }

    [Fact]
    public void Display_IsTheMessageAlone_OnceProblemsAreFixed()
    {
        var line = new StatusLine("Playing clip", Summary) with { DiagnosticsSummary = null };

        Assert.Equal("Playing clip", line.Display);
    }

    [Fact]
    public void Restore_ReplacesTheExpectedMessage_AndKeepsANewerSummary()
    {
        var line = new StatusLine("Press a key", null) with { DiagnosticsSummary = Summary };

        Assert.Equal(new StatusLine("Hotkeys off.", Summary), line.Restore("Press a key", "Hotkeys off."));
    }

    [Fact]
    public void Restore_LeavesANewerMessageAlone()
    {
        var line = new StatusLine("Couldn't play clip", null);

        Assert.Same(line, line.Restore("Press a key", "Hotkeys off."));
    }
}
