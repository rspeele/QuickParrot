namespace QuickParrot.Core.Grabs;

/// <summary>Formatting for the pending-grabs list, e.g. "21:04 · 30 s".</summary>
public static class PendingGrabDisplay
{
    public static string Format(PendingGrab grab) =>
        $"{grab.GrabbedAt.ToLocalTime():HH:mm} · {Math.Max(1, (int)Math.Round(grab.Duration.TotalSeconds))} s";
}
