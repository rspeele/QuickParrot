namespace QuickParrot.Core.Diagnostics;

/// <summary>Judges a recording of the cable taken while the test chime played and the user spoke.</summary>
public static class LoopbackTestAnalyzer
{
    public const double MinRecordingSeconds = 1;

    /// <param name="samples">Mono recording of the cable's capture side.</param>
    /// <param name="signal">The chime that was played into the cable during the recording.</param>
    public static LoopbackTestResult Analyze(ReadOnlySpan<float> samples, int sampleRate, LoopbackTestSignal signal)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 8000);
        if (samples.Length < MinRecordingSeconds * sampleRate)
            return LoopbackTestResult.Failed("The recording from CABLE Output was too short to check.");

        var peak = 0f;
        foreach (var sample in samples)
            peak = Math.Max(peak, Math.Abs(sample));

        var chime = LoopbackTestChimeDetector.Detect(samples, sampleRate, signal);
        var voice = LoopbackTestVoiceDetector.Detect(samples, sampleRate, signal.Frequencies);
        var verdict = (chime.Detected, voice.Detected) switch
        {
            (true, true) => LoopbackTestVerdict.Passed,
            (true, false) => LoopbackTestVerdict.VoiceMissing,
            (false, true) => LoopbackTestVerdict.TestSoundMissing,
            _ => LoopbackTestVerdict.NothingHeard,
        };

        return new LoopbackTestResult
        {
            Verdict = verdict,
            TestSoundDetected = chime.Detected,
            TestSoundDb = chime.LevelDb,
            TestSoundGainDb = chime.GainDb,
            VoiceDetected = voice.Detected,
            VoiceDb = voice.LevelDb,
            VoiceSeconds = voice.VoiceSeconds,
            BackgroundDb = voice.BackgroundDb,
            Peak = peak,
        };
    }
}
