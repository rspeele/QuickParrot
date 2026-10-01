using QuickParrot.Core.Diagnostics;
using static QuickParrot.Core.Tests.Diagnostics.LoopbackTestAudio;

namespace QuickParrot.Core.Tests.Diagnostics;

public class LoopbackTestAnalyzerTests
{
    private const int Rate = 48000;
    private const double Seconds = 5;
    private const double SpeechRms = 0.05;

    private static LoopbackTestResult Analyze(float[] samples, int rate = Rate) =>
        LoopbackTestAnalyzer.Analyze(samples, rate, LoopbackTestSignal.Default);

    [Fact]
    public void Silence_HearsNothing()
    {
        var result = Analyze(Silence(Rate, Seconds));

        Assert.Equal(LoopbackTestVerdict.NothingHeard, result.Verdict);
        Assert.False(result.TestSoundDetected);
        Assert.False(result.VoiceDetected);
        Assert.False(result.Clipped);
        Assert.Equal(-120, result.BackgroundDb);
    }

    [Fact]
    public void ChimeOnly_HearsTestSoundButNoVoice()
    {
        var result = Analyze(WithChime(Silence(Rate, Seconds), Rate, 0.5));

        Assert.Equal(LoopbackTestVerdict.VoiceMissing, result.Verdict);
        Assert.True(result.TestSoundDetected);
        Assert.Equal(0, result.VoiceSeconds);
        Assert.InRange(result.TestSoundGainDb!.Value, -1, 1);
    }

    [Fact]
    public void VoiceOnly_HearsVoiceButNoTestSound()
    {
        var result = Analyze(WithVoice(Silence(Rate, Seconds), Rate, SpeechRms));

        Assert.Equal(LoopbackTestVerdict.TestSoundMissing, result.Verdict);
        Assert.True(result.VoiceSeconds > 2, $"voice for {result.VoiceSeconds} s");
        Assert.InRange(result.VoiceDb, -30, -10);
    }

    [Fact]
    public void NoiseBurstsOnly_CountAsVoice()
    {
        var result = Analyze(WithNoiseBursts(Silence(Rate, Seconds), Rate, SpeechRms));

        Assert.Equal(LoopbackTestVerdict.TestSoundMissing, result.Verdict);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void VoiceAndChime_HearsBoth(int seed)
    {
        var recording = WithChime(WithVoice(Silence(Rate, Seconds), Rate, SpeechRms, seed: seed), Rate, 0.4);

        var result = Analyze(WithHiss(recording, 0.0003));

        Assert.Equal(LoopbackTestVerdict.Passed, result.Verdict);
        Assert.Empty(LoopbackTestAdvice.Describe(result).Advice);
    }

    [Fact]
    public void ContinuousLoudSpeech_StillCountsAsVoice()
    {
        var recording = WithVoice(WithVoice(Silence(Rate, Seconds), Rate, SpeechRms, 0, seed: 4), Rate, SpeechRms, 0.1, 5);

        Assert.True(Analyze(recording).VoiceDetected);
    }

    [Fact]
    public void QuietChimeUnderHiss_IsStillDetected_AndReportedAsQuiet()
    {
        var recording = WithHiss(WithChime(Silence(Rate, Seconds), Rate, 0.5, gain: 0.03), 0.001);

        var result = Analyze(recording);

        Assert.True(result.TestSoundDetected);
        Assert.InRange(result.TestSoundGainDb!.Value, -32, -28);
        Assert.Contains(LoopbackTestAdvice.Describe(result).Advice, a => a.Contains("quieter"));
    }

    [Fact]
    public void HissOnly_IsNotVoice_ButCountsAsBackground()
    {
        var result = Analyze(WithHiss(Silence(Rate, Seconds), 0.0005));

        Assert.Equal(LoopbackTestVerdict.NothingHeard, result.Verdict);
        Assert.True(result.BackgroundDb > LoopbackTestAdvice.AudibleBackgroundDb);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.013)]
    [InlineData(0.9871)]
    [InlineData(1.5)]
    public void Chime_IsFoundAtAnyDelay(double startSeconds)
    {
        var result = Analyze(WithChime(Silence(Rate, Seconds), Rate, startSeconds));

        Assert.True(result.TestSoundDetected);
    }

    [Fact]
    public void ChimeCutOffByTheRecordingsEnd_IsStillFound()
    {
        Assert.True(Analyze(WithChime(Silence(Rate, Seconds), Rate, 2.8)).TestSoundDetected);
    }

    [Fact]
    public void ChimeResampledFrom48kTo44k_IsFoundWithoutCountingAsVoice()
    {
        var recording = Resample(WithChime(Silence(Rate, Seconds), Rate, 0.61), Rate, 44100);

        var result = Analyze(recording, 44100);

        Assert.True(result.TestSoundDetected);
        Assert.False(result.VoiceDetected);
    }

    [Fact]
    public void VoiceAndChimeRenderedAt44k_HearsBoth()
    {
        var recording = WithChime(WithVoice(Silence(44100, Seconds), 44100, SpeechRms), 44100, 0.3);

        Assert.Equal(LoopbackTestVerdict.Passed, Analyze(recording, 44100).Verdict);
    }

    [Theory]
    [InlineData(1318.51)]
    [InlineData(1567.98)]
    public void SteadyToneAtOneChimePitch_IsNotTheChime(double frequencyHz)
    {
        var result = Analyze(WithTone(Silence(Rate, Seconds), Rate, frequencyHz, 0.1));

        Assert.False(result.TestSoundDetected);
    }

    [Fact]
    public void Clipping_IsReported()
    {
        var recording = WithVoice(Silence(Rate, Seconds), Rate, SpeechRms);
        for (var i = 0; i < recording.Length; i++)
            recording[i] = Math.Clamp(recording[i] * 20, -1f, 1f);

        var result = Analyze(recording);

        Assert.True(result.Clipped);
        Assert.Contains(LoopbackTestAdvice.Describe(result).Advice, a => a.Contains("distorted"));
    }

    [Fact]
    public void TooShortRecording_Fails()
    {
        var result = Analyze(Silence(Rate, 0.5));

        Assert.Equal(LoopbackTestVerdict.Failed, result.Verdict);
        Assert.NotNull(result.ErrorMessage);
    }
}
