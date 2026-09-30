using QuickParrot.Core.Playback;

namespace QuickParrot.Core.Tests.Playback;

// Prepares here stay in flight until the test completes them, like a slow decode on the thread pool.
public class PlaybackControllerAsyncPrepareTests : PlaybackControllerFixture
{
    public PlaybackControllerAsyncPrepareTests()
    {
        Player.ManualPrepare = true;
    }

    [Fact]
    public void PrepareSlowerThanPreRoll_StartsAsSoonAsPrepared()
    {
        var controller = Create();

        controller.Play(ClipA);
        Advance(2000);
        Assert.Equal(["ptt:press", "mic:mute", Prepared(ClipA)], Log);
        Assert.Equal(PlaybackPhase.PreRoll, controller.Phase);

        Player.CompletePrepare(ClipA);

        Assert.Equal(Play(ClipA), Log[^1]);
        Assert.Equal(PlaybackPhase.Playing, controller.Phase);
    }

    [Fact]
    public void PrepareFasterThanPreRoll_WaitsOutThePreRoll()
    {
        var controller = Create();

        controller.Play(ClipA);
        Advance(200);
        Player.CompletePrepare(ClipA);
        Advance(299);
        Assert.DoesNotContain(Play(ClipA), Log);

        Advance(1);
        Assert.Equal(Play(ClipA), Log[^1]);
    }

    [Fact]
    public void ZeroPreRoll_StartsWhenPrepared()
    {
        var controller = Create(PlaybackOptions.Default with { PreRoll = TimeSpan.Zero });

        controller.Play(ClipA);
        Assert.Equal(PlaybackPhase.PreRoll, controller.Phase);

        Player.CompletePrepare(ClipA);
        Assert.Equal(PlaybackPhase.Playing, controller.Phase);
    }

    [Fact]
    public void ReplaceDuringPreRoll_CancelsInFlightPrepare_AndKeepsTheOriginalDeadline()
    {
        var controller = Create();
        controller.Play(ClipA);
        Advance(100);

        controller.Play(ClipB);
        Assert.True(Player.WasCancelled(ClipA));
        Player.CompletePrepare(ClipA);
        Player.CompletePrepare(ClipB);
        Advance(399);
        Assert.DoesNotContain(Log, e => e.StartsWith("play:"));

        Advance(1);
        Assert.Equal(Play(ClipB), Log[^1]);
        Assert.Equal([ClipA], Player.DisposedClips);
        Assert.Equal(1, Log.Count(e => e == "ptt:press"));
    }

    [Fact]
    public void ReplaceAfterPreRollElapsed_StartsReplacementAsSoonAsPrepared()
    {
        var controller = Create();
        controller.Play(ClipA);
        Advance(600);

        controller.Play(ClipB);
        Player.CompletePrepare(ClipB);
        Player.CompletePrepare(ClipA);

        Assert.Equal(Play(ClipB), Log[^1]);
        Assert.DoesNotContain(Play(ClipA), Log);
        Assert.Equal([ClipA], Player.DisposedClips);
    }

    [Fact]
    public void ReplaceWhilePlaying_OldClipKeepsPlayingUntilReplacementIsPrepared()
    {
        var controller = Create();
        StartPlaying(controller, ClipA);

        controller.Play(ClipB);
        Advance(5000);
        Assert.Equal([Prepared(ClipB)], Log);
        Assert.Equal(PlaybackPhase.Playing, controller.Phase);

        Player.CompletePrepare(ClipB);
        Assert.Equal([Prepared(ClipB), Play(ClipB)], Log);
        Assert.Equal(PlaybackPhase.Playing, controller.Phase);
    }

    [Fact]
    public void ReplaceWhilePlaying_ThenReplaceAgain_OnlyTheLatestPlays()
    {
        var controller = Create();
        StartPlaying(controller, ClipA);

        controller.Play(ClipB);
        controller.Play(ClipC);
        Player.CompletePrepare(ClipC);
        Player.CompletePrepare(ClipB);

        Assert.Equal([Prepared(ClipB), Prepared(ClipC), Play(ClipC)], Log);
        Assert.True(Player.WasCancelled(ClipB));
        Assert.Equal([ClipB], Player.DisposedClips);
    }

