using QuickParrot.Core.Diagnostics;

namespace QuickParrot.Core.Tests.Diagnostics;

public class ClipLevelAnalyzerTests
{
    private const int SourceRate = 44100;
    private const int RecordingRate = 48000;
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(0.8);
    private static readonly TimeSpan SearchFrom = TimeSpan.FromSeconds(0.5);

    [Fact]
    public void UnchangedLevel_AtADifferentRate_ReportsNoDropAndUnityGain()
    {
        var analysis = Analyze(Recording(_ => 1));

        Assert.Equal(ClipLevelVerdict.NoDrop, analysis.Verdict);
        Assert.InRange(analysis.GainDb!.Value, -1, 1);
        Assert.Equal("No level drop detected.", analysis.Summary);
        Assert.Contains("as loud as the file", analysis.GainNote);
    }

    [Fact]
    public void ConstantlyQuieterRecording_ReportsTheGainButNoDrop()
    {
        var analysis = Analyze(Recording(_ => 0.5));

        Assert.Equal(ClipLevelVerdict.NoDrop, analysis.Verdict);
        Assert.InRange(analysis.GainDb!.Value, -7, -5);
        Assert.Contains("6 dB quieter than the file", analysis.GainNote);
    }

    [Fact]
    public void LevelTurnedDownPartWayThrough_ReportsTheDropAndWhen()
    {
        var analysis = Analyze(Recording(t => t < 1.5 ? 1 : t < 1.55 ? 1 - (t - 1.5) / 0.05 * 0.75 : 0.25));

        Assert.Equal(ClipLevelVerdict.Dropped, analysis.Verdict);
        Assert.InRange(analysis.DropDb!.Value, 10, 14);
        Assert.InRange(analysis.DropAfter!.Value.TotalSeconds, 1.3, 1.8);
        Assert.InRange(analysis.GainDb!.Value, -1, 1);
        Assert.StartsWith("Level dropped by ~12 dB about 1.", analysis.Summary);
    }

    [Fact]
    public void RecordingThatGoesSilentWhileTheClipIsStillPlaying_SaysItStoppedEarly()
    {
        var analysis = Analyze(Recording(t => t < 1.5 ? 1 : 0));

        Assert.Equal(ClipLevelVerdict.StoppedEarly, analysis.Verdict);
        Assert.InRange(analysis.DropAfter!.Value.TotalSeconds, 1.3, 1.8);
        Assert.Contains("stopped", analysis.Summary);
    }

    [Fact]
    public void ClipLongerThanTheRecording_IsOnlyJudgedOnWhatTheRecordingHolds()
    {
        // A quiet intro, then a part 34 dB louder that the capped recording never reached.
        static double Gain(double t) => t < 2 ? 0.02 : 1;
        var source = Signal(SourceRate, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(0.2), Gain);
        var recording = Signal(RecordingRate, TimeSpan.FromSeconds(1.5), Delay + TimeSpan.FromSeconds(0.2), Gain, tail: false);

        var analysis = ClipLevelAnalyzer.Analyze(source, recording, SearchFrom);

        Assert.Equal(ClipLevelVerdict.NoDrop, analysis.Verdict);
        Assert.InRange(analysis.GainDb!.Value, -1, 1);
    }

    [Fact]
    public void SilentRecording_NothingCameThrough()
    {
        var analysis = ClipLevelAnalyzer.Analyze(Source(), new MonoAudio(new float[RecordingRate * 4], RecordingRate), SearchFrom);

        Assert.Equal(ClipLevelVerdict.NothingCameThrough, analysis.Verdict);
    }

    [Fact]
    public void SilentClip_HasNothingToCompare()
    {
        var silent = new MonoAudio(new float[SourceRate * 2], SourceRate);

        var analysis = ClipLevelAnalyzer.Analyze(silent, Recording(_ => 1), SearchFrom);

        Assert.Equal(ClipLevelVerdict.SilentClip, analysis.Verdict);
    }

    [Fact]
    public void LoudSoundBeforeTheClipCouldStart_IsIgnored()
    {
        var recording = Recording(_ => 1);
        for (var i = 0; i < RecordingRate / 5; i++)
            recording.Samples[RecordingRate / 10 + i] = (float)(0.9 * Math.Sin(i * 0.3)); // e.g. the mic before muting

        var analysis = Analyze(recording);

        Assert.Equal(ClipLevelVerdict.NoDrop, analysis.Verdict);
        Assert.InRange(analysis.GainDb!.Value, -1, 1);
    }

    [Fact]
    public void ClipShorterThanOneWindow_IsTooShortToJudgeRatherThanSilent()
    {
        var source = new MonoAudio(Enumerable.Repeat(0.3f, SourceRate * 3 / 100).ToArray(), SourceRate);
        var recording = new MonoAudio(new float[RecordingRate * 2], RecordingRate);
        Array.Fill(recording.Samples, 0.3f, RecordingRate, RecordingRate * 3 / 100);

        var analysis = ClipLevelAnalyzer.Analyze(source, recording, SearchFrom);

        Assert.Equal(ClipLevelVerdict.TooShortToJudge, analysis.Verdict);
    }

    [Fact]
    public void VeryShortClip_IsTooShortToJudge()
    {
        var source = Signal(SourceRate, TimeSpan.FromSeconds(0.1), TimeSpan.Zero, _ => 1);
        var recording = Signal(RecordingRate, TimeSpan.FromSeconds(0.1), Delay, _ => 1);

        var analysis = ClipLevelAnalyzer.Analyze(source, recording, SearchFrom);

        Assert.Equal(ClipLevelVerdict.TooShortToJudge, analysis.Verdict);
    }

    private static ClipLevelAnalysis Analyze(MonoAudio recording) =>
        ClipLevelAnalyzer.Analyze(Source(), recording, SearchFrom);

    private static MonoAudio Source() => Signal(SourceRate, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(0.2), _ => 1);

    // The source's 3 s of sound delayed on the cable, with a gain over time measured from the sound's start.
    private static MonoAudio Recording(Func<double, double> gain) =>
        Signal(RecordingRate, TimeSpan.FromSeconds(3), Delay + TimeSpan.FromSeconds(0.2), gain);

    // A 300 Hz tone pulsing three times a second, roughly like syllables, after some silence and before half a second more.
    private static MonoAudio Signal(int rate, TimeSpan length, TimeSpan silence, Func<double, double> gain, bool tail = true)
    {
        var lead = (int)(silence.TotalSeconds * rate);
        var samples = new float[lead + (int)(length.TotalSeconds * rate) + (tail ? rate / 2 : 0)];
        for (var i = 0; i < (int)(length.TotalSeconds * rate); i++)
        {
            var t = i / (double)rate;
            var pulse = 0.5 + 0.5 * Math.Sin(2 * Math.PI * 3 * t - Math.PI / 2);
            samples[lead + i] = (float)(0.3 * gain(t) * pulse * Math.Sin(2 * Math.PI * 300 * t));
        }

        return new MonoAudio(samples, rate);
    }
}
