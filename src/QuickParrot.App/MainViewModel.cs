using QuickParrot.Core.Devices;
using QuickParrot.Core.Engine;
using QuickParrot.Core.Keyboard;
using QuickParrot.Core.Library;
using QuickParrot.Core.Settings;
using QuickParrot.Overlay;

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
    private readonly IChordKeyHook _hook;
    private readonly OverlayHost _overlay;
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
    private SmallFolderLayout _smallFolderLayout;
    private string _deviceStatus = "";
    private string _status = "";
    private string _libraryWarning = "";
    private CancellationTokenSource? _captureCts;
    private string _statusBeforeCapture = "";

    public MainViewModel(
        QuickParrotEngine engine, IAudioDeviceCatalog devices, IChordKeyHook hook, OverlayHost overlay, string? startupWarning = null)
    {
        _engine = engine;
        _devices = devices;
        _hook = hook;
        _overlay = overlay;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _engine.ErrorOccurred += message => _ui.Post(_ => Status = message, null);

        var settings = engine.Settings;
        _selectedCableId = settings.CableDeviceId ?? "";
        _selectedMonitorId = settings.MonitorDeviceId ?? "";
        _cableVolume = settings.CableVolume;
        _monitorVolume = settings.MonitorVolume;
        _smallFolderLayout = settings.SmallFolderLayout;
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

    public IReadOnlyList<SmallFolderLayout> SmallFolderLayoutChoices { get; } = Enum.GetValues<SmallFolderLayout>();

    public SmallFolderLayout SmallFolderLayout
    {
        get => _smallFolderLayout;
        set
        {
            if (!SetField(ref _smallFolderLayout, value))
                return;

            _engine.UpdateSettings(s => s with { SmallFolderLayout = value });
            _overlay.SmallFolderLayout = value;
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
        internal set => SetField(ref _status, value);
    }

    public string LibraryWarning
    {
        get => _libraryWarning;
        private set
        {
            if (SetField(ref _libraryWarning, value))
                OnPropertyChanged(nameof(HasLibraryWarning));
        }
    }

    public bool HasLibraryWarning => _libraryWarning.Length > 0;

    public string ChordKeyDisplay => _hook.ChordKey.ToString();

    public bool HotkeysEnabled
    {
        get => _hook.Enabled;
        set
        {
            if (_hook.Enabled == value)
                return;

            _hook.Enabled = value;
            _engine.UpdateSettings(s => s with { HotkeysEnabled = value });
            OnPropertyChanged();
        }
    }

    /// <summary>Waits for the next key press and, if valid, makes it the chord key.</summary>
    public async Task ChangeChordKeyAsync()
    {
        if (_captureCts is null)
            _statusBeforeCapture = Status;

        _captureCts?.Cancel();
        var cts = new CancellationTokenSource();
        _captureCts = cts;

        Status = "Press a key… (Esc to cancel)";
        ScanKey? captured;
        try
        {
            captured = await _hook.CaptureNextKeyAsync(cts.Token);
        }
        catch (InvalidOperationException)
        {
            Status = "Hotkeys aren't running, so the chord key can't be changed.";
            return;
        }
        catch (OperationCanceledException)
        {
            if (_captureCts == cts) // cancelled, not superseded by a newer capture
                Status = _statusBeforeCapture;
            return;
        }
        finally
        {
            if (_captureCts == cts)
                _captureCts = null;
        }

        if (captured is null)
        {
            Status = _statusBeforeCapture;
            return;
        }

        if (!captured.Value.IsValidChordKey)
        {
            Status = $"{captured.Value} can't be the chord key. Pick another key.";
            return;
        }

        _hook.ChordKey = captured.Value;
        _engine.UpdateSettings(s => s with { ChordKey = captured.Value });
        OnPropertyChanged(nameof(ChordKeyDisplay));
        Status = $"Chord key changed to {captured.Value}.";
    }

    /// <summary>Cancels an in-progress <see cref="ChangeChordKeyAsync"/> capture, e.g. because the window lost focus.</summary>
    public void CancelChordKeyCapture() => _captureCts?.Cancel();

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
        LibraryWarning = _browser?.OverlayTruncationWarning ?? "";
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
