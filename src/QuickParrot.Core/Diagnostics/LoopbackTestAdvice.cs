using System.Globalization;
using QuickParrot.Core.Dsp;

namespace QuickParrot.Core.Diagnostics;

/// <summary>Plain-language wording of a <see cref="LoopbackTestResult"/>, ready for display.</summary>
public sealed record LoopbackTestReport(string Headline, IReadOnlyList<string> Details, IReadOnlyList<string> Advice);

/// <summary>Builds a <see cref="LoopbackTestReport"/> from a <see cref="LoopbackTestResult"/>.</summary>
public static class LoopbackTestAdvice
{
    public const double QuietTestSoundGainDb = -12;
    public const double QuietVoiceDb = -40;

    // A digitally silent cable reads -120; any real mic's hiss sits well above this.
    public const double AudibleBackgroundDb = -90;

    public static LoopbackTestReport Describe(LoopbackTestResult result) =>
        new(Headline(result), Details(result), Advice(result));

    private static string Headline(LoopbackTestResult result) => result.Verdict switch
    {
        LoopbackTestVerdict.Passed => "Everything works: the game hears your voice and QuickParrot's sounds.",
        LoopbackTestVerdict.VoiceMissing => "The game hears QuickParrot's sounds, but not your voice.",
        LoopbackTestVerdict.TestSoundMissing => "The game hears your voice, but not QuickParrot's sounds.",
        LoopbackTestVerdict.NothingHeard => "The game would hear nothing: neither your voice nor QuickParrot's sounds.",
        _ => "The test couldn't run.",
    };

    private static IReadOnlyList<string> Details(LoopbackTestResult result)
    {
        if (result.Verdict == LoopbackTestVerdict.Failed)
            return [];

        var peakDb = Decibels.FromAmplitude(result.Peak, LoopbackTestSpectrum.SilenceDb);
        return
        [
            result.TestSoundDetected ? $"Test sound: heard ({Db(result.TestSoundDb)})" : "Test sound: not heard",
            result.VoiceDetected
                ? $"Voice: heard for {result.VoiceSeconds.ToString("0.0", CultureInfo.CurrentCulture)} s ({Db(result.VoiceDb)})"
                : "Voice: not heard",
            result.Clipped ? $"Peak: {Db(peakDb)}, clipping" : $"Peak: {Db(peakDb)}, no clipping",
        ];
    }

    private static IReadOnlyList<string> Advice(LoopbackTestResult result)
    {
        var advice = new List<string>();
        switch (result.Verdict)
        {
            case LoopbackTestVerdict.Failed:
                advice.Add(result.ErrorMessage ?? "Something went wrong.");
                return advice;
            case LoopbackTestVerdict.VoiceMissing:
                advice.Add(MissingVoice(result));
                break;
            case LoopbackTestVerdict.TestSoundMissing:
                advice.Add(
                    "Sounds played into CABLE Input aren't reaching CABLE Output. Check that VB-CABLE is installed and "
                    + "working, that CABLE Input's volume is up, and that QuickParrot isn't muted for CABLE Input in the "
                    + "Windows volume mixer.");
                break;
            case LoopbackTestVerdict.NothingHeard:
                advice.Add(
                    "Nothing is coming out of CABLE Output. Check that VB-CABLE is installed and enabled and that CABLE "
                    + "Input and CABLE Output aren't muted (see Diagnostics).");
                advice.Add(MissingVoice(result));
                break;
        }

        if (result.TestSoundGainDb < QuietTestSoundGainDb)
        {
            advice.Add(
                $"QuickParrot's sounds arrived {-result.TestSoundGainDb:0} dB quieter than they were sent. Check the "
                + "volume of CABLE Input and CABLE Output, and QuickParrot's level in the Windows volume mixer.");
        }

        if (result.VoiceDetected && result.VoiceDb < QuietVoiceDb)
            advice.Add("Your voice is quiet on the cable. Consider raising your mic's level.");
        if (result.Clipped)
            advice.Add("The recording hit full volume and distorted. Turn your mic's level down a little.");
        if (result.PlaybackError is { } playbackError)
            advice.Add($"Couldn't play the recording back to you: {playbackError}");

        return advice;
    }

    private static string MissingVoice(LoopbackTestResult result) =>
        result.BackgroundDb > AudibleBackgroundDb
            ? "QuickParrot heard faint sound on the cable but no speech. If you spoke, your mic may be muted or its "
              + "level too low."
            : "Your mic isn't reaching the cable. Check that \"Listen to this device\" is on for your mic, playing "
              + "to CABLE Input (see Diagnostics).";

    private static string Db(double db) => $"{db.ToString("0", CultureInfo.CurrentCulture)} dBFS";
}
