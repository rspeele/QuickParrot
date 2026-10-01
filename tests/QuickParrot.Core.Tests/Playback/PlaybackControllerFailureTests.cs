using QuickParrot.Core.Playback;

namespace QuickParrot.Core.Tests.Playback;

public class PlaybackControllerFailureTests : PlaybackControllerFixture
{
    [Fact]
    public void UnloadableClipWhenIdle_ReleasesImmediately_AndReportsError()
    {
        var controller = Create();
        Player.UnloadablePaths.Add(ClipA);

        controller.Play(ClipA);
        Advance(5000);

        Assert.Equal(["ptt:press", "mic:mute", Prepared(ClipA), "ptt:release", "mic:unmute"], Log);
        Assert.Equal(PlaybackPhase.Idle, controller.Phase);
        var error = Assert.Single(Errors);
        Assert.Equal(ClipA, error.ClipPath);
        Assert.Equal("The file no longer exists.", error.Message);
    }

    [Fact]
    public void UnloadableReplacementWhilePlaying_StopsLikeStop()
    {
        var controller = Create();
        PlayThroughPreRoll(controller, ClipA);
        Player.UnloadablePaths.Add(ClipB);

        controller.Play(ClipB);

        Assert.Equal([Prepared(ClipB), "stop", "ptt:release", "mic:unmute"], Log);
        Assert.Equal(PlaybackPhase.Idle, controller.Phase);
        Assert.Single(Errors);
    }

    [Fact]
    public void UnloadableReplacementDuringPreRoll_ReleasesAndDropsPendingClip()
    {
        var controller = Create();
        controller.Play(ClipA);
        Player.UnloadablePaths.Add(ClipB);

        controller.Play(ClipB);
        Advance(5000);

        Assert.DoesNotContain(Log, e => e.StartsWith("play:"));
        Assert.Equal(["ptt:release", "mic:unmute"], Log[^2..]);
        Assert.Single(Errors);
        Assert.Equal([ClipA], Player.DisposedClips);
    }

    [Fact]
    public void UnloadableReplacementDuringPostRoll_ReleasesImmediately()
    {
        var controller = Create();
        PlayThroughPreRoll(controller, ClipA);
        Player.RaiseFinished();
        Player.UnloadablePaths.Add(ClipB);

        controller.Play(ClipB);

        Assert.Equal(["ptt:release", "mic:unmute"], Log[^2..]);
        Assert.Equal(PlaybackPhase.Idle, controller.Phase);
    }

    [Fact]
    public void ClipFailingToStart_ReleasesAndReportsError()
    {
        var controller = Create();
        Player.UnplayablePaths.Add(ClipA);

        controller.Play(ClipA);
        Advance(500);

        Assert.Equal(["stop", "ptt:release", "mic:unmute"], Log[^3..]);
        Assert.Equal(PlaybackPhase.Idle, controller.Phase);
        Assert.Equal("Device unavailable.", Assert.Single(Errors).Message);
    }

    [Fact]
    public void ClipFailingMidPlay_ReleasesWithoutPostRoll_AndReportsError()
    {
        var controller = Create();
        PlayThroughPreRoll(controller, ClipA);

        Player.RaiseFinished(error: new IOException("Device removed."));

        Assert.Equal(["stop", "ptt:release", "mic:unmute"], Log);
        Assert.Equal(PlaybackPhase.Idle, controller.Phase);
        var error = Assert.Single(Errors);
        Assert.Equal(ClipA, error.ClipPath);
        Assert.Equal("Device removed.", error.Message);
    }

    [Fact]
    public void StaleFailure_FromReplacedClip_IsIgnored()
    {
        var controller = Create();
        PlayThroughPreRoll(controller, ClipA);
        var firstPlayId = Player.CurrentPlayId!.Value;
        controller.Play(ClipB);

        Player.RaiseFinished(firstPlayId, new IOException("Device removed."));

        Assert.Empty(Errors);
        Assert.Equal(PlaybackPhase.Playing, controller.Phase);
    }

