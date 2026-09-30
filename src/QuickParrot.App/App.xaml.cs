using System.ComponentModel;
using System.Diagnostics;
using QuickParrot.Audio;
using QuickParrot.Core.Engine;
using QuickParrot.Core.Library;
using QuickParrot.Core.Mic;
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
    private int _crashCleanupStarted;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
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
        _hook.PushToTalk.Binding = loaded.Settings.PushToTalkBinding;
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

        _engine = new QuickParrotEngine(
            _player,
            _hook.PushToTalk,
            micDucker,
            store,
            loaded.Settings,
            TimeProvider.System,
            root => new FileSystemFolderSource(root));
        _engine.Start();
        _devices.DevicesChanged += _engine.RetryMicRestore;

        _overlay = new OverlayHost { SmallFolderLayout = loaded.Settings.SmallFolderLayout };
        try
        {
            _overlay.Start();
        }
        catch (Exception ex)
        {
            warnings.Add($"Couldn't start the overlay: {ex.Message}");
        }

        _engine.ViewStateChanged += _overlay.Show;

        var viewModel = new MainViewModel(
            _engine, _devices, _devices, _hook, _overlay, warnings.Count == 0 ? null : string.Join(" ", warnings));

        // All raise these on their own thread, so they must be marshalled onto the UI thread.
        _overlay.ErrorOccurred += message =>
            Dispatcher.BeginInvoke(() => viewModel.Status = message);
        _hook.ErrorOccurred += message =>
            Dispatcher.BeginInvoke(() => viewModel.Status = message);
        micDucker.Warning += message =>
            Dispatcher.BeginInvoke(() => viewModel.Status = message);

        MainWindow = new MainWindow(viewModel);
        MainWindow.Show();
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
        // Order matters: no more chord events once the hook is disabled, then the engine releases push-to-talk
        // and restores the mic, then the hook itself, then the overlay, then audio.
        if (_hook is not null)
            _hook.Enabled = false;
        if (_devices is not null && _engine is not null)
            _devices.DevicesChanged -= _engine.RetryMicRestore;
        _engine?.Dispose();
        _hook?.Dispose();
        if (_engine is not null && _overlay is not null)
            _engine.ViewStateChanged -= _overlay.Show;
        _overlay?.Dispose();
        _player?.Dispose();
        _devices?.Dispose();
        base.OnExit(e);
    }
}
