using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using QuickParrot.App.Editor;
using QuickParrot.App.Library;
using QuickParrot.Audio;
using QuickParrot.Core.Devices;
using QuickParrot.Core.Diagnostics;
using QuickParrot.Core.Engine;
using QuickParrot.Core.Favorites;
using QuickParrot.Core.Grabs;
using QuickParrot.Core.Library;
using QuickParrot.Core.Mic;
using QuickParrot.Core.Naming;
using QuickParrot.Core.Replay;
using QuickParrot.Core.Settings;
using QuickParrot.Input;
using QuickParrot.Overlay;

namespace QuickParrot.App;

public partial class App : System.Windows.Application
{
    private static readonly TimeSpan CrashCleanupTimeout = TimeSpan.FromSeconds(2);

    private QuickParrotEngine? _engine;
    private NAudioClipPlayer? _player;
    private WindowsAudioDeviceCatalog? _devices;
    private LowLevelKeyboardHook? _hook;
    private MicDucker? _micDucker;
    private OverlayHost? _overlay;
    private AudioDiagnostics? _diagnostics;
    private ReplayBuffer? _replayBuffer;
    private FilePendingGrabStore? _grabStore;
    private LoopbackReplayCapture? _replayCapture;
    private EditorPreview? _editorPreview;
    private HttpClient? _httpClient;
    private LibraryViewModel? _library;
    private Action<AppSettings>? _applySettings;
    private int _crashCleanupStarted;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        if (RepairCommandLine.IsRepair(e.Args))
        {
            // The elevated one-shot helper: no UI, hooks or engine, just the repair and its exit code.
            Environment.ExitCode = ElevatedListenRepair.Run(e.Args);
            Shutdown(Environment.ExitCode);
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Trace.WriteLine($"QuickParrot: unhandled exception: {args.ExceptionObject}");
            if (args.IsTerminating)
                CleanUpAfterCrash();
        };
        DispatcherUnhandledException += (_, _) => CleanUpAfterCrash(); // left unhandled, so it still crashes
        TaskScheduler.UnobservedTaskException += (_, args) =>
            Trace.WriteLine($"QuickParrot: unobserved task exception: {args.Exception}");

        var store = new JsonSettingsStore(JsonSettingsStore.DefaultPath);
        var loaded = store.Load();
        _devices = new WindowsAudioDeviceCatalog();
        _player = new NAudioClipPlayer(_devices);
        _player.Configure(loaded.Settings.ToOutputSettings()); // so warm-up opens the configured devices
        _ = _player.WarmUpAsync();

        var warnings = new List<string>();
        if (loaded.Warning is { } loadWarning)
            warnings.Add(loadWarning);

        // Created before the engine so hotkeys are live as early as possible; the lambda reaches the engine
        // once it exists below.
        var hook = new LowLevelKeyboardHook(loaded.Settings.ChordKey, chordEvent => _engine?.Post(chordEvent));
        _hook = hook;
        ApplyHookSettings(hook, loaded.Settings);
        try
        {
            hook.Start();
        }
        catch (Win32Exception ex)
        {
            warnings.Add($"Couldn't enable hotkeys or push-to-talk: {ex.Message}");
        }

        var micDucker = new MicDucker(new WindowsMicVolumeControl(), _devices, new JsonMicRestoreStore(JsonMicRestoreStore.DefaultPath));
        if (micDucker.RestoreAfterCrash() is { } restoreWarning)
            warnings.Add(restoreWarning);
        _micDucker = micDucker;

        _replayBuffer = new ReplayBuffer(TimeSpan.FromSeconds(loaded.Settings.ReplayBufferSeconds));
        _grabStore = new FilePendingGrabStore(FilePendingGrabStore.DefaultDirectory);

        var engine = new QuickParrotEngine(
            _player,
            hook.PushToTalk,
            micDucker,
            store,
            loaded.Settings,
            TimeProvider.System,
            OpenFolderSource,
            _replayBuffer,
            _grabStore);
        _engine = engine;

        var replayCapture = new LoopbackReplayCapture(_replayBuffer);
        _replayCapture = replayCapture;
        replayCapture.Configure(loaded.Settings.ReplayBufferEnabled, loaded.Settings.MonitorDeviceId);

