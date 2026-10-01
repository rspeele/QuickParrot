using QuickParrot.Core.Grabs;

namespace QuickParrot.Core.Tests.Grabs;

public class PendingGrabDisplayTests
{
    [Fact]
    public void Format_ShowsLocalTimeAndRoundedDuration()
    {
        var grab = new PendingGrab("grab-1", "path", new DateTimeOffset(2026, 9, 30, 21, 4, 0, TimeSpan.Zero), TimeSpan.FromSeconds(29.6));

        Assert.Equal($"{grab.GrabbedAt.ToLocalTime():HH:mm} · 30 s", PendingGrabDisplay.Format(grab));
    }

    [Fact]
    public void Format_NeverShowsZeroSeconds()
    {
        var grab = new PendingGrab("grab-1", "path", DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(200));

        Assert.EndsWith("1 s", PendingGrabDisplay.Format(grab));
    }
}
