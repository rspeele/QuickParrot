using QuickParrot.Core.Keyboard;
using QuickParrot.Core.Settings;

namespace QuickParrot.Core.Tests;

public class SettingsMirrorTests
{
    private readonly Queue<Func<AppSettings, AppSettings>> _engineQueue = new();
    private readonly Queue<Action> _uiQueue = new();
    private readonly SettingsMirror _mirror;
    private AppSettings _engineSettings = new();

    public SettingsMirrorTests()
    {
        _mirror = new SettingsMirror(_engineSettings, _engineQueue.Enqueue, _uiQueue.Enqueue);
    }

    [Fact]
    public void Update_ShowsAtOnce_AndRaisesChanged()
    {
        (AppSettings Old, AppSettings New)? raised = null;
        _mirror.Changed += (old, now) => raised = (old, now);

        _mirror.Update(s => s with { PreRollMilliseconds = 800 });

        Assert.Equal(800, _mirror.Current.PreRollMilliseconds);
        Assert.Equal(500, raised!.Value.Old.PreRollMilliseconds);
        Assert.Equal(800, raised.Value.New.PreRollMilliseconds);
    }

    [Fact]
    public void Update_IsSanitizedLocally()
    {
        _mirror.Update(s => s with { PreRollMilliseconds = 99_999, LiteLlmBaseUrl = "  http://llm  " });

        Assert.Equal(AppSettings.MaxMarginMilliseconds, _mirror.Current.PreRollMilliseconds);
        Assert.Equal("http://llm", _mirror.Current.LiteLlmBaseUrl);
    }

    [Fact]
    public void Update_ThatChangesNothing_IsNotSent()
    {
        _mirror.Update(s => s with { PreRollMilliseconds = 500 });

        Assert.Empty(_engineQueue);
    }

    [Fact]
    public void EngineUpdate_IsShown_WhenNoLocalChangeIsInFlight()
    {
        Publish(_engineSettings with { NavigatorPersistentPath = "Movies" });
        RunUi();

        Assert.Equal("Movies", _mirror.Current.NavigatorPersistentPath);
    }

    [Fact]
    public void StaleEcho_DoesNotUndoANewerLocalChange()
    {
        _mirror.Update(s => s with { CableVolume = 0.2f });
        RunEngine(); // the echo of 0.2 is now queued for the UI...
        _mirror.Update(s => s with { CableVolume = 0.3f }); // ...but the slider has already moved on
        var shown = new List<float>();
        _mirror.Changed += (_, now) => shown.Add(now.CableVolume);

        RunUi();
        RunEngine();
        RunUi();

        Assert.Empty(shown);
        Assert.Equal(0.3f, _mirror.Current.CableVolume);
    }

    [Fact]
    public void EngineChanges_MadeWhileLocalChangesWereInFlight_ArriveWithTheLastEcho()
    {
        _mirror.Update(s => s with { CableVolume = 0.2f });
        Publish(_engineSettings with { NavigatorPersistentPath = "Movies" }); // e.g. chord navigation in a game
        RunEngine();
        RunUi();

        Assert.Equal("Movies", _mirror.Current.NavigatorPersistentPath);
        Assert.Equal(0.2f, _mirror.Current.CableVolume);
    }

    [Fact]
    public void EngineResult_ReplacesTheLocalGuess()
    {
        // The engine applies the change to its own settings, which moved on since the mirror last heard.
        _engineSettings = _engineSettings with { PushToTalkBinding = PushToTalkBinding.FromMouse(PushToTalkMouseButton.X1) };
        _mirror.Update(s => s with { ChordKey = new ScanKey(0x21, false) });
        RunEngine();
        RunUi();

        Assert.Equal(_engineSettings, _mirror.Current);
        Assert.Equal(PushToTalkMouseButton.X1, _mirror.Current.PushToTalkBinding.MouseButton);
    }

    private void Publish(AppSettings settings)
    {
        _engineSettings = settings;
        _uiQueue.Enqueue(() => _mirror.Receive(settings));
    }

    // Like QuickParrotEngine.UpdateSettings: apply, sanitize, then publish to the UI.
    private void RunEngine()
    {
        while (_engineQueue.TryDequeue(out var change))
        {
            _engineSettings = change(_engineSettings).Sanitized();
            var published = _engineSettings;
            _uiQueue.Enqueue(() => _mirror.Receive(published));
        }
    }

    private void RunUi()
    {
        while (_uiQueue.TryDequeue(out var action))
            action();
    }
}
