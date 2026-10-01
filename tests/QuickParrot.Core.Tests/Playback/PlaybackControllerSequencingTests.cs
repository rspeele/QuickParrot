using QuickParrot.Core.Playback;

namespace QuickParrot.Core.Tests.Playback;

public class PlaybackControllerSequencingTests : PlaybackControllerFixture
{
    [Fact]
    public void PlayWhenIdle_PressesAndMutesImmediately_ThenStartsAfterPreRoll()
    {
        var controller = Create();

        controller.Play(ClipA);

        Assert.Equal(["ptt:press", "mic:mute", Prepared(ClipA)], Log);
        Assert.Equal(PlaybackPhase.PreRoll, controller.Phase);

        Advance(499);
        Assert.DoesNotContain(Play(ClipA), Log);

        Advance(1);
        Assert.Equal(Play(ClipA), Log[^1]);
        Assert.Equal(PlaybackPhase.Playing, controller.Phase);
    }

    [Fact]
    public void PlayDuringPreRoll_ReplacesPendingClip_OnlyWaitsOutTheRemainder()
    {
        var controller = Create();

        controller.Play(ClipA);
        Advance(300);
        controller.Play(ClipB);
        Advance(199);
        Assert.DoesNotContain(Log, e => e.StartsWith("play:"));

        Advance(1);
        Assert.Equal(["ptt:press", "mic:mute", Prepared(ClipA), Prepared(ClipB), Play(ClipB)], Log);
    }

    [Fact]
    public void ReplacedPendingClip_IsDisposed_PlayedClipIsNot()
    {
        var controller = Create();

        controller.Play(ClipA);
        controller.Play(ClipB);
        Advance(500);

        Assert.Equal([ClipA], Player.DisposedClips);
    }

    [Fact]
    public void StopDuringPreRoll_DisposesPendingClip()
    {
        var controller = Create();
        controller.Play(ClipA);

        controller.Stop();

        Assert.Equal([ClipA], Player.DisposedClips);
    }

    [Fact]
    public void DisposeDuringPreRoll_DisposesPendingClip()
    {
        var controller = Create();
        controller.Play(ClipA);

        controller.Dispose();

        Assert.Equal([ClipA], Player.DisposedClips);
        Assert.Equal("mic:unmute", Log[^1]);
    }

    [Fact]
    public void PlayWhilePlaying_SwitchesImmediately_KeepsPushToTalkHeld()
    {
        var controller = Create();
        PlayThroughPreRoll(controller, ClipA);

        controller.Play(ClipB);

        Assert.Equal([Prepared(ClipB), Play(ClipB)], Log);
        Assert.Equal(PlaybackPhase.Playing, controller.Phase);
    }

    [Fact]
    public void ClipFinishes_WaitsPostRoll_ThenReleasesAndUnmutes()
    {
        var controller = Create();
        PlayThroughPreRoll(controller, ClipA);

        Player.RaiseFinished();
        Assert.Equal(PlaybackPhase.PostRoll, controller.Phase);
        Advance(499);
        Assert.Empty(Log);

        Advance(1);
        Assert.Equal(["stop", "ptt:release", "mic:unmute"], Log);
        Assert.Equal(PlaybackPhase.Idle, controller.Phase);
    }

    [Fact]
    public void PlayDuringPostRoll_CancelsPendingRelease_AndPlaysImmediately()
    {
        var controller = Create();
        PlayThroughPreRoll(controller, ClipA);
        Player.RaiseFinished();
        Advance(300);

        controller.Play(ClipB);
        Advance(5000);

        Assert.Equal([Prepared(ClipB), Play(ClipB)], Log);
        Assert.Equal(PlaybackPhase.Playing, controller.Phase);
    }

    [Fact]
    public void ReplacementClip_GetsItsOwnPostRoll()
    {
        var controller = Create();
        PlayThroughPreRoll(controller, ClipA);
        controller.Play(ClipB);
        Log.Clear();

        Player.RaiseFinished();
        Advance(500);

        Assert.Equal(["stop", "ptt:release", "mic:unmute"], Log);
    }

    [Fact]
    public void StopWhilePlaying_StopsAndReleasesImmediately()
    {
        var controller = Create();
        PlayThroughPreRoll(controller, ClipA);

        controller.Stop();

        Assert.Equal(["stop", "ptt:release", "mic:unmute"], Log);
        Assert.Equal(PlaybackPhase.Idle, controller.Phase);
    }

    [Fact]
    public void StopDuringPreRoll_ReleasesAndNeverStartsTheClip()
    {
        var controller = Create();
        controller.Play(ClipA);
        Advance(200);

        controller.Stop();
        Advance(5000);

        Assert.DoesNotContain(Play(ClipA), Log);
        Assert.Equal(["ptt:release", "mic:unmute"], Log[^2..]);
        Assert.Equal(PlaybackPhase.Idle, controller.Phase);
    }

