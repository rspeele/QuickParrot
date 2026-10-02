namespace QuickParrot.Core.Diagnostics;

public enum ClipLevelVerdict
{
    NothingCameThrough,
    SilentClip,
    TooShortToJudge,
    NoDrop,
    Dropped,
    StoppedEarly,
}

/// <summary>
/// How a clip's level on the cable compared with the file's own. <see cref="GainDb"/> is the recording's level
/// relative to the file's near the clip's start.
/// </summary>
public sealed record ClipLevelAnalysis(
    ClipLevelVerdict Verdict, double? GainDb = null, double? DropDb = null, TimeSpan? DropAfter = null)
{
    public string Summary => Verdict switch
    {
        ClipLevelVerdict.NothingCameThrough =>
            "Nothing came through the cable. Check that the cable is your output device and isn't muted.",
        ClipLevelVerdict.SilentClip => "The clip itself is silent, so there's nothing to compare.",
        ClipLevelVerdict.TooShortToJudge => "The clip was too short or quiet to tell whether its level changed.",
        ClipLevelVerdict.Dropped =>
            $"Level dropped by ~{DropDb:0} dB about {DropAfter?.TotalSeconds:0.0} s after the clip started.",
        ClipLevelVerdict.StoppedEarly =>
            $"The clip went silent about {DropAfter?.TotalSeconds:0.0} s after it started: it was stopped (e.g. by a "
            + "chord tap) or something muted it completely.",
        _ => "No level drop detected.",
    };

    /// <summary>The overall level change, for context; null when there's no gain to report.</summary>
    public string? GainNote => GainDb is { } gain
        ? $"At the start, the clip reached the cable {Describe(gain)} (your cable volume and Windows' CABLE Input "
          + "volume both affect this)."
        : null;

    private static string Describe(double gainDb) => Math.Round(gainDb) switch
    {
        0 => "as loud as the file",
        > 0 => $"{gainDb:0} dB louder than the file",
        _ => $"{-gainDb:0} dB quieter than the file",
    };
}
