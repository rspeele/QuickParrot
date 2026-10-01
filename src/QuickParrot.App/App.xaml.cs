using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using QuickParrot.Audio;
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
    private ClipEncoder? _clipEncoder;
    private EditorPreview? _editorPreview;
    private HttpClient? _httpClient;
    private DpapiProtector? _dpapiProtector;
    private MainViewModel? _viewModel;
    private (bool Enabled, string? MonitorDeviceId)? _lastReplayCaptureConfig;
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
        _hook = new LowLevelKeyboardHook(loaded.Settings.ChordKey, chordEvent => _engine?.Post(chordEvent))
        {
            Enabled = loaded.Settings.HotkeysEnabled,
        };
        _hook.PushToTalkBinding = loaded.Settings.PushToTalkBinding;
        _hook.SetChordlessFavoriteSlots(
            FavoriteStatus.ChordlessMask(loaded.Settings.Favorites, loaded.Settings.FavoritesWithoutChord));
        try
        {
            _hook.Start();
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

        _engine = new QuickParrotEngine(
            _player,
            _hook.PushToTalk,
            micDucker,
            store,
            loaded.Settings,
            TimeProvider.System,
            root => new FileSystemFolderSource(root),
            _replayBuffer,
            _grabStore);
        _engine.Start();
        _devices.DevicesChanged += _engine.RetryMicRestore;

        _replayCapture = new LoopbackReplayCapture(_replayBuffer);
        _lastReplayCaptureConfig = (loaded.Settings.ReplayBufferEnabled, loaded.Settings.MonitorDeviceId);
        _replayCapture.Configure(loaded.Settings.ReplayBufferEnabled, loaded.Settings.MonitorDeviceId);
        var hook = _hook;
        _engine.SettingsChanged += settings =>
        {
            hook.SetChordlessFavoriteSlots(FavoriteStatus.ChordlessMask(settings.Favorites, settings.FavoritesWithoutChord));
            (bool Enabled, string? MonitorDeviceId) config = (settings.ReplayBufferEnabled, settings.MonitorDeviceId);
            if (config == _lastReplayCaptureConfig)
                return;

            _lastReplayCaptureConfig = config;
            _replayCapture?.Configure(config.Enabled, config.MonitorDeviceId);
        };

        _overlay = new OverlayHost();
        try
        {
            _overlay.Start();
        }
        catch (Exception ex)
        {
            warnings.Add($"Couldn't start the overlay: {ex.Message}");
        }

        _engine.ViewStateChanged += _overlay.Show;

        var engine = _engine;
        _diagnostics = new AudioDiagnostics(
            new WindowsAudioSetupReader(() => engine.Settings.ToConfiguredDevices(), () => micDucker.HasPendingRestore),
            new AudioSetupRepairer(new WindowsAudioSystemWriter(Environment.ProcessPath ?? "")),
            TimeProvider.System);
        _devices.SetupChanged += _diagnostics.RequestCheck;

        _clipEncoder = new ClipEncoder();
        _editorPreview = new EditorPreview(_devices, () => engine.Settings.ToOutputSettings());
        _httpClient = new HttpClient();
        _dpapiProtector = new DpapiProtector();

        var loopbackTest = new LoopbackTestViewModel(new LoopbackTester(), _devices, _devices, _engine, _diagnostics);
        var diagnosticsViewModel = new DiagnosticsViewModel(_diagnostics, loopbackTest);
        // One-way: the setup test's readiness depends on the devices diagnostics already watches, but it never
        // references DiagnosticsViewModel back.
        diagnosticsViewModel.ReportChanged += _ => loopbackTest.Refresh();

        var viewModel = new MainViewModel(
            _engine,
            _devices,
            _devices,
            _hook,
            _grabStore,
            _editorPreview,
            _clipEncoder,
            _httpClient,
            _dpapiProtector,
            diagnosticsViewModel,
            warnings.Count == 0 ? null : string.Join(" ", warnings));
        _viewModel = viewModel;

        // All raise these on their own thread, so they must be marshalled onto the UI thread.
        _overlay.ErrorOccurred += message =>
            Dispatcher.BeginInvoke(() => viewModel.Status = message);
        _hook.ErrorOccurred += message =>
            Dispatcher.BeginInvoke(() => viewModel.Status = message);
        micDucker.Warning += message =>
            Dispatcher.BeginInvoke(() => viewModel.Status = message);
        _replayCapture.ErrorOccurred += message =>
            Dispatcher.BeginInvoke(() => viewModel.Status = message);

        // Both are safe to call from any thread; the status line update for a failed grab rides along on ErrorOccurred.
        _engine.GrabSaved += grab => _overlay.ShowToast(ReplayGrabber.SavedMessage(grab), TimeSpan.FromSeconds(1.5));
        _engine.GrabFailed += message => _overlay.ShowToast(message, TimeSpan.FromSeconds(1.5), isError: true);
        _engine.FavoritesNotice += notice => _overlay.ShowToast(notice.Message, TimeSpan.FromSeconds(1.5), notice.IsError);

        MainWindow = new MainWindow(viewModel);
        MainWindow.Show();
        _ = _diagnostics.CheckNowAsync();
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
        // audio users (replay capture, editor preview, player) before the devices themselves.
        if (_hook is not null)
            _hook.Enabled = false;
        if (_devices is not null && _engine is not null)
            _devices.DevicesChanged -= _engine.RetryMicRestore;
        if (_devices is not null && _diagnostics is not null)
            _devices.SetupChanged -= _diagnostics.RequestCheck;
        _diagnostics?.Dispose();
        _viewModel?.Dispose();
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
