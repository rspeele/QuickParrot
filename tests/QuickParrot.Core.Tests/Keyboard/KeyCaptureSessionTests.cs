using QuickParrot.Core.Keyboard;

namespace QuickParrot.Core.Tests.Keyboard;

public class KeyCaptureSessionTests
{
    private static readonly ScanKey F = new(0x21, false);

    private readonly List<TaskCompletionSource<ScanKey?>> _captures = [];
    private readonly KeyCaptureSession _session;

    public KeyCaptureSessionTests()
    {
        _session = new KeyCaptureSession(ct =>
        {
            var capture = new TaskCompletionSource<ScanKey?>();
            ct.Register(() => capture.TrySetCanceled(ct));
            _captures.Add(capture);
            return capture.Task;
        });
    }

    [Fact]
    public async Task PressedKey_IsCaptured()
    {
        var pending = _session.CaptureAsync("Ready");

        _captures[0].SetResult(F);

        Assert.Equal(new KeyCaptureResult(KeyCaptureOutcome.Captured, F, "Ready"), await pending);
    }

    [Fact]
    public async Task Escape_IsCancelled_WithTheMessageToRestore()
    {
        var pending = _session.CaptureAsync("Ready");

        _captures[0].SetResult(null);

        var result = await pending;
        Assert.Equal(KeyCaptureOutcome.Cancelled, result.Outcome);
        Assert.Equal("Ready", result.MessageBefore);
    }

    [Fact]
    public async Task Cancel_EndsTheCapture()
    {
        var pending = _session.CaptureAsync("Ready");

        _session.Cancel();

        Assert.Equal(KeyCaptureOutcome.Cancelled, (await pending).Outcome);
    }

    [Fact]
    public async Task NewCapture_SupersedesTheOld_AndKeepsTheOriginalMessage()
    {
        var first = _session.CaptureAsync("Ready");
        var second = _session.CaptureAsync(KeyCaptureSession.Prompt);

        Assert.Equal(KeyCaptureOutcome.Superseded, (await first).Outcome);

        _session.Cancel();
        var result = await second;
        Assert.Equal(KeyCaptureOutcome.Cancelled, result.Outcome);
        Assert.Equal("Ready", result.MessageBefore);
    }

    [Fact]
    public async Task OldCaptureEndingWithNoKey_AfterANewOneStarted_IsSuperseded()
    {
        // Like the hook: a new capture ends the old one with no key, whatever its cancellation token says.
        TaskCompletionSource<ScanKey?>? previous = null;
        var session = new KeyCaptureSession(_ =>
        {
            previous?.TrySetResult(null);
            previous = new TaskCompletionSource<ScanKey?>();
            return previous.Task;
        });

        var first = session.CaptureAsync("Ready");
        var second = session.CaptureAsync(KeyCaptureSession.Prompt);

        Assert.Equal(KeyCaptureOutcome.Superseded, (await first).Outcome);
        previous!.SetResult(F);
        Assert.Equal(KeyCaptureOutcome.Captured, (await second).Outcome);
    }

    [Fact]
    public async Task AfterACaptureEnds_TheNextOneRemembersTheNewMessage()
    {
        var first = _session.CaptureAsync("Ready");
        _captures[0].SetResult(F);
        await first;

        var second = _session.CaptureAsync("Chord key changed to F.");
        _session.Cancel();

        Assert.Equal("Chord key changed to F.", (await second).MessageBefore);
    }

    [Fact]
    public async Task HookNotRunning_IsUnavailable()
    {
        var session = new KeyCaptureSession(_ => throw new InvalidOperationException("The keyboard hook isn't running."));

        Assert.Equal(KeyCaptureOutcome.Unavailable, (await session.CaptureAsync("Ready")).Outcome);
    }
}