    [Fact]
    public void OldClipFinishingWhileReplacementPrepares_HoldsTheKeyUntilItStarts()
    {
        var controller = Create();
        StartPlaying(controller, ClipA);
        controller.Play(ClipB);

        Player.RaiseFinished();
        Advance(5000);
        Assert.Equal(PlaybackPhase.PostRoll, controller.Phase);
        Assert.DoesNotContain("ptt:release", Log);

        Player.CompletePrepare(ClipB);
        Assert.Equal(PlaybackPhase.Playing, controller.Phase);
        Assert.Equal(Play(ClipB), Log[^1]);
    }

    [Fact]
    public void ReplaceDuringPostRoll_CancelsPendingRelease_StartsWhenPrepared()
    {
        var controller = Create();
        StartPlaying(controller, ClipA);
        Player.RaiseFinished();
        Advance(300);

        controller.Play(ClipB);
        Advance(5000);
        Assert.Equal([Prepared(ClipB)], Log);

        Player.CompletePrepare(ClipB);
        Assert.Equal([Prepared(ClipB), Play(ClipB)], Log);
        Assert.Equal(PlaybackPhase.Playing, controller.Phase);
    }

    [Fact]
    public void StopDuringPreRoll_CancelsPrepare_AndDisposesItsLateResult()
    {
        var controller = Create();
        controller.Play(ClipA);

        controller.Stop();
        Assert.True(Player.WasCancelled(ClipA));
        Player.CompletePrepare(ClipA);
        Advance(5000);

        Assert.Equal(["ptt:press", "mic:mute", Prepared(ClipA), "ptt:release", "mic:unmute"], Log);
        Assert.Equal([ClipA], Player.DisposedClips);
        Assert.Equal(PlaybackPhase.Idle, controller.Phase);
    }

    [Fact]
    public void StopWhilePlaying_WithReplacementInFlight_StopsNowAndDisposesTheReplacement()
    {
        var controller = Create();
        StartPlaying(controller, ClipA);
        controller.Play(ClipB);

        controller.Stop();
        Player.CompletePrepare(ClipB);

        Assert.Equal([Prepared(ClipB), "stop", "ptt:release", "mic:unmute"], Log);
        Assert.Equal([ClipB], Player.DisposedClips);
        Assert.Equal(PlaybackPhase.Idle, controller.Phase);
    }

    [Fact]
    public void StopDuringPostRoll_WithReplacementInFlight_DisposesTheReplacement()
    {
        var controller = Create();
        StartPlaying(controller, ClipA);
        Player.RaiseFinished();
        controller.Play(ClipB);

        controller.Stop();
        Player.CompletePrepare(ClipB);

        Assert.Equal([Prepared(ClipB), "stop", "ptt:release", "mic:unmute"], Log);
        Assert.Equal([ClipB], Player.DisposedClips);
    }

    [Fact]
    public void DisposeWithPrepareInFlight_DisposesItsLateResult()
    {
        var controller = Create();
        controller.Play(ClipA);

        controller.Dispose();
        Player.CompletePrepare(ClipA);

        Assert.Equal([ClipA], Player.DisposedClips);
        Assert.Equal("mic:unmute", Log[^1]);
    }

    [Fact]
    public void StaleResult_AfterStopAndReplay_DoesNotDisturbTheNewRequest()
    {
        var controller = Create();
        controller.Play(ClipA);
        controller.Stop();
        controller.Play(ClipB);

        Player.CompletePrepare(ClipA);
        Assert.Equal(PlaybackPhase.PreRoll, controller.Phase);

        Player.CompletePrepare(ClipB);
        Advance(500);
        Assert.Equal(Play(ClipB), Log[^1]);
        Assert.Equal([ClipA], Player.DisposedClips);
    }

    [Fact]
    public void StaleFailure_FromSupersededPrepare_IsIgnored()
    {
        var controller = Create();
        controller.Play(ClipA);
        controller.Play(ClipB);

        Player.FailPrepare(ClipA, new InvalidDataException("Bad file."));
        Assert.Empty(Errors);
        Assert.Equal(PlaybackPhase.PreRoll, controller.Phase);

        Player.CompletePrepare(ClipB);
        Advance(500);
        Assert.Equal(Play(ClipB), Log[^1]);
    }

