using QuickParrot.App.Mvvm;
using QuickParrot.Core.Mic;
using QuickParrot.Core.Navigation;
using QuickParrot.Core.Settings;

namespace QuickParrot.App;

public sealed record MicDuckModeChoice(MicDuckMode Mode, string Label);

/// <summary>The Settings tab's plain on/off and slider settings, read from and written to <see cref="SettingsMirror"/>.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private static readonly (string, Func<AppSettings, object?>)[] Properties =
    [
        (nameof(SmallFolderLayout), s => s.SmallFolderLayout),
        (nameof(HotkeysEnabled), s => s.HotkeysEnabled),
        (nameof(PushToTalkEnabled), s => s.PushToTalkEnabled),
        (nameof(PreRollMilliseconds), s => s.PreRollMilliseconds),
        (nameof(PostRollMilliseconds), s => s.PostRollMilliseconds),
        (nameof(MicDuckMode), s => s.MicDuckMode),
        (nameof(IsMicAttenuationEnabled), s => s.MicDuckMode == MicDuckMode.Attenuate),
        (nameof(MicAttenuationPercent), s => s.MicAttenuationPercent),
        (nameof(ReplayBufferEnabled), s => s.ReplayBufferEnabled),
        (nameof(ReplayBufferSeconds), s => s.ReplayBufferSeconds),
    ];

    private readonly SettingsMirror _settings;

    public SettingsViewModel(SettingsMirror settings)
    {
        _settings = settings;
        _settings.Changed += (old, now) => OnPropertiesChanged(old, now, Properties);
    }

    public IReadOnlyList<SmallFolderLayout> SmallFolderLayoutChoices { get; } = Enum.GetValues<SmallFolderLayout>();

    public SmallFolderLayout SmallFolderLayout
    {
        get => _settings.Current.SmallFolderLayout;
        set => _settings.Update(s => s with { SmallFolderLayout = value });
    }

    public bool HotkeysEnabled
    {
        get => _settings.Current.HotkeysEnabled;
        set => _settings.Update(s => s with { HotkeysEnabled = value });
    }

    public bool PushToTalkEnabled
    {
        get => _settings.Current.PushToTalkEnabled;
        set => _settings.Update(s => s with { PushToTalkEnabled = value });
    }

    public int PreRollMilliseconds
    {
        get => _settings.Current.PreRollMilliseconds;
        set => _settings.Update(s => s with { PreRollMilliseconds = value });
    }

    public int PostRollMilliseconds
    {
        get => _settings.Current.PostRollMilliseconds;
        set => _settings.Update(s => s with { PostRollMilliseconds = value });
    }

    public IReadOnlyList<MicDuckModeChoice> MicDuckModeChoices { get; } =
    [
        new(MicDuckMode.Off, "Off"),
        new(MicDuckMode.Mute, "Mute"),
        new(MicDuckMode.Attenuate, "Turn down"),
    ];

    public MicDuckMode MicDuckMode
    {
        get => _settings.Current.MicDuckMode;
        set => _settings.Update(s => s with { MicDuckMode = value });
    }

    public bool IsMicAttenuationEnabled => MicDuckMode == MicDuckMode.Attenuate;

    public int MicAttenuationPercent
    {
        get => _settings.Current.MicAttenuationPercent;
        set => _settings.Update(s => s with { MicAttenuationPercent = value });
    }

    public bool ReplayBufferEnabled
    {
        get => _settings.Current.ReplayBufferEnabled;
        set => _settings.Update(s => s with { ReplayBufferEnabled = value });
    }

    public int ReplayBufferSeconds
    {
        get => _settings.Current.ReplayBufferSeconds;
        set => _settings.Update(s => s with { ReplayBufferSeconds = value });
    }
}
