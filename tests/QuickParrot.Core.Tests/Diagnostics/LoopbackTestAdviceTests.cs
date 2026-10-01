using QuickParrot.Core.Diagnostics;

namespace QuickParrot.Core.Tests.Diagnostics;

public class LoopbackTestAdviceTests
{
    [Fact]
    public void Passed_HasNoAdvice_AndDescribesEachCheck()
    {
        var report = LoopbackTestAdvice.Describe(Result(LoopbackTestVerdict.Passed) with { Peak = 0.5 });

        Assert.Empty(report.Advice);
        Assert.StartsWith("Everything works", report.Headline);
        Assert.Equal(3, report.Details.Count);
        Assert.Contains("no clipping", report.Details[2]);
    }

    [Fact]
    public void VoiceMissing_OnASilentCable_PointsAtListenToThisDevice()
    {
        var report = LoopbackTestAdvice.Describe(Result(LoopbackTestVerdict.VoiceMissing));

        Assert.Contains("Listen to this device", Assert.Single(report.Advice));
    }

    [Fact]
    public void VoiceMissing_WithBackgroundSound_SuggestsTheMicIsMutedOrQuiet()
    {
        var report = LoopbackTestAdvice.Describe(Result(LoopbackTestVerdict.VoiceMissing) with { BackgroundDb = -70 });

        Assert.Contains("muted", Assert.Single(report.Advice));
    }

    [Fact]
    public void TestSoundMissing_PointsAtTheCable()
    {
        var report = LoopbackTestAdvice.Describe(Result(LoopbackTestVerdict.TestSoundMissing));

        Assert.Contains("CABLE Input", Assert.Single(report.Advice));
    }

    [Fact]
    public void NothingHeard_PointsAtBothTheCableAndTheMic()
    {
        var advice = LoopbackTestAdvice.Describe(Result(LoopbackTestVerdict.NothingHeard)).Advice;

        Assert.Equal(2, advice.Count);
        Assert.Contains("VB-CABLE", advice[0]);
        Assert.Contains("Listen to this device", advice[1]);
    }

    [Fact]
    public void Failed_ShowsTheError_AndNoDetails()
    {
        var report = LoopbackTestAdvice.Describe(LoopbackTestResult.Failed("CABLE Output is disabled."));

        Assert.Equal("The test couldn't run.", report.Headline);
        Assert.Equal(["CABLE Output is disabled."], report.Advice);
        Assert.Empty(report.Details);
    }

    [Fact]
    public void Warnings_AreAddedToAPass()
    {
        var report = LoopbackTestAdvice.Describe(Result(LoopbackTestVerdict.Passed) with
        {
            TestSoundGainDb = -20,
            VoiceDb = -45,
            Peak = 1,
            PlaybackError = "Headphones unplugged.",
        });

        Assert.Equal(4, report.Advice.Count);
        Assert.Contains("20 dB quieter", report.Advice[0]);
        Assert.Contains("voice is quiet", report.Advice[1]);
        Assert.Contains("distorted", report.Advice[2]);
        Assert.Contains("Headphones unplugged.", report.Advice[3]);
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
