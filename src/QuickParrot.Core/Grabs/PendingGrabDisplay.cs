namespace QuickParrot.Core.Grabs;

/// <summary>Formatting for the pending-grabs list, e.g. "21:04 · 30 s".</summary>
public static class PendingGrabDisplay
{
    public static string Format(PendingGrab grab) => $"{grab.GrabbedAt.ToLocalTime():HH:mm} · {Seconds(grab.Duration)}";

    /// <summary>Whole seconds, at least 1, e.g. "30 s".</summary>
    public static string Seconds(TimeSpan duration) => $"{Math.Max(1, (int)Math.Round(duration.TotalSeconds))} s";
}
