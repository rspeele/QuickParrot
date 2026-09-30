using QuickParrot.Core.Diagnostics;

namespace QuickParrot.Core.Tests.Diagnostics;

public class LoopbackTestAdviceTests
{
    [Fact]
    public void Passed_HasNoAdvice_AndDescribesEachCheck()
    {
        var result = Result(LoopbackTestVerdict.Passed) with { Peak = 0.5 };

        Assert.Empty(result.Advice);
        Assert.StartsWith("Everything works", result.Headline);
        Assert.Equal(3, result.Details.Count);
        Assert.Contains("no clipping", result.Details[2]);
    }

    [Fact]
    public void VoiceMissing_OnASilentCable_PointsAtListenToThisDevice()
    {
        var result = Result(LoopbackTestVerdict.VoiceMissing);

        Assert.Contains("Listen to this device", Assert.Single(result.Advice));
    }

    [Fact]
    public void VoiceMissing_WithBackgroundSound_SuggestsTheMicIsMutedOrQuiet()
    {
        var result = Result(LoopbackTestVerdict.VoiceMissing) with { BackgroundDb = -70 };

        Assert.Contains("muted", Assert.Single(result.Advice));
    }

    [Fact]
    public void TestSoundMissing_PointsAtTheCable()
    {
        Assert.Contains("CABLE Input", Assert.Single(Result(LoopbackTestVerdict.TestSoundMissing).Advice));
    }

    [Fact]
    public void NothingHeard_PointsAtBothTheCableAndTheMic()
    {
        var advice = Result(LoopbackTestVerdict.NothingHeard).Advice;

        Assert.Equal(2, advice.Count);
        Assert.Contains("VB-CABLE", advice[0]);
        Assert.Contains("Listen to this device", advice[1]);
    }

    [Fact]
    public void Failed_ShowsTheError_AndNoDetails()
    {
        var result = LoopbackTestResult.Failed("CABLE Output is disabled.");

        Assert.Equal("The test couldn't run.", result.Headline);
        Assert.Equal(["CABLE Output is disabled."], result.Advice);
        Assert.Empty(result.Details);
    }

    [Fact]
    public void Warnings_AreAddedToAPass()
    {
        var result = Result(LoopbackTestVerdict.Passed) with
        {
            TestSoundGainDb = -20,
            VoiceDb = -45,
            Peak = 1,
            PlaybackError = "Headphones unplugged.",
        };

        Assert.Equal(4, result.Advice.Count);
        Assert.Contains("20 dB quieter", result.Advice[0]);
        Assert.Contains("voice is quiet", result.Advice[1]);
        Assert.Contains("distorted", result.Advice[2]);
        Assert.Contains("Headphones unplugged.", result.Advice[3]);
    }

    private static LoopbackTestResult Result(LoopbackTestVerdict verdict) => new()
    {
        Verdict = verdict,
        TestSoundDetected = verdict is LoopbackTestVerdict.Passed or LoopbackTestVerdict.VoiceMissing,
        TestSoundDb = -16,
        TestSoundGainDb = verdict is LoopbackTestVerdict.Passed or LoopbackTestVerdict.VoiceMissing ? 0 : null,
        VoiceDetected = verdict is LoopbackTestVerdict.Passed or LoopbackTestVerdict.TestSoundMissing,
        VoiceDb = -22,
        VoiceSeconds = 2.5,
    };
}
