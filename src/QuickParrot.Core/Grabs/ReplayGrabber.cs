using QuickParrot.Core.Replay;

namespace QuickParrot.Core.Grabs;

/// <summary>Either the saved grab or a user-facing reason there isn't one.</summary>
public sealed record GrabResult(PendingGrab? Grab, string? Error)
{
    public static GrabResult Saved(PendingGrab grab) => new(grab, null);

    public static GrabResult Failed(string error) => new(null, error);
}

/// <summary>Saves the replay buffer's recent audio as a pending grab. Does disk IO, so keep it off hot threads.</summary>
public sealed class ReplayGrabber(IReplaySource replay, IPendingGrabStore store)
{
    public const string DisabledMessage = "Instant replay is off";
    public const string EmptyMessage = "Nothing to grab yet";

    /// <summary>Never throws.</summary>
    public GrabResult Grab(TimeSpan length, DateTimeOffset grabbedAt)
    {
        try
        {
            var snapshot = replay.Snapshot(length);
            if (snapshot.Frames == 0)
                return GrabResult.Failed(EmptyMessage);

            if (snapshot.IsSilent)
                return GrabResult.Failed($"Nothing was playing in the last {PendingGrabDisplay.Seconds(snapshot.Duration)}.");

            snapshot = snapshot.DownmixedToStereo();
            return GrabResult.Saved(store.Save(snapshot.Samples.Span, snapshot.SampleRate, snapshot.Channels, grabbedAt));
        }
        catch (Exception e)
        {
            return GrabResult.Failed($"Couldn't save the grab: {e.Message}");
        }
    }

    /// <summary>The overlay confirmation, e.g. "Grabbed last 30 s".</summary>
    public static string SavedMessage(PendingGrab grab) => $"Grabbed last {PendingGrabDisplay.Seconds(grab.Duration)}";
}
