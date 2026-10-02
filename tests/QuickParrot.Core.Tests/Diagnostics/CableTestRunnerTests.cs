using QuickParrot.Core.Devices;
using QuickParrot.Core.Diagnostics;

namespace QuickParrot.Core.Tests.Diagnostics;

public class CableTestRunnerTests
{
    private static readonly LoopbackTestSetup Ready = new("cable-in", "cable-out", "headphones");
    private static readonly LoopbackTestSetup NoCable = new(null, null, "headphones");

    private readonly List<string> _log = [];
    private LoopbackTestSetup _setup = Ready;
    private int _changes;

    private CableTestRunner Create()
    {
        var runner = new CableTestRunner(() => _setup, on => _log.Add($"suppress:{on}"), () => _log.Add("stop"));
        runner.Changed += () => _changes++;
        return runner;
    }

    [Fact]
    public void CableRun_SuppressesBeforeStopping_AndUnsuppressesWhenDone()
    {
        var runner = Create();

        var run = runner.TryStart(recordsCable: true);
        Assert.NotNull(run);
        Assert.Same(Ready, run.Setup);
        Assert.True(runner.IsBusy);
        run.Dispose();

        Assert.Equal(["suppress:True", "stop", "suppress:False"], _log);
        Assert.False(runner.IsBusy);
        Assert.Equal(2, _changes);
    }

    [Fact]
    public void OnlyOneRunAtATime()
    {
        var runner = Create();
        using var first = runner.TryStart(recordsCable: true);

        Assert.Null(runner.TryStart(recordsCable: true));
        Assert.Null(runner.TryStart(recordsCable: false));
    }

    [Fact]
    public void CableRun_NeedsTheCable_ButAReplayDoesNot()
    {
        _setup = NoCable;
        var runner = Create();

        Assert.Null(runner.TryStart(recordsCable: true));
        using var replay = runner.TryStart(recordsCable: false);

        Assert.NotNull(replay);
        Assert.Empty(_log); // a replay leaves playback alone
    }

    [Fact]
    public void Cancel_CancelsTheCurrentRun()
    {
        var runner = Create();
        using var run = runner.TryStart(recordsCable: false)!;

        runner.Cancel();

        Assert.True(run.Token.IsCancellationRequested);
    }

    [Fact]
    public void Refresh_RaisesChangedOnlyWhenReadinessChanges()
    {
        var runner = Create();
        Assert.True(runner.IsSetupReady);

        runner.Refresh();
        Assert.Equal(0, _changes);

        _setup = NoCable;
        runner.Refresh();
        Assert.False(runner.IsSetupReady);
        Assert.Equal(1, _changes);
    }

    [Fact]
    public void DevicesThatCantBeRead_AreNotReady()
    {
        var runner = new CableTestRunner(() => throw new InvalidOperationException("COM error"), _ => { }, () => { });

        Assert.False(runner.IsSetupReady);
        Assert.Null(runner.TryStart(recordsCable: true));
    }
}