    [Fact]
    public void CanPlayAgainAfterFailure()
    {
        var controller = Create();
        Player.UnloadablePaths.Add(ClipA);
        controller.Play(ClipA);

        PlayThroughPreRoll(controller, ClipB);

        Assert.Equal(PlaybackPhase.Playing, controller.Phase);
    }

    [Fact]
    public void PushToTalkDisabled_SkipsPressAndRelease()
    {
        var controller = Create(PlaybackOptions.Default with { PushToTalkEnabled = false });

        controller.Play(ClipA);
        Advance(500);
        controller.Stop();

        Assert.DoesNotContain(Log, e => e.StartsWith("ptt:"));
        Assert.Contains("mic:mute", Log);
        Assert.Contains("mic:unmute", Log);
    }

    [Fact]
    public void MicMuteDisabled_SkipsMuteAndUnmute()
    {
        var controller = Create(PlaybackOptions.Default with { MicMuteEnabled = false });

        controller.Play(ClipA);
        Advance(500);
        controller.Stop();

        Assert.DoesNotContain(Log, e => e.StartsWith("mic:"));
        Assert.Contains("ptt:press", Log);
        Assert.Contains("ptt:release", Log);
    }

    [Fact]
    public void DisablingPushToTalkWhileHeld_StillReleasesIt()
    {
        var controller = Create();
        PlayThroughPreRoll(controller, ClipA);

        controller.Options = controller.Options with { PushToTalkEnabled = false, MicMuteEnabled = false };
        controller.Stop();

        Assert.Equal(["stop", "ptt:release", "mic:unmute"], Log);
    }

    [Fact]
    public void EnablingPushToTalkMidClip_PressesOnReplacement()
    {
        var controller = Create(PlaybackOptions.Default with { PushToTalkEnabled = false });
        PlayThroughPreRoll(controller, ClipA);

        controller.Options = controller.Options with { PushToTalkEnabled = true };
        controller.Play(ClipC);

        Assert.Equal(["ptt:press", Prepared(ClipC), Play(ClipC)], Log);
    }

    [Fact]
    public void PlayerStopThrowing_StillReleasesAndUnmutes()
    {
        var controller = Create();
        PlayThroughPreRoll(controller, ClipA);
        Player.ThrowOnStop = true;

        Assert.Throws<InvalidOperationException>(controller.Stop);

        Assert.Equal(["stop", "ptt:release", "mic:unmute"], Log);
        Assert.Equal(PlaybackPhase.Idle, controller.Phase);
        Player.ThrowOnStop = false;
        PlayThroughPreRoll(controller, ClipB);
    }

    [Fact]
    public void PlayerStopThrowing_WhenReplacementFails_StillReleasesAndReportsError()
    {
        var controller = Create();
        PlayThroughPreRoll(controller, ClipA);
        Player.UnloadablePaths.Add(ClipB);
        Player.ThrowOnStop = true;

        controller.Play(ClipB);

        Assert.Equal([Prepared(ClipB), "stop", "ptt:release", "mic:unmute"], Log);
        Assert.Equal(PlaybackPhase.Idle, controller.Phase);
        Assert.Single(Errors);
        Assert.Equal("Stop failed.", Assert.Single(DispatchErrors).Message);
        DispatchErrors.Clear();
    }

    [Fact]
    public void PushToTalkReleaseThrowing_StillUnmutes()
    {
        var controller = Create();
        PlayThroughPreRoll(controller, ClipA);
        PushToTalk.ThrowOnRelease = true;

        Assert.Throws<InvalidOperationException>(controller.Stop);

        Assert.Equal(["stop", "ptt:release", "mic:unmute"], Log);
    }

    [Fact]
    public void Dispose_StopsAndReleases_AndIgnoresLaterCallbacks()
    {
        var controller = Create();
        PlayThroughPreRoll(controller, ClipA);
        var playId = Player.CurrentPlayId!.Value;

        controller.Dispose();
        Player.RaiseFinished(playId, new IOException("late"));

        Assert.Equal(["stop", "ptt:release", "mic:unmute"], Log);
        Assert.Empty(Errors);
    }
}
