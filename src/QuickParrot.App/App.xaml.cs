using System.ComponentModel;
using QuickParrot.Audio;
using QuickParrot.Core.Engine;
using QuickParrot.Core.Library;
using QuickParrot.Core.Playback;
using QuickParrot.Core.Settings;
using QuickParrot.Input;
using QuickParrot.Overlay;

namespace QuickParrot.App;

public partial class App : System.Windows.Application
{
    private QuickParrotEngine? _engine;
    private NAudioClipPlayer? _player;
    private WindowsAudioDeviceCatalog? _devices;
    private LowLevelKeyboardHook? _hook;
    private OverlayHost? _overlay;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        var store = new JsonSettingsStore(JsonSettingsStore.DefaultPath);
        var loaded = store.Load();
        _devices = new WindowsAudioDeviceCatalog();
        _player = new NAudioClipPlayer(_devices);
        _player.Configure(loaded.Settings.ToOutputSettings()); // so warm-up opens the configured devices
        _ = _player.WarmUpAsync();
        _engine = new QuickParrotEngine(
            _player,
            new LoggingPushToTalk(),
            new LoggingMicMuter(),
            store,
            loaded.Settings,
            TimeProvider.System,
            root => new FileSystemFolderSource(root));
        _engine.Start();

        var warnings = new List<string>();
        if (loaded.Warning is { } loadWarning)
            warnings.Add(loadWarning);

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

        _hook = new LowLevelKeyboardHook(loaded.Settings.ChordKey, _engine.Post) { Enabled = loaded.Settings.HotkeysEnabled };
        try
        {
            _hook.Start();
        }
        catch (Win32Exception ex)
        {
            warnings.Add($"Couldn't enable hotkeys: {ex.Message}");
        }

        var viewModel = new MainViewModel(_engine, _devices, _hook, warnings.Count == 0 ? null : string.Join(" ", warnings));

        // The overlay raises this on its own thread, so it must be marshalled onto the UI thread.
        _overlay.ErrorOccurred += message =>
            Dispatcher.BeginInvoke(() => viewModel.Status = message);

        MainWindow = new MainWindow(viewModel);
        MainWindow.Show();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        // Order matters: no more chord events before the engine stops, then the overlay, then audio.
        _hook?.Dispose();
        _engine?.Dispose();
        if (_engine is not null && _overlay is not null)
            _engine.ViewStateChanged -= _overlay.Show;
        _overlay?.Dispose();
        _player?.Dispose();
        _devices?.Dispose();
        base.OnExit(e);
    }
}
