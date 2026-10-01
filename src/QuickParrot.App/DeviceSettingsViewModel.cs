using QuickParrot.App.Mvvm;
using QuickParrot.Core.Devices;
using QuickParrot.Core.Settings;

namespace QuickParrot.App;

/// <summary>The cable, monitor and mic dropdowns and output volumes. Devices are enumerated only on <see cref="Refresh"/>.</summary>
public sealed class DeviceSettingsViewModel : ObservableObject
{
    private static readonly (string, Func<AppSettings, object?>)[] Properties =
    [
        (nameof(SelectedCableId), s => s.CableDeviceId),
        (nameof(SelectedMonitorId), s => s.MonitorDeviceId),
        (nameof(DeviceStatus), s => (s.CableDeviceId, s.MonitorDeviceId)),
        (nameof(CableVolume), s => s.CableVolume),
        (nameof(MonitorVolume), s => s.MonitorVolume),
        (nameof(SelectedMicDeviceId), s => s.MicDeviceId),
    ];

    private readonly SettingsMirror _settings;
    private readonly IAudioDeviceCatalog _devices;
    private readonly ICaptureDeviceCatalog _captureDevices;
    private IReadOnlyList<AudioDeviceInfo> _renderDevices = [];
    private string? _defaultRenderId;
    private OutputDeviceChoices _outputChoices = new([], []);
    private IReadOnlyList<MicDeviceChoice> _micDeviceChoices = [];

    public DeviceSettingsViewModel(SettingsMirror settings, IAudioDeviceCatalog devices, ICaptureDeviceCatalog captureDevices)
    {
        _settings = settings;
        _devices = devices;
        _captureDevices = captureDevices;
        _settings.Changed += (old, now) => OnPropertiesChanged(old, now, Properties);
        Refresh();
    }

    public IReadOnlyList<OutputDeviceChoice> CableChoices => _outputChoices.Cable;

    public IReadOnlyList<OutputDeviceChoice> MonitorChoices => _outputChoices.Monitor;

    public IReadOnlyList<MicDeviceChoice> MicDeviceChoices => _micDeviceChoices;

    // WPF writes null while a choices list is being replaced; that isn't a choice.
    public string SelectedCableId
    {
        get => _settings.Current.CableDeviceId ?? "";
        set
        {
            if (value is not null)
                _settings.Update(s => s with { CableDeviceId = NullIfEmpty(value) });
        }
    }

    public string SelectedMonitorId
    {
        get => _settings.Current.MonitorDeviceId ?? "";
        set
        {
            if (value is not null)
                _settings.Update(s => s with { MonitorDeviceId = NullIfEmpty(value) });
        }
    }

    public string SelectedMicDeviceId
    {
        get => _settings.Current.MicDeviceId ?? "";
        set
        {
            if (value is not null)
                _settings.Update(s => s with { MicDeviceId = NullIfEmpty(value) });
        }
    }

    public double CableVolume
    {
        get => _settings.Current.CableVolume;
        set => _settings.Update(s => s with { CableVolume = (float)value });
    }

    public double MonitorVolume
    {
        get => _settings.Current.MonitorVolume;
        set => _settings.Update(s => s with { MonitorVolume = (float)value });
    }

    public string DeviceStatus => OutputDeviceSelector.Select(
            _renderDevices, _settings.Current.CableDeviceId, _settings.Current.MonitorDeviceId, _defaultRenderId)
        .Describe();

    /// <summary>Re-reads the system's devices into the dropdowns.</summary>
    public void Refresh()
    {
        _renderDevices = _devices.GetRenderDevices();
        _defaultRenderId = _devices.GetDefaultRenderDeviceId();
        _outputChoices = OutputDeviceMenu.Build(_renderDevices);
        _micDeviceChoices = MicDeviceMenu.Build(
            _captureDevices.GetCaptureDevices(),
            _captureDevices.GetDefaultCaptureDeviceId(),
            _captureDevices.GetDefaultCommunicationsCaptureDeviceId());

        // The lists first, then the selections, so each dropdown re-selects from its new list.
        OnPropertyChanged(nameof(CableChoices));
        OnPropertyChanged(nameof(MonitorChoices));
        OnPropertyChanged(nameof(MicDeviceChoices));
        OnPropertyChanged(nameof(SelectedCableId));
        OnPropertyChanged(nameof(SelectedMonitorId));
        OnPropertyChanged(nameof(SelectedMicDeviceId));
        OnPropertyChanged(nameof(DeviceStatus));
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
