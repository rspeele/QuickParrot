namespace QuickParrot.Input.Tests;

// No-op install/uninstall/release: only the thread's own message queue is used, no hooks or input.
public sealed class HookThreadTests
{
    private readonly List<string> _log = [];
    private readonly HookThread _thread = new("test hook thread");

    [Fact]
    public void WhileStopped_PostRunsInline_UnlessToldToDrop()
    {
        _thread.Post(() => Log("kept"));
        _thread.Post(() => Log("dropped"), dropIfStopped: true);

        Assert.Equal(["kept"], _log);
    }

    [Fact]
    public void WhileRunning_CommandsRunInOrderOnTheThread()
    {
        var callerThread = Environment.CurrentManagedThreadId;
        var ranOn = new List<int>();
        Start();

        for (var i = 0; i < 3; i++)
        {
            var n = i;
            _thread.Post(() =>
            {
                ranOn.Add(Environment.CurrentManagedThreadId);
                Log($"cmd{n}");
            }, dropIfStopped: true);
        }

        _thread.Stop();

        Assert.Equal(["install", "cmd0", "cmd1", "cmd2", "uninstall", "release"], _log);
        Assert.DoesNotContain(callerThread, ranOn);
        Assert.False(_thread.IsRunning);
    }

    [Fact]
    public async Task CommandsQueuedBeforeStop_AllRunBeforeRelease()
    {
        using var gate = new ManualResetEventSlim();
        Start();
        _thread.Post(() => gate.Wait());
        _thread.Post(() => Log("queued"));

        var stopping = Task.Run(_thread.Stop);
        gate.Set();
        await stopping;

        Assert.Equal(["install", "queued", "uninstall", "release"], _log);
    }

    [Fact]
    public void FailedInstall_TearsDownAndRethrows_ThenPostsRunInline()
    {
        var error = Assert.Throws<InvalidOperationException>(() => _thread.Start(
            () => throw new InvalidOperationException("nope"), () => Log("uninstall"), () => Log("release")));

        Assert.Equal("nope", error.Message);
        Assert.False(_thread.IsRunning);
        _thread.Post(() => Log("inline"));
        Assert.Equal(["uninstall", "release", "inline"], _log);
    }

    [Fact]
    public void StartAndStop_AreIdempotent()
    {
        Start();
        Start();
        _thread.Stop();
        _thread.Stop();

        Assert.Equal(["install", "uninstall", "release"], _log);
    }

    [Fact]
    public async Task ManyStartStopCycles_NeverLoseAPostOrTheQuit()
    {
        await Task.Run(() =>
        {
            for (var i = 0; i < 500; i++)
            {
                _thread.Start(() => { }, () => { }, () => { });
                _thread.Post(() => { });
                _thread.Stop();
            }
        }).WaitAsync(TimeSpan.FromSeconds(10));
    }

    private void Start() => _thread.Start(() => Log("install"), () => Log("uninstall"), () => Log("release"));

    private void Log(string entry)
    {
        lock (_log)
            _log.Add(entry);
    }
}