        var diagnostics = new AudioDiagnostics(
            new WindowsAudioSetupReader(() => engine.Settings.ToConfiguredDevices(), () => micDucker.HasPendingRestore),
            new AudioSetupRepairer(new WindowsAudioSystemWriter(Environment.ProcessPath ?? "")),
            TimeProvider.System);
        _diagnostics = diagnostics;

        var settings = new SettingsMirror(engine.Settings, engine.UpdateSettings, PostToUi);
        var applied = engine.Settings;
        // The one place settings reach the hook, replay capture and diagnostics. Raised on the engine thread, and
        // subscribed before it starts so no change is missed.
        _applySettings = updated =>
        {
            ApplyHookSettings(hook, updated);
            if ((updated.ReplayBufferEnabled, updated.MonitorDeviceId) != (applied.ReplayBufferEnabled, applied.MonitorDeviceId))
                replayCapture.Configure(updated.ReplayBufferEnabled, updated.MonitorDeviceId);
            if (updated.ToConfiguredDevices() != applied.ToConfiguredDevices())
                diagnostics.RequestCheck();
            applied = updated;
            PostToUi(() => settings.Receive(updated));
        };
        engine.SettingsChanged += _applySettings;

        var overlay = new OverlayHost();
        _overlay = overlay;
        try
        {
            overlay.Start();
        }
        catch (Exception ex)
        {
            warnings.Add($"Couldn't start the overlay: {ex.Message}");
        }

        engine.ViewStateChanged += overlay.Show;
        engine.Start();
        _devices.DevicesChanged += engine.RetryMicRestore;
        _devices.SetupChanged += diagnostics.RequestCheck;

        var clipEncoder = new ClipEncoder();
        _editorPreview = new EditorPreview(_devices, () => engine.Settings.ToOutputSettings());
        var httpClient = new HttpClient();
        _httpClient = httpClient;
        var protector = new DpapiProtector();

        var status = new StatusViewModel();
        if (warnings.Count > 0)
            status.Report(string.Join(" ", warnings));

        // All raised on their own threads.
        overlay.ErrorOccurred += message => PostToUi(() => status.Report(message));
        hook.ErrorOccurred += message => PostToUi(() => status.Report(message));
        micDucker.Warning += message => PostToUi(() => status.Report(message));
        replayCapture.ErrorOccurred += message => PostToUi(() => status.Report(message));
        engine.ErrorOccurred += message => PostToUi(() => status.Report(message));
        engine.FavoritesNotice += notice => PostToUi(() => status.Report(notice.Message));

        // All safe to call from any thread; the status line update for a failed grab rides along on ErrorOccurred.
        engine.GrabSaved += grab => overlay.ShowToast(ReplayGrabber.SavedMessage(grab), TimeSpan.FromSeconds(1.5));
        engine.GrabFailed += message => overlay.ShowToast(message, TimeSpan.FromSeconds(1.5), isError: true);
        engine.FavoritesNotice += notice => overlay.ShowToast(notice.Message, TimeSpan.FromSeconds(1.5), notice.IsError);

        var devices = _devices;
        var testRunner = new CableTestRunner(
            () => LoopbackTestSetup.Resolve(
                devices.GetRenderDevices(), devices.GetCaptureDevices(), settings.Current.CableDeviceId,
                settings.Current.MonitorDeviceId, devices.GetDefaultRenderDeviceId()),
            engine.SuppressPlayback,
            engine.Stop);
        var loopbackTest = new LoopbackTestViewModel(new LoopbackTester(), testRunner, diagnostics);
        var clipTest = new ClipCableTestViewModel(
            new ClipCableTest(new ClipCableAudio()), testRunner, engine, settings, OpenFolderSource);
        var diagnosticsViewModel = new DiagnosticsViewModel(diagnostics, loopbackTest, clipTest, PostToUi);
        // One-way: the tests' readiness depends on the devices diagnostics already watches, but they never
        // reference DiagnosticsViewModel back.
        diagnosticsViewModel.ReportChanged += _ => testRunner.Refresh();
        diagnosticsViewModel.ReportChanged += status.ShowDiagnostics;

