using System.Text.Json.Serialization;
using QuickParrot.Core.Diagnostics;
using QuickParrot.Core.Favorites;
using QuickParrot.Core.Keyboard;
using QuickParrot.Core.Mic;
using QuickParrot.Core.Navigation;
using QuickParrot.Core.Playback;

namespace QuickParrot.Core.Settings;

public sealed record AppSettings
{
    public const int MaxMarginMilliseconds = 5000;
    public const int MinReplayBufferSeconds = 5;
    public const int MaxReplayBufferSeconds = 120;

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

    /// <summary>Keep the last <see cref="ReplayBufferSeconds"/> of the monitor output in memory, for grabbing.</summary>
    public bool ReplayBufferEnabled { get; init; } = true;

    public int ReplayBufferSeconds { get; init; } = 30;

    /// <summary>Null disables LiteLLM-based clip naming.</summary>
    public string? LiteLlmBaseUrl { get; init; }

    /// <summary>Base64 DPAPI ciphertext; never the plaintext key. See <c>Naming.IDpapiProtector</c>.</summary>
    public string? LiteLlmApiKeyEncrypted { get; init; }

    public string LiteLlmTranscriptionModel { get; init; } = "whisper-1";

    public string LiteLlmChatModel { get; init; } = "gpt-4o-mini";

    /// <summary>Whether releasing a clip-editor selection-edge drag plays a 1 s sample of that edge.</summary>
    public bool EditorPlaySampleOnDrag { get; init; } = true;

    public FavoriteSlots Favorites { get; init; } = FavoriteSlots.Empty;

    /// <summary>Plain F-keys play assigned favorites while a fullscreen game is focused, without the chord.</summary>
    public bool FavoritesWithoutChord { get; init; }

    public PlaybackOptions ToPlaybackOptions() => new(
        TimeSpan.FromMilliseconds(PreRollMilliseconds),
        TimeSpan.FromMilliseconds(PostRollMilliseconds),
        PushToTalkEnabled,
        MicDuckMode != MicDuckMode.Off);

    public OutputSettings ToOutputSettings() => new(CableDeviceId, MonitorDeviceId, CableVolume, MonitorVolume);

    public MicDuckSettings ToMicDuckSettings() => new(MicDuckMode, MicAttenuationPercent, MicDeviceId);

    public ConfiguredDevices ToConfiguredDevices() => new(CableDeviceId, MonitorDeviceId, MicDeviceId);

    /// <summary>These settings on <paramref name="root"/>; a different root starts navigation from its top.</summary>
    public AppSettings WithLibraryRoot(string? root) =>
        root == LibraryRoot ? this : this with { LibraryRoot = root, NavigatorPersistentPath = "" };

    /// <summary>These settings with <paramref name="key"/> as the chord key, or unchanged with the reason it can't be.</summary>
    public (AppSettings Settings, string? Error) WithChordKey(ScanKey key) =>
        PushToTalkBinding.ValidateChordKey(key) is { } error ? (this, error) : (this with { ChordKey = key }, null);

    /// <summary>These settings with <paramref name="binding"/> for push-to-talk, or unchanged with the reason it can't be.</summary>
    public (AppSettings Settings, string? Error) WithPushToTalkBinding(PushToTalkBinding binding) =>
        binding.Validate(ChordKey) is { } error ? (this, error) : (this with { PushToTalkBinding = binding }, null);

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
        ReplayBufferSeconds = Math.Clamp(ReplayBufferSeconds, MinReplayBufferSeconds, MaxReplayBufferSeconds),
        LiteLlmBaseUrl = string.IsNullOrWhiteSpace(LiteLlmBaseUrl) ? null : LiteLlmBaseUrl.Trim(),
        LiteLlmApiKeyEncrypted = string.IsNullOrEmpty(LiteLlmApiKeyEncrypted) ? null : LiteLlmApiKeyEncrypted,
        LiteLlmTranscriptionModel = string.IsNullOrWhiteSpace(LiteLlmTranscriptionModel) ? "whisper-1" : LiteLlmTranscriptionModel.Trim(),
        LiteLlmChatModel = string.IsNullOrWhiteSpace(LiteLlmChatModel) ? "gpt-4o-mini" : LiteLlmChatModel.Trim(),
        Favorites = Favorites ?? FavoriteSlots.Empty,
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
