namespace QuickParrot.Core.Playback;

public sealed record PlaybackOptions(TimeSpan PreRoll, TimeSpan PostRoll, bool PushToTalkEnabled, bool MicMuteEnabled)
{
    public static readonly TimeSpan DefaultMargin = TimeSpan.FromMilliseconds(500);

    internal static PlaybackOptions Default { get; } = new(DefaultMargin, DefaultMargin, true, true);
}

public enum PlaybackPhase
{
    Idle,
    PreRoll,
    Playing,
    PostRoll,
}

public sealed record PlaybackError(string ClipPath, string Message);
