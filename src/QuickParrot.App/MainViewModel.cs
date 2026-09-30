using QuickParrot.Core.Devices;
using QuickParrot.Core.Engine;
using QuickParrot.Core.Library;

namespace QuickParrot.App;

public sealed record LibraryItem(FolderEntry Entry)
{
    public string Display => Entry.IsFolder ? $"[{Entry.Name}]" : Entry.Name;
}

/// <summary>An empty <see cref="Id"/> means "auto-detect" / "Windows default".</summary>
public sealed record DeviceChoice(string Id, string Name);

public sealed class MainViewModel : ObservableObject
{
    private readonly QuickParrotEngine _engine;
    private readonly IAudioDeviceCatalog _devices;
    private readonly SynchronizationContext _ui;
    private LibraryBrowser? _browser;
    private IReadOnlyList<LibraryItem> _entries = [];
    private IReadOnlyList<DeviceChoice> _cableChoices = [];
    private IReadOnlyList<DeviceChoice> _monitorChoices = [];
    private string _libraryRoot = "";
    private string _selectedCableId;
    private string _selectedMonitorId;
    private double _cableVolume;
    private double _monitorVolume;
    private string _deviceStatus = "";
    private string _status = "";

    public MainViewModel(QuickParrotEngine engine, IAudioDeviceCatalog devices, string? startupWarning = null)
    {
        _engine = engine;
        _devices = devices;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _engine.ErrorOccurred += message => _ui.Post(_ => Status = message, null);

        var settings = engine.Settings;
        _selectedCableId = settings.CableDeviceId ?? "";
        _selectedMonitorId = settings.MonitorDeviceId ?? "";
        _cableVolume = settings.CableVolume;
        _monitorVolume = settings.MonitorVolume;
        OpenLibrary(settings.LibraryRoot);
        RefreshDevices();
        Status = startupWarning ?? "";
    }

    public string LibraryRoot
    {
        get => _libraryRoot;
        private set => SetField(ref _libraryRoot, value);
    }

    public string CurrentPath => _browser is null ? "" : "/" + _browser.CurrentPath;

    public bool CanGoUp => _browser?.CanGoUp == true;

    public IReadOnlyList<LibraryItem> Entries
    {
        get => _entries;
        private set => SetField(ref _entries, value);
    }

    public IReadOnlyList<DeviceChoice> CableChoices
    {
        get => _cableChoices;
        private set => SetField(ref _cableChoices, value);
    }

    public IReadOnlyList<DeviceChoice> MonitorChoices
    {
        get => _monitorChoices;
        private set => SetField(ref _monitorChoices, value);
    }

    public string SelectedCableId
    {
        get => _selectedCableId;
        set
        {
            if (value is null || !SetField(ref _selectedCableId, value))
                return; // WPF writes null while the choices list is being replaced

            _engine.UpdateSettings(s => s with { CableDeviceId = NullIfEmpty(value) });
            UpdateDeviceStatus();
        }
    }

    public string SelectedMonitorId
    {
        get => _selectedMonitorId;
        set
        {
            if (value is null || !SetField(ref _selectedMonitorId, value))
                return;

            _engine.UpdateSettings(s => s with { MonitorDeviceId = NullIfEmpty(value) });
            UpdateDeviceStatus();
        }
    }

    public double CableVolume
    {
        get => _cableVolume;
        set
        {
            if (SetField(ref _cableVolume, value))
                _engine.UpdateSettings(s => s with { CableVolume = (float)value });
        }
    }

    public double MonitorVolume
    {
        get => _monitorVolume;
        set
        {
            if (SetField(ref _monitorVolume, value))
                _engine.UpdateSettings(s => s with { MonitorVolume = (float)value });
        }
    }

    public string DeviceStatus
    {
        get => _deviceStatus;
        private set => SetField(ref _deviceStatus, value);
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public void ChooseLibrary(string root)
    {
        _engine.UpdateSettings(s => s with { LibraryRoot = root });
        OpenLibrary(root);
    }

    public void Open(LibraryItem? item)
    {
        if (_browser is null || item is null)
            return;

        if (_browser.Open(item.Entry) is { } clipPath)
        {
            _engine.Play(clipPath);
            Status = $"Playing {item.Entry.Name}";
        }
        else
        {
            ShowEntries();
        }
    }

    public void GoUp()
    {
        _browser?.GoUp();
        ShowEntries();
    }

    public void Stop()
    {
        _engine.Stop();
        Status = "Stopped";
    }

    public void RefreshDevices()
    {
        var devices = _devices.GetRenderDevices().Where(d => d.State != AudioDeviceState.NotPresent).ToList();
        var listed = devices.Select(d => new DeviceChoice(d.Id, d.IsActive ? d.Name : $"{d.Name} ({d.State.ToString().ToLowerInvariant()})"));

        CableChoices = [new DeviceChoice("", "(Auto-detect cable)"), .. listed];
        MonitorChoices = [new DeviceChoice("", "(Windows default output)"), .. listed];
        OnPropertyChanged(nameof(SelectedCableId));
        OnPropertyChanged(nameof(SelectedMonitorId));
        UpdateDeviceStatus(devices);
        _browser?.Refresh();
        ShowEntries();
    }

    private void OpenLibrary(string? root)
    {
        _browser = string.IsNullOrEmpty(root) ? null : new LibraryBrowser(new FileSystemFolderSource(root));
        LibraryRoot = string.IsNullOrEmpty(root) ? "(no sound library chosen)" : root;
        ShowEntries();
    }

    private void ShowEntries()
    {
        Entries = _browser?.Entries.Select(e => new LibraryItem(e)).ToList() ?? [];
        OnPropertyChanged(nameof(CurrentPath));
        OnPropertyChanged(nameof(CanGoUp));
    }

    private void UpdateDeviceStatus(IReadOnlyList<AudioDeviceInfo>? devices = null)
    {
        var selection = OutputDeviceSelector.Select(
            devices ?? _devices.GetRenderDevices(),
            NullIfEmpty(SelectedCableId),
            NullIfEmpty(SelectedMonitorId),
            _devices.GetDefaultRenderDeviceId());
        DeviceStatus = selection.Describe();
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
