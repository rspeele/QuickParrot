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

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        var store = new JsonSettingsStore(JsonSettingsStore.DefaultPath);
        var devices = new WindowsAudioDeviceCatalog();
        _player = new NAudioClipPlayer(devices);
        _engine = new QuickParrotEngine(
            _player,
            new LoggingPushToTalk(),
            new LoggingMicMuter(),
            store,
            store.Load(),
            TimeProvider.System,
            root => new FileSystemFolderSource(root));
        var viewModel = new MainViewModel(_engine, devices);
        _engine.Start();

        MainWindow = new MainWindow(viewModel);
        MainWindow.Show();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _engine?.Dispose();
        _player?.Dispose();
        base.OnExit(e);
    }
}
