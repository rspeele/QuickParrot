using System.Diagnostics;
using System.IO;
using System.Net.Http;
using QuickParrot.App.Editor;
using QuickParrot.App.Library;
using QuickParrot.Core.Devices;
using QuickParrot.Core.Diagnostics;
using QuickParrot.Core.Editing;
using QuickParrot.Core.Engine;
using QuickParrot.Core.Grabs;
using QuickParrot.Core.Keyboard;
using QuickParrot.Core.Library;
using QuickParrot.Core.Mic;
using QuickParrot.Core.Naming;
using QuickParrot.Core.Navigation;
using QuickParrot.Core.Settings;

namespace QuickParrot.App;

/// <summary>Asks the host window to show the clip editor for a just-opened grab.</summary>
public sealed record GrabEditorRequest(ClipEditorViewModel ViewModel, PendingGrab Grab);

public sealed record LibraryItem(FolderEntry Entry)
{
    public string Display => Entry.IsFolder ? $"[{Entry.Name}]" : Entry.Name;
}

/// <summary>An empty <see cref="Id"/> means "auto-detect" / "Windows default".</summary>
public sealed record DeviceChoice(string Id, string Name);

public sealed record MicDuckModeChoice(MicDuckMode Mode, string Label);

public sealed record MouseButtonChoice(PushToTalkMouseButton Button, string Label);

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly QuickParrotEngine _engine;
    private readonly IAudioDeviceCatalog _devices;
    private readonly ICaptureDeviceCatalog _captureDevices;
    private readonly IChordKeyHook _hook;
    private readonly IPendingGrabStore _grabStore;
    private readonly IEditorPreview _preview;
    private readonly IClipEncoder _encoder;
    private readonly HttpClient _httpClient;
    private readonly IDpapiProtector _protector;
    private readonly SynchronizationContext _ui;
    private readonly TimeProvider _time;
    private readonly DiagnosticsStatusLine _diagnosticsStatus = new();
    private readonly HashSet<string> _openGrabIds = [];
    private readonly Dictionary<string, int> _savedGrabCounts = [];
    private readonly Dictionary<string, ClipEditorViewModel> _openEditors = [];
    private OpenedLibrary? _library;
    private LibraryWatcher? _libraryWatcher;
    private IReadOnlyList<LibraryItem> _entries = [];
    private IReadOnlyList<DeviceChoice> _cableChoices = [];
    private IReadOnlyList<DeviceChoice> _monitorChoices = [];
    private IReadOnlyList<MicDeviceChoice> _micDeviceChoices = [];
    private string _libraryRoot = "";
    private string _selectedCableId;
    private string _selectedMonitorId;
    private string _selectedMicDeviceId;
    private double _cableVolume;
    private double _monitorVolume;
    private SmallFolderLayout _smallFolderLayout;
    private bool _pushToTalkEnabled;
    private int _preRollMilliseconds;
    private int _postRollMilliseconds;
    private MicDuckMode _micDuckMode;
    private int _micAttenuationPercent;
    private string _deviceStatus = "";
    private string _status = "";
    private string _libraryWarning = "";
    private CancellationTokenSource? _captureCts;
    private string _statusBeforeCapture = "";
    private bool _replayBufferEnabled;
    private int _replayBufferSeconds;
    private string _liteLlmBaseUrl = "";
    private string _liteLlmTranscriptionModel = "";
    private string _liteLlmChatModel = "";
    private bool _hasSavedLiteLlmApiKey;
    private bool _isTestingLiteLlmConnection;
    private string _liteLlmConnectionTestResult = "";

    public MainViewModel(
        QuickParrotEngine engine,
        IAudioDeviceCatalog devices,
        ICaptureDeviceCatalog captureDevices,
        IChordKeyHook hook,
        IPendingGrabStore grabStore,
        IEditorPreview preview,
        IClipEncoder encoder,
        HttpClient httpClient,
        IDpapiProtector protector,
        DiagnosticsViewModel diagnostics,
        string? startupWarning = null,
        TimeProvider? time = null)
    {
        _engine = engine;
        _devices = devices;
        _captureDevices = captureDevices;
        _hook = hook;
        _grabStore = grabStore;
        _preview = preview;
        _encoder = encoder;
        _httpClient = httpClient;
        _protector = protector;
        _time = time ?? TimeProvider.System;
        Diagnostics = diagnostics;
        PendingGrabs = new PendingGrabsViewModel(grabStore, engine);
        Favorites = new FavoritesViewModel(engine, message => Status = message);
        diagnostics.ReportChanged += report =>
        {
            if (_diagnosticsStatus.Update(Status, report) is { } status)
                Status = status;
        };
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _engine.ErrorOccurred += message => _ui.Post(_ => Status = message, null);

        var settings = engine.Settings;
        _selectedCableId = settings.CableDeviceId ?? "";
        _selectedMonitorId = settings.MonitorDeviceId ?? "";
        _selectedMicDeviceId = settings.MicDeviceId ?? "";
        _cableVolume = settings.CableVolume;
        _monitorVolume = settings.MonitorVolume;
        _smallFolderLayout = settings.SmallFolderLayout;
        _pushToTalkEnabled = settings.PushToTalkEnabled;
        _preRollMilliseconds = settings.PreRollMilliseconds;
        _postRollMilliseconds = settings.PostRollMilliseconds;
        _micDuckMode = settings.MicDuckMode;
        _micAttenuationPercent = settings.MicAttenuationPercent;
        _replayBufferEnabled = settings.ReplayBufferEnabled;
        _replayBufferSeconds = settings.ReplayBufferSeconds;
        _liteLlmBaseUrl = settings.LiteLlmBaseUrl ?? "";
        _liteLlmTranscriptionModel = settings.LiteLlmTranscriptionModel;
        _liteLlmChatModel = settings.LiteLlmChatModel;
        _hasSavedLiteLlmApiKey = !string.IsNullOrEmpty(settings.LiteLlmApiKeyEncrypted);
        OpenLibrary(settings.LibraryRoot);
        RefreshDevices();
        Status = startupWarning ?? "";
    }

    public DiagnosticsViewModel Diagnostics { get; }

    public PendingGrabsViewModel PendingGrabs { get; }

    public FavoritesViewModel Favorites { get; }

    /// <summary>Raised when a grab is ready to edit; the view hosts <see cref="ClipEditorWindow"/> for it.</summary>
    public event Action<GrabEditorRequest>? EditorRequested;

    /// <summary>Raised when a grab that's already open is requested again; the view brings its window to the front.</summary>
    public event Action<PendingGrab>? EditorBringToFrontRequested;

    public string LibraryRoot
    {
        get => _libraryRoot;
        private set => SetField(ref _libraryRoot, value);
    }

    public string CurrentPath => _library is null ? "" : "/" + _library.Browser.CurrentPath;

    public bool CanGoUp => _library?.Browser.CanGoUp == true;

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
            Diagnostics.RequestCheck();
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
            Diagnostics.RequestCheck();
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

    public bool PushToTalkEnabled
    {
        get => _pushToTalkEnabled;
        set
        {
            if (!SetField(ref _pushToTalkEnabled, value))
                return;

            _engine.UpdateSettings(s => s with { PushToTalkEnabled = value });
        }
    }

    public string PushToTalkBindingDisplay => _hook.PushToTalkBinding.ToString();

    public IReadOnlyList<MouseButtonChoice> PushToTalkMouseButtonChoices { get; } =
        Enum.GetValues<PushToTalkMouseButton>()
            .Select(button => new MouseButtonChoice(button, PushToTalkBinding.FromMouse(button).ToString()))
            .ToList();

    /// <summary>Null when the current binding is a keyboard key rather than a mouse button.</summary>
    public PushToTalkMouseButton? SelectedPushToTalkMouseButton
    {
        get => _hook.PushToTalkBinding.MouseButton;
        set
        {
            if (value is null)
                return;

            TryApplyPushToTalkBinding(PushToTalkBinding.FromMouse(value.Value));
        }
    }

    public int PreRollMilliseconds
    {
        get => _preRollMilliseconds;
        set
        {
            var clamped = Math.Clamp(value, 0, AppSettings.MaxMarginMilliseconds);
            if (!SetField(ref _preRollMilliseconds, clamped))
                return;

            _engine.UpdateSettings(s => s with { PreRollMilliseconds = clamped });
        }
    }

    public int PostRollMilliseconds
    {
        get => _postRollMilliseconds;
        set
        {
            var clamped = Math.Clamp(value, 0, AppSettings.MaxMarginMilliseconds);
            if (!SetField(ref _postRollMilliseconds, clamped))
                return;

            _engine.UpdateSettings(s => s with { PostRollMilliseconds = clamped });
        }
    }

    public IReadOnlyList<MicDuckModeChoice> MicDuckModeChoices { get; } =
    [
        new(MicDuckMode.Off, "Off"),
        new(MicDuckMode.Mute, "Mute"),
        new(MicDuckMode.Attenuate, "Turn down"),
    ];

    public MicDuckMode MicDuckMode
    {
        get => _micDuckMode;
        set
        {
            if (!SetField(ref _micDuckMode, value))
                return;

            _engine.UpdateSettings(s => s with { MicDuckMode = value });
            OnPropertyChanged(nameof(IsMicAttenuationEnabled));
        }
    }

    public bool IsMicAttenuationEnabled => MicDuckMode == MicDuckMode.Attenuate;

    public int MicAttenuationPercent
    {
        get => _micAttenuationPercent;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (!SetField(ref _micAttenuationPercent, clamped))
                return;

            _engine.UpdateSettings(s => s with { MicAttenuationPercent = clamped });
        }
    }

    public IReadOnlyList<MicDeviceChoice> MicDeviceChoices
    {
        get => _micDeviceChoices;
        private set => SetField(ref _micDeviceChoices, value);
    }

    public string SelectedMicDeviceId
    {
        get => _selectedMicDeviceId;
        set
        {
            if (value is null || !SetField(ref _selectedMicDeviceId, value))
                return; // WPF writes null while the choices list is being replaced

            _engine.UpdateSettings(s => s with { MicDeviceId = NullIfEmpty(value) });
            Diagnostics.RequestCheck();
        }
    }

    public bool ReplayBufferEnabled
    {
        get => _replayBufferEnabled;
        set
        {
            if (!SetField(ref _replayBufferEnabled, value))
                return;

            _engine.UpdateSettings(s => s with { ReplayBufferEnabled = value });
        }
    }

    public int ReplayBufferSeconds
    {
        get => _replayBufferSeconds;
        set
        {
            var clamped = Math.Clamp(value, AppSettings.MinReplayBufferSeconds, AppSettings.MaxReplayBufferSeconds);
            if (!SetField(ref _replayBufferSeconds, clamped))
                return;

            _engine.UpdateSettings(s => s with { ReplayBufferSeconds = clamped });
        }
    }

    public string GrabHotkeyDisplay => $"{ChordKeyDisplay}+Enter";

    public string LiteLlmBaseUrl
    {
        get => _liteLlmBaseUrl;
        set
        {
            if (!SetField(ref _liteLlmBaseUrl, value))
                return;

            _engine.UpdateSettings(s => s with { LiteLlmBaseUrl = NullIfEmpty(value) });
        }
    }

    public string LiteLlmTranscriptionModel
    {
        get => _liteLlmTranscriptionModel;
        set
        {
            if (!SetField(ref _liteLlmTranscriptionModel, value))
                return;

            _engine.UpdateSettings(s => s with { LiteLlmTranscriptionModel = value });
        }
    }

    public string LiteLlmChatModel
    {
        get => _liteLlmChatModel;
        set
        {
            if (!SetField(ref _liteLlmChatModel, value))
                return;

            _engine.UpdateSettings(s => s with { LiteLlmChatModel = value });
        }
    }

    public string LiteLlmApiKeyPlaceholder => _hasSavedLiteLlmApiKey ? "(saved — type to replace)" : "(not set)";

    public bool CanTestLiteLlmConnection => !_isTestingLiteLlmConnection;

    public string LiteLlmTestConnectionLabel => _isTestingLiteLlmConnection ? "Testing…" : "Test connection";

    public string LiteLlmConnectionTestResult
    {
        get => _liteLlmConnectionTestResult;
        private set => SetField(ref _liteLlmConnectionTestResult, value);
    }

    /// <summary>Called from the password box's PasswordChanged handler; never bound, so the plaintext never round-trips through XAML.</summary>
    public void SetLiteLlmApiKey(string plaintext)
    {
        var encrypted = string.IsNullOrEmpty(plaintext) ? null : _protector.Protect(plaintext);
        _engine.UpdateSettings(s => s with { LiteLlmApiKeyEncrypted = encrypted });
        _hasSavedLiteLlmApiKey = encrypted is not null;
        OnPropertyChanged(nameof(LiteLlmApiKeyPlaceholder));
    }

    public async Task TestLiteLlmConnectionAsync()
    {
        if (_isTestingLiteLlmConnection)
            return;

        await _engine.FlushAsync(); // the base URL box commits on lost focus, i.e. on this very click
        var (options, warning) = LiteLlmOptionsResolver.Resolve(_engine.Settings, _protector);
        if (warning is not null)
            Status = warning;

        if (options is null)
        {
            LiteLlmConnectionTestResult = "Set a base URL first.";
            return;
        }

        _isTestingLiteLlmConnection = true;
        OnPropertyChanged(nameof(CanTestLiteLlmConnection));
        OnPropertyChanged(nameof(LiteLlmTestConnectionLabel));
        try
        {
            var namer = new LiteLlmClipNamer(_httpClient, options);
            var result = await namer.TestConnectionAsync(CancellationToken.None);
            LiteLlmConnectionTestResult = result.Message;
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException or UriFormatException)
        {
            LiteLlmConnectionTestResult = $"That base URL isn't usable: {e.Message}";
        }
        finally
        {
            _isTestingLiteLlmConnection = false;
            OnPropertyChanged(nameof(CanTestLiteLlmConnection));
            OnPropertyChanged(nameof(LiteLlmTestConnectionLabel));
        }
    }

    /// <summary>Reads the grab off the UI thread and asks the view to open the clip editor for it.</summary>
    public async Task OpenGrabAsync(PendingGrab grab)
    {
        if (_library is not { } library)
        {
            Status = "Choose a sound library folder first.";
            return;
        }

        if (!_openGrabIds.Add(grab.Id))
        {
            EditorBringToFrontRequested?.Invoke(grab);
            return;
        }

        EditableAudio audio;
        try
        {
            audio = await Task.Run(() => LoadGrabAudio(grab));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            _openGrabIds.Remove(grab.Id);
            Status = $"Couldn't open the grab: {e.Message}";
            return;
        }

        var options = new ClipEditorOptions
        {
            LibraryRoot = library.Root,
            InitialFolder = library.Browser.CurrentPath,
            SuggestName = BuildSuggestName(),
            PlaySampleOnDrag = _engine.Settings.EditorPlaySampleOnDrag,
            PlaySampleOnDragChanged = value => _engine.UpdateSettings(s => s with { EditorPlaySampleOnDrag = value }),
        };

        var editorViewModel = new ClipEditorViewModel(audio, _preview, _encoder, options);
        editorViewModel.ClipSaved += _ =>
        {
            // A save can finish after its editor closed; counting it then would leak into the grab's next editor.
            if (_openEditors.GetValueOrDefault(grab.Id) == editorViewModel)
                _savedGrabCounts[grab.Id] = _savedGrabCounts.GetValueOrDefault(grab.Id) + 1;
            RefreshLibraryView();
        };
        _openEditors[grab.Id] = editorViewModel;

        EditorRequested?.Invoke(new GrabEditorRequest(editorViewModel, grab));
    }

    /// <summary>Call when the clip editor for <paramref name="grab"/> closes, to apply the keep/delete rule.</summary>
    public void OnGrabEditorClosed(PendingGrab grab, ClipEditorOutcome outcome)
    {
        _openGrabIds.Remove(grab.Id);
        _openEditors.Remove(grab.Id);
        var savedCount = _savedGrabCounts.Remove(grab.Id, out var count) ? count : 0;
        if (!PendingGrabCleanupRule.ShouldDelete(outcome == ClipEditorOutcome.Discarded, savedCount))
            return;

        try
        {
            _grabStore.Delete(grab);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Status = $"Couldn't delete the grab: {e.Message}";
        }
    }

    /// <summary>Waits for the next key press and, if valid, makes it the chord key.</summary>
    public Task ChangeChordKeyAsync() => CaptureKeyAsync("the chord key", key =>
    {
        if (_hook.PushToTalkBinding.ValidateChordKey(key) is { } error)
        {
            Status = error;
            return;
        }

        _hook.ChordKey = key;
        _engine.UpdateSettings(s => s with { ChordKey = key });
        OnPropertyChanged(nameof(ChordKeyDisplay));
        OnPropertyChanged(nameof(GrabHotkeyDisplay));
        Status = $"Chord key changed to {key}.";
    });

    /// <summary>Waits for the next key press and, if valid, makes it the push-to-talk binding.</summary>
    public Task ChangePushToTalkKeyAsync() => CaptureKeyAsync("push-to-talk", key =>
        TryApplyPushToTalkBinding(PushToTalkBinding.FromKey(key)));

    /// <summary>Cancels an in-progress key capture, e.g. because the window lost focus.</summary>
    public void CancelKeyCapture() => _captureCts?.Cancel();

    private async Task CaptureKeyAsync(string subject, Action<ScanKey> onCaptured)
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
            Status = $"Hotkeys aren't running, so {subject} can't be changed.";
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

        onCaptured(captured.Value);
    }

    private void TryApplyPushToTalkBinding(PushToTalkBinding binding)
    {
        if (binding.Validate(_hook.ChordKey) is { } error)
        {
            Status = error;
            return;
        }

        _hook.PushToTalkBinding = binding;
        _engine.UpdateSettings(s => s with { PushToTalkBinding = binding });
        OnPropertyChanged(nameof(PushToTalkBindingDisplay));
        OnPropertyChanged(nameof(SelectedPushToTalkMouseButton));
        Status = $"Push-to-talk changed to {binding}.";
    }

    public void ChooseLibrary(string root)
    {
        _engine.UpdateSettings(s => s with { LibraryRoot = root });
        OpenLibrary(root);
    }

    public void Open(LibraryItem? item)
    {
        if (_library is null || item is null)
            return;

        if (_library.Browser.Open(item.Entry) is { } clipPath)
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
        _library?.Browser.GoUp();
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

        MicDeviceChoices = MicDeviceMenu.Build(
            _captureDevices.GetCaptureDevices(),
            _captureDevices.GetDefaultCaptureDeviceId(),
            _captureDevices.GetDefaultCommunicationsCaptureDeviceId());
        OnPropertyChanged(nameof(SelectedMicDeviceId));

        _library?.Browser.Refresh();
        ShowEntries();
    }

    /// <summary>The full path of the folder currently shown on the Library tab, or null with no library chosen.</summary>
    private string? LibraryFolderFullPath =>
        _library is null ? null : LibraryPathResolver.FullPath(_library.Root, _library.Browser.CurrentPath);

    public bool CanOpenInExplorer => LibraryFolderFullPath is not null;

    /// <summary>Opens the folder currently shown on the Library tab in Windows Explorer.</summary>
    public void OpenCurrentFolderInExplorer()
    {
        if (LibraryFolderFullPath is not { } path)
            return;

        if (!Directory.Exists(path))
        {
            RefreshLibraryView();
            Status = "That folder no longer exists.";
            return;
        }

        try
        {
            // Shell-opening the folder itself sidesteps explorer.exe's own argument parsing (commas, "D:\" roots).
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Status = $"Couldn't open the folder: {e.Message}";
        }
    }

    public void Dispose() => _libraryWatcher?.Dispose();

    private void OpenLibrary(string? root)
    {
        _libraryWatcher?.Dispose();
        _libraryWatcher = null;
        _library = string.IsNullOrEmpty(root)
            ? null
            : new OpenedLibrary(root, new LibraryBrowser(new FileSystemFolderSource(root)));
        LibraryRoot = string.IsNullOrEmpty(root) ? "(no sound library chosen)" : root;
        if (!string.IsNullOrEmpty(root))
        {
            _libraryWatcher = new LibraryWatcher(root, _time);
            _libraryWatcher.Changed += () => _ui.Post(_ => OnLibraryChanged(), null);
        }

        ShowEntries();
    }

    // Runs on the UI thread: the watcher raises its event off-thread, but the handler above posts here first.
    // Refreshes the Library tab's listing (falling back to the nearest existing ancestor) and every open editor's folder list.
    private void OnLibraryChanged()
    {
        RefreshLibraryView();
        Favorites.Refresh();
        foreach (var editor in _openEditors.Values)
            editor.RefreshFolders();
    }

    private void ShowEntries()
    {
        var entries = _library?.Browser.Entries.Select(e => new LibraryItem(e)).ToList() ?? [];
        if (!entries.SequenceEqual(_entries)) // unchanged after a live refresh: keep the list's focus and scroll position
            Entries = entries;
        LibraryWarning = OverlayCapacity.TruncationWarning(_library?.Browser.Entries.Count ?? 0) ?? "";
        OnPropertyChanged(nameof(CurrentPath));
        OnPropertyChanged(nameof(CanGoUp));
        OnPropertyChanged(nameof(LibraryFolderFullPath));
        OnPropertyChanged(nameof(CanOpenInExplorer));
    }

    private void RefreshLibraryView()
    {
        _library?.Browser.Refresh();
        ShowEntries();
    }

    private static EditableAudio LoadGrabAudio(PendingGrab grab)
    {
        using var stream = new FileStream(grab.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var wav = WavFile.ReadFloat32(stream);
        return new EditableAudio(wav.Samples, wav.SampleRate, wav.Channels, sourceLabel: $"Grab at {grab.GrabbedAt.ToLocalTime():HH:mm}");
    }

    /// <summary>Null when LiteLLM naming isn't configured; otherwise resolves settings fresh on every call, so a
    /// mid-session settings change takes effect without reopening the editor.</summary>
    private Func<EditableAudio, CancellationToken, Task<string?>>? BuildSuggestName()
    {
        var (options, _) = LiteLlmOptionsResolver.Resolve(_engine.Settings, _protector);
        if (options is null)
            return null;

        return async (audio, ct) =>
        {
            var (resolved, warning) = LiteLlmOptionsResolver.Resolve(_engine.Settings, _protector);
            if (warning is not null)
                Status = warning;
            if (resolved is null)
                return null;

            var namer = new LiteLlmClipNamer(_httpClient, resolved);
            var suggestion = await namer.SuggestAsync(audio.Samples, audio.SampleRate, audio.Channels, ct);
            if (suggestion?.Name is { } name)
                return name;

            throw new InvalidOperationException(suggestion?.ErrorMessage ?? "No name suggested.");
        };
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

    // The engine applies settings asynchronously, so the root is kept here rather than read back from it.
    private sealed record OpenedLibrary(string Root, LibraryBrowser Browser);
}
