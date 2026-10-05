using QuickParrot.Core.Playback;

namespace QuickParrot.Core.Tests.Playback;

public class PlaybackControllerPhraseTests : PlaybackControllerFixture
{
    [Fact]
    public void Phrase_PreparesAndPlaysOnce_WithOnePushToTalkAndMicMuteCycle()
    {
        var controller = Create();

        controller.PlayPhrase([ClipA, ClipB, ClipC]);

        Assert.Equal(["ptt:press", "mic:mute", Prepared(ClipA)], Log);
        Assert.Equal(new[] { ClipA, ClipB, ClipC }, Assert.Single(Player.Phrases));
        Assert.Single(Player.Prepares);
        Advance(500);
        Assert.Equal(PlaybackPhase.Playing, controller.Phase);
        Assert.Equal(Play(ClipA), Log[^1]);

        Player.RaiseFinished();
        Advance(499);
        Assert.DoesNotContain("ptt:release", Log);
        Advance(1);
        Assert.Equal(["ptt:press", "mic:mute", Prepared(ClipA), Play(ClipA), "stop", "ptt:release", "mic:unmute"], Log);
    }

    [Fact]
    public void Phrase_SnapshotsSelectionBeforePendingListIsCleared()
    {
        var controller = Create();
        var paths = new List<string> { ClipA, ClipB };

        controller.PlayPhrase(paths);
        paths.Clear();

        Assert.Equal(new[] { ClipA, ClipB }, Assert.Single(Player.Phrases));
    }

    [Fact]
    public void EmptyPhrase_LeavesCurrentPlaybackAlone()
    {
        var controller = Create();
        PlayThroughPreRoll(controller, ClipA);

        controller.PlayPhrase([]);

        Assert.Empty(Log);
        Assert.Empty(Player.Phrases);
        Assert.Equal(PlaybackPhase.Playing, controller.Phase);
    }

    [Fact]
    public void MissingFragment_ReleasesAndUnmutesWithoutPlayingPartialPhrase()
    {
        var controller = Create();
        Player.UnloadablePaths.Add(ClipB);

        controller.PlayPhrase([ClipA, ClipB]);
        Advance(500);

        Assert.Single(Errors);
        Assert.Equal(PlaybackPhase.Idle, controller.Phase);
        Assert.DoesNotContain(Log, entry => entry.StartsWith("play:"));
        Assert.Equal(["ptt:press", "mic:mute", "ptt:release", "mic:unmute"], Log);
    }

    [Fact]
    public void StopDuringPhrasePreparation_CancelsAndDisposesLateResult()
    {
        var controller = Create();
        Player.ManualPrepare = true;
        controller.PlayPhrase([ClipA, ClipB]);

        controller.Stop();
        Player.CompletePrepare(ClipA);
        Advance(500);

        Assert.True(Player.WasCancelled(ClipA));
        Assert.Equal([ClipA], Player.DisposedClips);
        Assert.Equal(PlaybackPhase.Idle, controller.Phase);
        Assert.DoesNotContain(Log, entry => entry.StartsWith("play:"));
    }

    [Fact]
    public void PushToTalkDisabled_PhraseStillPlaysWithoutPressingAKey()
    {
        var controller = Create(PlaybackOptions.Default with { PushToTalkEnabled = false });

        controller.PlayPhrase([ClipA, ClipB]);
        Advance(500);
        Player.RaiseFinished();
        Advance(500);

        Assert.DoesNotContain(Log, entry => entry.StartsWith("ptt:"));
        Assert.Contains(Play(ClipA), Log);
    }
}