    [Fact]
    public void StopDuringPostRoll_ReleasesImmediately()
    {
        var controller = Create();
        PlayThroughPreRoll(controller, ClipA);
        Player.RaiseFinished();
        Advance(100);

        controller.Stop();
        Advance(5000);

        Assert.Equal(["stop", "ptt:release", "mic:unmute"], Log);
    }

    [Fact]
    public void StopWhenIdle_IsNoOp()
    {
        var controller = Create();

        controller.Stop();

        Assert.Empty(Log);
    }

    [Fact]
    public void PlayAfterStopDuringPreRoll_WaitsAFullNewPreRoll()
    {
        var controller = Create();
        controller.Play(ClipA);
        Advance(400);
        controller.Stop();

        controller.Play(ClipB);
        Advance(100); // when ClipA's cancelled timer would have fired
        Assert.DoesNotContain(Log, e => e.StartsWith("play:"));

        Advance(400);
        Assert.Equal(Play(ClipB), Log[^1]);
    }

    [Fact]
    public void StaleFinished_FromReplacedClip_IsIgnored()
    {
        var controller = Create();
        PlayThroughPreRoll(controller, ClipA);
        var firstPlayId = Player.CurrentPlayId!.Value;
        controller.Play(ClipB);

        Player.RaiseFinished(firstPlayId);
        Advance(5000);

        Assert.Equal(PlaybackPhase.Playing, controller.Phase);
        Assert.DoesNotContain("ptt:release", Log);
    }

    [Fact]
    public void StaleFinished_AfterStopAndReplay_IsIgnored()
    {
        var controller = Create();
        PlayThroughPreRoll(controller, ClipA);
        var firstPlayId = Player.CurrentPlayId!.Value;
        controller.Stop();
        PlayThroughPreRoll(controller, ClipB);

        Player.RaiseFinished(firstPlayId);

        Assert.Equal(PlaybackPhase.Playing, controller.Phase);
    }

    [Fact]
    public void FinishedWhileIdle_IsIgnored()
    {
        var controller = Create();
        PlayThroughPreRoll(controller, ClipA);
        var playId = Player.CurrentPlayId!.Value;
        controller.Stop();
        Log.Clear();

        Player.RaiseFinished(playId);
        Advance(5000);

        Assert.Empty(Log);
        Assert.Equal(PlaybackPhase.Idle, controller.Phase);
    }

    [Fact]
    public void CustomMargins_AreHonoured()
    {
        var options = PlaybackOptions.Default with
        {
            PreRoll = TimeSpan.FromMilliseconds(200),
            PostRoll = TimeSpan.FromMilliseconds(1000),
        };
        var controller = Create(options);

        controller.Play(ClipA);
        Advance(200);
        Assert.Equal(PlaybackPhase.Playing, controller.Phase);

        Player.RaiseFinished();
        Advance(999);
        Assert.Equal(PlaybackPhase.PostRoll, controller.Phase);
        Advance(1);
        Assert.Equal(PlaybackPhase.Idle, controller.Phase);
    }

    [Fact]
    public void ZeroMargins_PlayAndReleaseWithoutWaiting()
    {
        var controller = Create(PlaybackOptions.Default with { PreRoll = TimeSpan.Zero, PostRoll = TimeSpan.Zero });

        controller.Play(ClipA);
        Assert.Equal(PlaybackPhase.Playing, controller.Phase);

        Player.RaiseFinished();
        Assert.Equal(PlaybackPhase.Idle, controller.Phase);
        Assert.Equal("mic:unmute", Log[^1]);
    }

    [Fact]
    public void CallbacksAreRoutedThroughDispatch()
    {
        var queued = new Queue<Action>();
        var controller = Create(dispatch: queued.Enqueue);

        controller.Play(ClipA);
        Advance(500);
        Assert.Equal(PlaybackPhase.PreRoll, controller.Phase); // prepared and fired, but not yet run on "our" thread

        queued.Dequeue()();
        Assert.Equal(PlaybackPhase.PreRoll, controller.Phase);
        queued.Dequeue()();
        Assert.Equal(PlaybackPhase.Playing, controller.Phase);

        Player.RaiseFinished();
        Assert.Equal(PlaybackPhase.Playing, controller.Phase);
        queued.Dequeue()();
        Assert.Equal(PlaybackPhase.PostRoll, controller.Phase);
    }

    [Fact]
    public void TimerCallbackQueuedBeforeStop_IsIgnored()
    {
        var queued = new Queue<Action>();
        var controller = Create(dispatch: queued.Enqueue);
        controller.Play(ClipA);
        Advance(500);

        controller.Stop();
        controller.Play(ClipB);
        queued.Dequeue()(); // ClipA's prepare result, delivered late
        queued.Dequeue()(); // ClipA's pre-roll expiry, delivered late

        Assert.Equal(PlaybackPhase.PreRoll, controller.Phase);
        Assert.DoesNotContain(Log, e => e.StartsWith("play:"));
        Assert.Equal([ClipA], Player.DisposedClips);
    }
}