        _library = new LibraryViewModel(settings, engine, status, OpenFolderSource, WatchLibrary, OpenInExplorer, PostToUi);
        var editorServices = new GrabEditorServices(
            _grabStore,
            _editorPreview,
            clipEncoder,
            OpenFolderSource,
            () => LiteLlmNaming.CreateSuggester(() => settings.Current, protector, httpClient, status.Report),
            editor => ClipEditorWindow.Open(editor, MainWindow));
        var viewModel = new MainViewModel(
            status,
            _library,
            new PendingGrabsViewModel(_grabStore, engine, settings, status, PostToUi),
            new FavoritesViewModel(engine, settings, OpenFolderSource),
            new SettingsViewModel(settings),
            new DeviceSettingsViewModel(settings, _devices, _devices),
            new HotkeysViewModel(settings, status, hook.CaptureNextKeyAsync),
            new LiteLlmSettingsViewModel(settings, status, httpClient, protector),
            diagnosticsViewModel,
            new GrabEditorCoordinator(settings, _library, status, editorServices));

        MainWindow = new MainWindow(viewModel);
        MainWindow.Show();
        _ = diagnostics.CheckNowAsync();
    }

    // Runs actions on the UI thread in the order posted, which SettingsMirror relies on.
    private void PostToUi(Action action) => Dispatcher.BeginInvoke(action);

    private static IFolderSource OpenFolderSource(string root) => new FileSystemFolderSource(root);

    private static IDisposable WatchLibrary(string root, Action changed)
    {
        var watcher = new LibraryWatcher(root, TimeProvider.System);
        watcher.Changed += changed;
        return watcher;
    }

    // Shell-opening the folder itself sidesteps explorer.exe's own argument parsing (commas, "D:\" roots).
    private static void OpenInExplorer(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();

    // Thread-safe, and settings are already sanitized, so none of these throw.
    private static void ApplyHookSettings(LowLevelKeyboardHook hook, AppSettings settings)
    {
        if (hook.ChordKey != settings.ChordKey)
            hook.ChordKey = settings.ChordKey;
        if (hook.SaveNavigationKey != settings.SaveNavigationKey)
            hook.SaveNavigationKey = settings.SaveNavigationKey;
        if (hook.SearchKey != settings.SearchKey)
            hook.SearchKey = settings.SearchKey;
        hook.SetSearchLibrary(settings.LibraryRoot);
        if (hook.PushToTalkBinding != settings.PushToTalkBinding)
            hook.PushToTalkBinding = settings.PushToTalkBinding;
        if (hook.Enabled != settings.HotkeysEnabled)
            hook.Enabled = settings.HotkeysEnabled;
        hook.SetChordlessFavoriteSlots(FavoriteStatus.ChordlessMask(settings.Favorites, settings.FavoritesWithoutChord));
    }

    // Runs on a fresh thread with a deadline, since the crashed state may have the engine or hook threads stuck.
    // A hard kill skips this; the next startup restores the mic then.
    private void CleanUpAfterCrash()
    {
        if (Interlocked.Exchange(ref _crashCleanupStarted, 1) != 0)
            return;

        var pushToTalk = _hook?.PushToTalk;
        var micDucker = _micDucker;
        var cleanup = new Thread(() =>
        {
            pushToTalk?.EmergencyRelease();
            micDucker?.EmergencyRestore(TimeSpan.FromMilliseconds(500));
        })
        {
            IsBackground = true,
            Name = "QuickParrot crash cleanup",
        };
        try
        {
            cleanup.Start();
            cleanup.Join(CrashCleanupTimeout);
        }
        catch (Exception cleanupError)
        {
            Trace.WriteLine($"QuickParrot: crash cleanup failed: {cleanupError.Message}");
        }
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        // Order matters: hook disabled, engine (releases push-to-talk, restores the mic), hook, overlay, then the
        // audio users (replay capture, editor preview, player) before the devices themselves. Settings stop
        // reaching the hook first, so a change the engine applies while shutting down can't re-enable it.
        if (_engine is not null && _applySettings is not null)
            _engine.SettingsChanged -= _applySettings;
        if (_hook is not null)
            _hook.Enabled = false;
        if (_devices is not null && _engine is not null)
            _devices.DevicesChanged -= _engine.RetryMicRestore;
        if (_devices is not null && _diagnostics is not null)
            _devices.SetupChanged -= _diagnostics.RequestCheck;
        _diagnostics?.Dispose();
        _library?.Dispose();
        _engine?.Dispose();
        _hook?.Dispose();
        if (_engine is not null && _overlay is not null)
            _engine.ViewStateChanged -= _overlay.Show;
        _overlay?.Dispose();
        _replayCapture?.Dispose();
        _editorPreview?.Dispose();
        _player?.Dispose();
        _devices?.Dispose();
        _httpClient?.Dispose();
        base.OnExit(e);
    }
}