    [Fact]
    public void StaleResult_QueuedBeforeStop_IsDisposedNotPlayed()
    {
        var queued = new Queue<Action>();
        var controller = Create(dispatch: queued.Enqueue);
        controller.Play(ClipA);
        Player.CompletePrepare(ClipA); // result is now queued for the controller's thread

        controller.Stop();
        Assert.Equal([ClipA], Player.DisposedClips);
        while (queued.Count > 0)
            queued.Dequeue()();

        Assert.DoesNotContain(Play(ClipA), Log);
        Assert.Equal(PlaybackPhase.Idle, controller.Phase);
    }

    [Fact]
    public void DroppedDispatch_StillDisposesTheClipOnStop()
    {
        var controller = Create(dispatch: _ => { }); // e.g. the engine's queue has shut down
        controller.Play(ClipA);
        Player.CompletePrepare(ClipA);

        controller.Stop();

        Assert.Equal([ClipA], Player.DisposedClips);
    }

    [Fact]
    public void PrepareFailureDuringPreRoll_ReleasesImmediately_AndReportsError()
    {
        var controller = Create();
        controller.Play(ClipA);
        Advance(100);

        Player.FailPrepare(ClipA, new InvalidDataException("The file isn't a supported audio format."));

        Assert.Equal(["ptt:press", "mic:mute", Prepared(ClipA), "ptt:release", "mic:unmute"], Log);
        Assert.Equal(PlaybackPhase.Idle, controller.Phase);
        Assert.Equal(new PlaybackError(ClipA, "The file isn't a supported audio format."), Assert.Single(Errors));
        Advance(5000);
        Assert.Equal("mic:unmute", Log[^1]);
    }

    [Fact]
    public void PrepareFailureAfterPreRollElapsed_ReleasesAndReportsError()
    {
        var controller = Create();
        controller.Play(ClipA);
        Advance(800);

        Player.FailPrepare(ClipA, new FileNotFoundException("gone", ClipA));

        Assert.Equal(["ptt:release", "mic:unmute"], Log[^2..]);
        Assert.Equal("The file no longer exists.", Assert.Single(Errors).Message);
    }

    [Fact]
    public void ReplacementFailingWhilePlaying_StopsTheOldClipToo()
    {
        var controller = Create();
        StartPlaying(controller, ClipA);
        controller.Play(ClipB);

        Player.FailPrepare(ClipB, new InvalidDataException("Bad file."));

        Assert.Equal([Prepared(ClipB), "stop", "ptt:release", "mic:unmute"], Log);
        Assert.Equal(PlaybackPhase.Idle, controller.Phase);
        Assert.Equal(ClipB, Assert.Single(Errors).ClipPath);
    }

    [Fact]
    public void ReplacementFailingDuringPostRoll_ReleasesImmediately()
    {
        var controller = Create();
        StartPlaying(controller, ClipA);
        Player.RaiseFinished();
        controller.Play(ClipB);

        Player.FailPrepare(ClipB, new InvalidDataException("Bad file."));

        Assert.Equal([Prepared(ClipB), "stop", "ptt:release", "mic:unmute"], Log);
        Assert.Single(Errors);
    }

    [Fact]
    public void UnexpectedCancellation_IsReportedAsFailure()
    {
        var controller = Create();
        controller.Play(ClipA);

        Player.CancelPrepare(ClipA);

        Assert.Equal(PlaybackPhase.Idle, controller.Phase);
        Assert.Single(Errors);
    }

    [Fact]
    public void PlayerThrowingSynchronouslyFromPrepare_IsReportedAsFailure()
    {
        var controller = Create();
        Player.ThrowFromPrepare = new InvalidOperationException("Player broken.");

        controller.Play(ClipA);

        Assert.Equal(PlaybackPhase.Idle, controller.Phase);
        Assert.Equal("Player broken.", Assert.Single(Errors).Message);
    }

    [Fact]
    public void CanPlayAgainAfterPrepareFailure()
    {
        var controller = Create();
        controller.Play(ClipA);
        Player.FailPrepare(ClipA, new InvalidDataException("Bad file."));

        StartPlaying(controller, ClipB);

        Assert.Equal(PlaybackPhase.Playing, controller.Phase);
    }

    private void StartPlaying(PlaybackController controller, string path)
    {
        controller.Play(path);
        Player.CompletePrepare(path);
        Time.Advance(controller.Options.PreRoll);
        Assert.Equal(PlaybackPhase.Playing, controller.Phase);
        Log.Clear();
    }
}
