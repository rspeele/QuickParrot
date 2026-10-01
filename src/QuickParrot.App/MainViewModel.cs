using System.IO;
using System.Net.Http;
using QuickParrot.App.Editor;
using QuickParrot.Core.Editing;
using QuickParrot.Core.Grabs;
using QuickParrot.Core.Naming;
using QuickParrot.Core.Settings;

namespace QuickParrot.App;

/// <summary>Asks the host window to show the clip editor for a just-opened grab.</summary>
public sealed record GrabEditorRequest(ClipEditorViewModel ViewModel, PendingGrab Grab);

/// <summary>What opening a grab in the clip editor needs beyond the library and settings.</summary>
public sealed record GrabEditorServices(
    IPendingGrabStore GrabStore, IEditorPreview Preview, IClipEncoder Encoder, HttpClient HttpClient, IDpapiProtector Protector);

/// <summary>The main window: hosts each tab's view model and opens grabs in the clip editor.</summary>
public sealed class MainViewModel
{
    private readonly SettingsMirror _settings;
    private readonly IPendingGrabStore _grabStore;
    private readonly IEditorPreview _preview;
    private readonly IClipEncoder _encoder;
    private readonly HttpClient _httpClient;
    private readonly IDpapiProtector _protector;
    private readonly HashSet<string> _openGrabIds = [];
    private readonly Dictionary<string, int> _savedGrabCounts = [];
    private readonly Dictionary<string, ClipEditorViewModel> _openEditors = [];

    public MainViewModel(
        SettingsMirror settings,
        StatusViewModel status,
        LibraryViewModel library,
        PendingGrabsViewModel pendingGrabs,
        FavoritesViewModel favorites,
        SettingsViewModel general,
        DeviceSettingsViewModel devices,
        HotkeysViewModel hotkeys,
        LiteLlmSettingsViewModel liteLlm,
        DiagnosticsViewModel diagnostics,
        GrabEditorServices editorServices)
    {
        _settings = settings;
        _grabStore = editorServices.GrabStore;
        _preview = editorServices.Preview;
        _encoder = editorServices.Encoder;
        _httpClient = editorServices.HttpClient;
        _protector = editorServices.Protector;
        Status = status;
        Library = library;
        PendingGrabs = pendingGrabs;
        Favorites = favorites;
        Settings = general;
        Devices = devices;
        Hotkeys = hotkeys;
        LiteLlm = liteLlm;
        Diagnostics = diagnostics;
        Library.ContentsChanged += OnLibraryContentsChanged;
    }

    public StatusViewModel Status { get; }

    public LibraryViewModel Library { get; }

    public PendingGrabsViewModel PendingGrabs { get; }

    public FavoritesViewModel Favorites { get; }

    public SettingsViewModel Settings { get; }

    public DeviceSettingsViewModel Devices { get; }

    public HotkeysViewModel Hotkeys { get; }

    public LiteLlmSettingsViewModel LiteLlm { get; }

    public DiagnosticsViewModel Diagnostics { get; }

    /// <summary>Raised when a grab is ready to edit; the view hosts <see cref="ClipEditorWindow"/> for it.</summary>
    public event Action<GrabEditorRequest>? EditorRequested;

    /// <summary>Raised when a grab that's already open is requested again; the view brings its window to the front.</summary>
    public event Action<PendingGrab>? EditorBringToFrontRequested;

    /// <summary>Reads the grab off the UI thread and asks the view to open the clip editor for it.</summary>
    public async Task OpenGrabAsync(PendingGrab grab)
    {
        if (Library.Root is not { } libraryRoot)
        {
            Status.Report("Choose a sound library folder first.");
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
            Status.Report($"Couldn't open the grab: {e.Message}");
            return;
        }

        var options = new ClipEditorOptions
        {
            LibraryRoot = libraryRoot,
            InitialFolder = Library.Folder,
            SuggestName = BuildSuggestName(),
            PlaySampleOnDrag = _settings.Current.EditorPlaySampleOnDrag,
            PlaySampleOnDragChanged = value => _settings.Update(s => s with { EditorPlaySampleOnDrag = value }),
        };

        var editorViewModel = new ClipEditorViewModel(audio, _preview, _encoder, options);
        editorViewModel.ClipSaved += _ =>
        {
            // A save can finish after its editor closed; counting it then would leak into the grab's next editor.
            if (_openEditors.GetValueOrDefault(grab.Id) == editorViewModel)
                _savedGrabCounts[grab.Id] = _savedGrabCounts.GetValueOrDefault(grab.Id) + 1;
            Library.Refresh();
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
            Status.Report($"Couldn't delete the grab: {e.Message}");
        }
    }

    // Files changed on disk: the favorites' missing marks and every open editor's folder list may be stale.
    private void OnLibraryContentsChanged()
    {
        Favorites.Refresh();
        foreach (var editor in _openEditors.Values)
            editor.RefreshFolders();
    }

    private static EditableAudio LoadGrabAudio(PendingGrab grab)
    {
        using var stream = new FileStream(grab.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var wav = WavFile.ReadFloat32(stream);
        return new EditableAudio(wav.Samples, wav.SampleRate, wav.Channels, sourceLabel: $"Grab at {grab.GrabbedAt.ToLocalTime():HH:mm}");
    }

    /// <summary>Null when LiteLLM naming isn't configured; otherwise resolves settings fresh on every call, so a
    /// mid-session settings change takes effect without reopening the editor.</summary>
    private Func<EditableAudio, CancellationToken, Task<NameSuggestion>>? BuildSuggestName()
    {
        var (options, _) = LiteLlmOptionsResolver.Resolve(_settings.Current, _protector);
        if (options is null)
            return null;

        return async (audio, ct) =>
        {
            var (resolved, warning) = LiteLlmOptionsResolver.Resolve(_settings.Current, _protector);
            if (warning is not null)
                Status.Report(warning);
            if (resolved is null)
                return new NameSuggestion(null);

            var namer = new LiteLlmClipNamer(_httpClient, resolved);
            return await namer.SuggestAsync(audio.Samples, audio.SampleRate, audio.Channels, ct);
        };
    }
}
