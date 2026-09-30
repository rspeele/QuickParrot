using System.Text.Json.Serialization;
using QuickParrot.Core.Keyboard;
using QuickParrot.Core.Mic;
using QuickParrot.Core.Playback;

namespace QuickParrot.Core.Settings;

/// <summary>How a folder with 9 or fewer entries is drawn in the overlay.</summary>
public enum SmallFolderLayout
{
    List,
    Ring,
}

public sealed record AppSettings
{
    public const int MaxMarginMilliseconds = 5000;

    public string? LibraryRoot { get; init; }

    public string NavigatorPersistentPath { get; init; } = "";

    public ScanKey ChordKey { get; init; } = ScanKey.DefaultChordKey;

    public bool HotkeysEnabled { get; init; } = true;

    /// <summary>Null auto-detects the virtual cable.</summary>
    public string? CableDeviceId { get; init; }

    /// <summary>Null follows the Windows default output.</summary>
    public string? MonitorDeviceId { get; init; }

    public float CableVolume { get; init; } = 1f;

    public float MonitorVolume { get; init; } = 1f;

    public bool PushToTalkEnabled { get; init; }

    public PushToTalkBinding PushToTalkBinding { get; init; } = PushToTalkBinding.Default;

    public MicDuckMode MicDuckMode { get; init; }

    /// <summary>In attenuate mode, the percentage of its original volume the mic drops to.</summary>
    public int MicAttenuationPercent { get; init; } = MicDuckSettings.DefaultAttenuationPercent;

    /// <summary>Null auto-detects the real mic.</summary>
    public string? MicDeviceId { get; init; }

    // Read from older settings files only; Sanitized folds it into MicDuckMode.
    [JsonInclude, JsonPropertyName("micMuteEnabled"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    private bool? LegacyMicMuteEnabled { get; init; }

    public int PreRollMilliseconds { get; init; } = 500;

    public int PostRollMilliseconds { get; init; } = 500;

    public SmallFolderLayout SmallFolderLayout { get; init; } = SmallFolderLayout.List;

    public PlaybackOptions ToPlaybackOptions() => new(
        TimeSpan.FromMilliseconds(PreRollMilliseconds),
        TimeSpan.FromMilliseconds(PostRollMilliseconds),
        PushToTalkEnabled,
        MicDuckMode != MicDuckMode.Off);

    public OutputSettings ToOutputSettings() => new(CableDeviceId, MonitorDeviceId, CableVolume, MonitorVolume);

    public MicDuckSettings ToMicDuckSettings() => new(MicDuckMode, MicAttenuationPercent, MicDeviceId);

    /// <summary>Clamps out-of-range values, e.g. from a hand-edited settings file.</summary>
    public AppSettings Sanitized() => this with
    {
        NavigatorPersistentPath = NavigatorPersistentPath ?? "",
        ChordKey = ChordKey.IsValidChordKey ? ChordKey : ScanKey.DefaultChordKey,
        CableVolume = ClampVolume(CableVolume),
        MonitorVolume = ClampVolume(MonitorVolume),
        PreRollMilliseconds = Math.Clamp(PreRollMilliseconds, 0, MaxMarginMilliseconds),
        PostRollMilliseconds = Math.Clamp(PostRollMilliseconds, 0, MaxMarginMilliseconds),
        SmallFolderLayout = Enum.IsDefined(SmallFolderLayout) ? SmallFolderLayout : SmallFolderLayout.List,
        PushToTalkEnabled = PushToTalkEnabled && IsPushToTalkBindingValid(),
        PushToTalkBinding = IsPushToTalkBindingValid() ? PushToTalkBinding : PushToTalkBinding.Default,
        MicDuckMode = SanitizeMicDuckMode(),
        MicAttenuationPercent = Math.Clamp(MicAttenuationPercent, 0, 100),
        MicDeviceId = string.IsNullOrEmpty(MicDeviceId) ? null : MicDeviceId,
        LegacyMicMuteEnabled = null,
    };

    private MicDuckMode SanitizeMicDuckMode()
    {
        if (!Enum.IsDefined(MicDuckMode))
            return MicDuckMode.Off;

        return MicDuckMode == MicDuckMode.Off && LegacyMicMuteEnabled == true ? MicDuckMode.Mute : MicDuckMode;
    }

    // An unusable binding is swapped for the default, but disabled so QuickParrot never presses a surprise key.
    private bool IsPushToTalkBindingValid() =>
        PushToTalkBinding.Validate(ChordKey.IsValidChordKey ? ChordKey : ScanKey.DefaultChordKey) is null;

    private static float ClampVolume(float volume) => float.IsFinite(volume) ? Math.Clamp(volume, 0f, 1f) : 1f;
}
