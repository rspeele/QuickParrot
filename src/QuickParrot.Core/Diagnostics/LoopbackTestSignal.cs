namespace QuickParrot.Core.Diagnostics;

/// <summary>One tone of the test chime, timed from the start of the chime.</summary>
public sealed record LoopbackTestNote(double FrequencyHz, double StartSeconds, double DurationSeconds)
{
    public double EndSeconds => StartSeconds + DurationSeconds;
}

/// <summary>
/// The setup test's "ding-dong" chime, played twice. Pure sines at fixed pitches, so the analyzer can pick them out
/// from speech by their narrow spectral peaks and their timing.
/// </summary>
public sealed class LoopbackTestSignal
{
    private const double AttackSeconds = 0.012;
    private const double ReleaseSeconds = 0.04;
    private const double DecaySeconds = 0.6;

    public LoopbackTestSignal(IReadOnlyList<LoopbackTestNote> notes, double peakAmplitude)
    {
        if (notes.Count == 0)
            throw new ArgumentException("The chime needs at least one note.", nameof(notes));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(peakAmplitude);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(peakAmplitude, 1.0);

        Notes = notes;
        PeakAmplitude = peakAmplitude;
        DurationSeconds = notes.Max(n => n.EndSeconds);
        Frequencies = notes.Select(n => n.FrequencyHz).Distinct().Order().ToArray();
    }

    /// <summary>G6 then E6, twice, at -14 dBFS peak.</summary>
    public static LoopbackTestSignal Default { get; } = new(
        [
            new(1567.98, 0.00, 0.60),
            new(1318.51, 0.60, 0.85),
            new(1567.98, 1.95, 0.60),
            new(1318.51, 2.55, 0.85),
        ],
        0.2);

    public IReadOnlyList<LoopbackTestNote> Notes { get; }

    public double PeakAmplitude { get; }

    public double DurationSeconds { get; }

    /// <summary>The distinct note pitches, lowest first.</summary>
    public IReadOnlyList<double> Frequencies { get; }

    /// <summary>The note's sine amplitude at a time from its own start: quick attack, bell-like decay, soft release.</summary>
    public double AmplitudeAt(LoopbackTestNote note, double secondsIntoNote)
    {
        var t = secondsIntoNote;
        if (t < 0 || t > note.DurationSeconds)
            return 0;

        var remaining = note.DurationSeconds - t;
        var attack = t < AttackSeconds ? RaisedCosine(t / AttackSeconds) : 1;
        var release = remaining < ReleaseSeconds ? RaisedCosine(remaining / ReleaseSeconds) : 1;
        return PeakAmplitude * Math.Exp(-t / DecaySeconds) * attack * release;
    }

    /// <summary>Renders the chime as mono samples; the same rate always gives the same samples.</summary>
    public float[] Render(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 8000);
        var samples = new float[(int)Math.Ceiling(DurationSeconds * sampleRate)];
        foreach (var note in Notes)
        {
            var first = (int)Math.Round(note.StartSeconds * sampleRate);
            var end = Math.Min(samples.Length, (int)Math.Ceiling(note.EndSeconds * sampleRate));
            var step = 2 * Math.PI * note.FrequencyHz / sampleRate;
            for (var i = first; i < end; i++)
            {
                var n = i - first;
                samples[i] += (float)(AmplitudeAt(note, (double)n / sampleRate) * Math.Sin(step * n));
            }
        }

        return samples;
    }

    private static double RaisedCosine(double fraction) => 0.5 - 0.5 * Math.Cos(Math.PI * fraction);
}
