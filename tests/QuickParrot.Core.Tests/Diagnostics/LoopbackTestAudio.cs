using QuickParrot.Core.Diagnostics;

namespace QuickParrot.Core.Tests.Diagnostics;

/// <summary>Deterministic synthetic recordings for the setup-test analyzer.</summary>
internal static class LoopbackTestAudio
{
    public static float[] Silence(int sampleRate, double seconds) => new float[(int)(sampleRate * seconds)];

    public static float[] WithChime(float[] recording, int sampleRate, double startSeconds, double gain = 1)
    {
        var chime = LoopbackTestSignal.Default.Render(sampleRate);
        var start = (int)Math.Round(startSeconds * sampleRate);
        for (var i = 0; i < chime.Length && start + i < recording.Length; i++)
            recording[start + i] += (float)(chime[i] * gain);

        return recording;
    }

    /// <summary>
    /// Voiced "syllables": a gliding harmonic series with a speech-like spectral tilt, gated on and off, at roughly
    /// the given RMS during speech.
    /// </summary>
    public static float[] WithVoice(float[] recording, int sampleRate, double rms, double startSeconds = 0.2, int seed = 1)
    {
        var random = new Random(seed);
        var syllables = SyllableGate(recording.Length, sampleRate, startSeconds, random);
        var harmonicNorm = Math.Sqrt(Enumerable.Range(1, 30).Sum(k => 1.0 / (k * k)) / 2);
        var phase = 0.0;
        for (var i = 0; i < recording.Length; i++)
        {
            var t = i / (double)sampleRate;
            var pitch = 140 + 40 * Math.Sin(2 * Math.PI * 0.7 * t) + 6 * Math.Sin(2 * Math.PI * 5.5 * t);
            phase += 2 * Math.PI * pitch / sampleRate;
            // sin(k·phase) by the Chebyshev recurrence, much cheaper than 30 Math.Sin calls per sample.
            var (sin, cos) = Math.SinCos(phase);
            double sample = 0, previous = 0, current = sin;
            for (var k = 1; k <= 30 && k * pitch < 4000; k++)
            {
                sample += current / k;
                (previous, current) = (current, 2 * cos * current - previous);
            }

            recording[i] += (float)(syllables[i] * rms / harmonicNorm * sample);
        }

        return recording;
    }

    /// <summary>Unvoiced "fricative" bursts: noise band-limited to roughly 300-3000 Hz.</summary>
    public static float[] WithNoiseBursts(float[] recording, int sampleRate, double rms, int seed = 2)
    {
        var random = new Random(seed);
        var syllables = SyllableGate(recording.Length, sampleRate, 0.3, random);
        var noise = BandNoise(recording.Length, sampleRate, 300, 3000, random);
        for (var i = 0; i < recording.Length; i++)
            recording[i] += (float)(syllables[i] * rms * noise[i]);

        return recording;
    }

    /// <summary>Steady white noise, like a mic's hiss.</summary>
    public static float[] WithHiss(float[] recording, double rms, int seed = 3)
    {
        var random = new Random(seed);
        for (var i = 0; i < recording.Length; i++)
            recording[i] += (float)(rms * Math.Sqrt(3) * (2 * random.NextDouble() - 1));

        return recording;
    }

    public static float[] WithTone(float[] recording, int sampleRate, double frequencyHz, double amplitude)
    {
        for (var i = 0; i < recording.Length; i++)
            recording[i] += (float)(amplitude * Math.Sin(2 * Math.PI * frequencyHz * i / sampleRate));

        return recording;
    }

    /// <summary>Resamples by cubic interpolation, as a sound card's rate conversion might.</summary>
    public static float[] Resample(float[] source, int fromRate, int toRate)
    {
        var result = new float[(int)((long)source.Length * toRate / fromRate)];
        for (var i = 0; i < result.Length; i++)
        {
            var position = i * (double)fromRate / toRate;
            var index = (int)position;
            var fraction = position - index;
            float At(int n) => source[Math.Clamp(n, 0, source.Length - 1)];
            var (p0, p1, p2, p3) = (At(index - 1), At(index), At(index + 1), At(index + 2));
            result[i] = (float)(p1 + 0.5 * fraction * (p2 - p0 + fraction * (2 * p0 - 5 * p1 + 4 * p2 - p3
                + fraction * (3 * (p1 - p2) + p3 - p0))));
        }

        return result;
    }

    // Syllables of 120-300 ms separated by 60-200 ms gaps, with 20 ms fades.
    private static double[] SyllableGate(int length, int sampleRate, double startSeconds, Random random)
    {
        var gate = new double[length];
        var position = (int)(startSeconds * sampleRate);
        var fade = (int)(0.02 * sampleRate);
        while (position < length)
        {
            var on = (int)((0.12 + 0.18 * random.NextDouble()) * sampleRate);
            for (var i = 0; i < on && position + i < length; i++)
                gate[position + i] = Math.Min(1, Math.Min(i, on - i) / (double)fade);

            position += on + (int)((0.06 + 0.14 * random.NextDouble()) * sampleRate);
        }

        return gate;
    }

    // White noise through a band-pass made of two one-pole filters, normalised to unit RMS.
    private static double[] BandNoise(int length, int sampleRate, double lowHz, double highHz, Random random)
    {
        var noise = new double[length];
        var lowAlpha = Math.Exp(-2 * Math.PI * lowHz / sampleRate);
        var highAlpha = Math.Exp(-2 * Math.PI * highHz / sampleRate);
        double lowState = 0, highState = 0, sumSquares = 0;
        for (var i = 0; i < length; i++)
        {
            var white = 2 * random.NextDouble() - 1;
            highState = (1 - highAlpha) * white + highAlpha * highState;
            lowState = (1 - lowAlpha) * highState + lowAlpha * lowState;
            noise[i] = highState - lowState;
            sumSquares += noise[i] * noise[i];
        }

        var scale = 1 / Math.Sqrt(sumSquares / length);
        for (var i = 0; i < length; i++)
            noise[i] *= scale;

        return noise;
    }
}
