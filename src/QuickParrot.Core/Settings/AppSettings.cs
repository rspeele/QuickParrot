using QuickParrot.Core.Playback;

namespace QuickParrot.Core.Settings;

public sealed record AppSettings
{
    public const int MaxMarginMilliseconds = 5000;

    public string? LibraryRoot { get; init; }

    public string NavigatorPersistentPath { get; init; } = "";

    /// <summary>Null auto-detects the virtual cable.</summary>
    public string? CableDeviceId { get; init; }

    /// <summary>Null follows the Windows default output.</summary>
    public string? MonitorDeviceId { get; init; }

    public float CableVolume { get; init; } = 1f;

    public float MonitorVolume { get; init; } = 1f;

    public bool PushToTalkEnabled { get; init; }

    /// <summary>Placeholder until push-to-talk key simulation exists, e.g. "V".</summary>
    public string? PushToTalkKey { get; init; }

    public bool MicMuteEnabled { get; init; }

    public int PreRollMilliseconds { get; init; } = 500;

    public int PostRollMilliseconds { get; init; } = 500;

    public PlaybackOptions ToPlaybackOptions() => new(
        TimeSpan.FromMilliseconds(PreRollMilliseconds),
        TimeSpan.FromMilliseconds(PostRollMilliseconds),
        PushToTalkEnabled,
        MicMuteEnabled);

    public OutputSettings ToOutputSettings() => new(CableDeviceId, MonitorDeviceId, CableVolume, MonitorVolume);

    /// <summary>Clamps out-of-range values, e.g. from a hand-edited settings file.</summary>
    public AppSettings Sanitized() => this with
    {
        NavigatorPersistentPath = NavigatorPersistentPath ?? "",
        CableVolume = ClampVolume(CableVolume),
        MonitorVolume = ClampVolume(MonitorVolume),
        PreRollMilliseconds = Math.Clamp(PreRollMilliseconds, 0, MaxMarginMilliseconds),
        PostRollMilliseconds = Math.Clamp(PostRollMilliseconds, 0, MaxMarginMilliseconds),
    };

    private static float ClampVolume(float volume) => float.IsFinite(volume) ? Math.Clamp(volume, 0f, 1f) : 1f;
}
