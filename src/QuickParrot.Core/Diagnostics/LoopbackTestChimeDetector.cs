using QuickParrot.Core.Dsp;
using static QuickParrot.Core.Diagnostics.LoopbackTestSpectrum;

namespace QuickParrot.Core.Diagnostics;

/// <summary>
/// Finds the test chime in a recording: frames where a chime pitch stands well above its spectral neighbours,
/// arranged in the chime's note pattern at some unknown delay.
/// </summary>
internal static class LoopbackTestChimeDetector
{
    private const double WindowSeconds = 0.08;
    private const double HopSeconds = 0.02;
    private const double MinToneAboveNeighboursDb = 15;
    private const double MinToneDb = -70;
    private const double MinMatchFraction = 0.5;
    private const double MinMatchFractionPerPitch = 0.3;

    // Beyond the 80 ms Hann window's main lobe (±25 Hz); speech harmonics are rarely this narrow.
    private static readonly double[] NeighbourOffsetsHz = [-100, -50, 50, 100];

    public readonly record struct Detection(bool Detected, double LevelDb, double? GainDb);

    public static Detection Detect(ReadOnlySpan<float> samples, int sampleRate, LoopbackTestSignal signal)
    {
        var windowLength = (int)Math.Round(WindowSeconds * sampleRate);
        var hop = (int)Math.Round(HopSeconds * sampleRate);
        if (samples.Length < windowLength)
            return new Detection(false, SilenceDb, null);

        var frameCount = (samples.Length - windowLength) / hop + 1;
        var tones = MeasureTones(samples, sampleRate, signal.Frequencies, windowLength, hop, frameCount);
        var template = BuildTemplate(signal, windowLength / (double)sampleRate, hop / (double)sampleRate);

        var (bestOffset, bestFraction, bestPitchFraction) = (0, -1.0, 0.0);
        for (var offset = -template[^1].Frame; offset < frameCount; offset++)
        {
            var (fraction, weakestPitch) = Score(template, tones, offset, signal.Frequencies.Count);
            if (fraction > bestFraction)
                (bestOffset, bestFraction, bestPitchFraction) = (offset, fraction, weakestPitch);
        }

        var detected = bestFraction >= MinMatchFraction && bestPitchFraction >= MinMatchFractionPerPitch;
        if (!detected)
            return new Detection(false, SilenceDb, null);

        var levels = new List<double>();
        var gains = new List<double>();
        foreach (var slot in template)
        {
            var frame = bestOffset + slot.Frame;
            if (frame < 0 || frame >= frameCount || !tones[slot.Pitch][frame].Present)
                continue;

            levels.Add(tones[slot.Pitch][frame].Amplitude);
            gains.Add(tones[slot.Pitch][frame].Amplitude / slot.ExpectedAmplitude);
        }

        return new Detection(true, Decibels.FromAmplitude(Percentile(levels, 0.5), SilenceDb), Decibels.FromAmplitude(Percentile(gains, 0.5), SilenceDb));
    }

    private static Tone[][] MeasureTones(
        ReadOnlySpan<float> samples, int sampleRate, IReadOnlyList<double> pitches, int windowLength, int hop, int frameCount)
    {
        var window = RaisedCosine.Hann(windowLength);
        var coherentGain = window.Sum();
        var minAmplitude = Decibels.ToAmplitude(MinToneDb);
        var minRatio = Decibels.ToPower(MinToneAboveNeighboursDb);
        var tones = pitches.Select(_ => new Tone[frameCount]).ToArray();
        var frame = new double[windowLength];
        var perPitch = NeighbourOffsetsHz.Length + 1;
        var coefficients = new double[pitches.Count * perPitch];
        for (var p = 0; p < pitches.Count; p++)
        {
            coefficients[p * perPitch] = GoertzelCoefficient(pitches[p], sampleRate);
            for (var n = 0; n < NeighbourOffsetsHz.Length; n++)
                coefficients[p * perPitch + n + 1] = GoertzelCoefficient(pitches[p] + NeighbourOffsetsHz[n], sampleRate);
        }

        var powers = new double[coefficients.Length];
        for (var f = 0; f < frameCount; f++)
        {
            var start = f * hop;
            for (var i = 0; i < windowLength; i++)
                frame[i] = samples[start + i] * window[i];

            GoertzelPowers(frame, coefficients, powers);
            for (var p = 0; p < pitches.Count; p++)
            {
                var power = powers[p * perPitch];
                var neighbours = powers.AsSpan(p * perPitch + 1, NeighbourOffsetsHz.Length);

                // The two quietest neighbours, so one stray speech harmonic nearby doesn't mask the tone.
                neighbours.Sort();
                var reference = (neighbours[0] + neighbours[1]) / 2;
                var amplitude = 2 * Math.Sqrt(power) / coherentGain;
                tones[p][f] = new Tone(amplitude, amplitude >= minAmplitude && power >= reference * minRatio);
            }
        }

        return tones;
    }

    // Only frames lying wholly inside a note (with half a hop to spare for the unknown sub-hop delay) are scored.
    private static List<Slot> BuildTemplate(LoopbackTestSignal signal, double windowSeconds, double hopSeconds)
    {
        var slots = new List<Slot>();
        var margin = hopSeconds / 2;
        foreach (var note in signal.Notes)
        {
            var pitch = IndexOf(signal.Frequencies, note.FrequencyHz);
            var first = (int)Math.Ceiling((note.StartSeconds + margin) / hopSeconds);
            for (var frame = first; frame * hopSeconds + windowSeconds <= note.EndSeconds - margin; frame++)
            {
                var center = frame * hopSeconds + windowSeconds / 2 - note.StartSeconds;
                slots.Add(new Slot(frame, pitch, signal.AmplitudeAt(note, center)));
            }
        }

        if (slots.Count == 0)
            throw new ArgumentException("The chime's notes are too short to detect.", nameof(signal));

        return slots.OrderBy(s => s.Frame).ToList();
    }

    private static (double Fraction, double WeakestPitch) Score(List<Slot> template, Tone[][] tones, int offset, int pitchCount)
    {
        Span<int> hits = stackalloc int[pitchCount];
        Span<int> totals = stackalloc int[pitchCount];
        var frameCount = tones[0].Length;
        foreach (var slot in template)
        {
            totals[slot.Pitch]++;
            var frame = offset + slot.Frame;
            if (frame >= 0 && frame < frameCount && tones[slot.Pitch][frame].Present)
                hits[slot.Pitch]++;
        }

        var (allHits, weakest) = (0, 1.0);
        for (var p = 0; p < pitchCount; p++)
        {
            allHits += hits[p];
            if (totals[p] > 0)
                weakest = Math.Min(weakest, hits[p] / (double)totals[p]);
        }

        return (allHits / (double)template.Count, weakest);
    }

    private static int IndexOf(IReadOnlyList<double> values, double value)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] == value)
                return i;
        }

        return -1;
    }

    private readonly record struct Tone(double Amplitude, bool Present);

    private readonly record struct Slot(int Frame, int Pitch, double ExpectedAmplitude);
}
