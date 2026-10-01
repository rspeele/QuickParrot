namespace QuickParrot.Core.Diagnostics;

public enum LoopbackTestVerdict
{
    Passed,
    VoiceMissing,
    TestSoundMissing,
    NothingHeard,
    Failed,
}

/// <summary>
/// What the setup test heard on the cable. Levels are dB relative to a full-scale sine (dBFS), -120 for silence.
/// </summary>
public sealed record LoopbackTestResult
{
    public const double ClippingPeak = 0.99;

    public required LoopbackTestVerdict Verdict { get; init; }

    public bool TestSoundDetected { get; init; }

    public double TestSoundDb { get; init; } = LoopbackTestSpectrum.SilenceDb;

    /// <summary>How much louder (positive) or quieter the chime arrived than it was sent; null if not heard.</summary>
    public double? TestSoundGainDb { get; init; }

    public bool VoiceDetected { get; init; }

    public double VoiceDb { get; init; } = LoopbackTestSpectrum.SilenceDb;

    public double VoiceSeconds { get; init; }

    /// <summary>The quieter moments' speech-band level: roughly the mic's hiss, or -120 for a silent cable.</summary>
    public double BackgroundDb { get; init; } = LoopbackTestSpectrum.SilenceDb;

    /// <summary>Largest absolute sample, 0 to 1 (or above, for float streams).</summary>
    public double Peak { get; init; }

    public bool Clipped => Peak >= ClippingPeak;

    /// <summary>Why the test couldn't run; set only for <see cref="LoopbackTestVerdict.Failed"/>.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Why the recording couldn't be played back to the user, if it couldn't; the analysis still stands.</summary>
    public string? PlaybackError { get; init; }

    public static LoopbackTestResult Failed(string message) =>
        new() { Verdict = LoopbackTestVerdict.Failed, ErrorMessage = message };
}
