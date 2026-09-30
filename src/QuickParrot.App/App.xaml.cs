using QuickParrot.Audio;
using QuickParrot.Core.Engine;
using QuickParrot.Core.Library;
using QuickParrot.Core.Playback;
using QuickParrot.Core.Settings;

namespace QuickParrot.App;

public partial class App : System.Windows.Application
{
    private QuickParrotEngine? _engine;
    private NAudioClipPlayer? _player;
    private WindowsAudioDeviceCatalog? _devices;

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
        var viewModel = new MainViewModel(_engine, _devices, loaded.Warning);
        _engine.Start();

        MainWindow = new MainWindow(viewModel);
        MainWindow.Show();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _engine?.Dispose();
        _player?.Dispose();
        _devices?.Dispose();
        base.OnExit(e);
    }
}
