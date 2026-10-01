using QuickParrot.Core.Dsp;
using QuickParrot.Core.Editing;

namespace QuickParrot.Core.Tests.Editing;

/// <summary>Deterministic synthetic audio for the editing DSP tests.</summary>
internal static class TestSignals
{
    public static double DbToLinear(double db) => Math.Pow(10, db / 20);

    public static double Lufs(EditableAudio audio) =>
        LoudnessMeter.IntegratedLufs(audio.Samples.Span, audio.Channels, audio.SampleRate);

    public static double TruePeakDb(float[] samples, int channels = 2) =>
        Decibels.FromAmplitude(TruePeakMeter.PeakLinear(samples, channels));

    /// <summary>A sine with the given peak level, identical on every channel.</summary>
    public static float[] Sine(double frequency, double peakDbfs, double seconds, int sampleRate = 48000, int channels = 2, double phase = 0)
    {
        var frames = (int)Math.Round(seconds * sampleRate);
        var amplitude = DbToLinear(peakDbfs);
        var samples = new float[frames * channels];
        var step = 2 * Math.PI * frequency / sampleRate;
        var (stepSin, stepCos) = Math.SinCos(step);
        double sin = 0, cos = 1;
        for (var f = 0; f < frames; f++)
        {
            // Rotate by the phase step (much cheaper than Math.Sin), resynchronising now and then to stop drift.
            if (f % 4096 == 0)
                (sin, cos) = Math.SinCos(step * f + phase);
            else
                (sin, cos) = (sin * stepCos + cos * stepSin, cos * stepCos - sin * stepSin);

            var value = (float)(amplitude * sin);
            for (var c = 0; c < channels; c++)
                samples[f * channels + c] = value;
        }

        return samples;
    }

    /// <summary>Pink noise (Paul Kellet's filter on seeded white noise), scaled to the given RMS, independent per channel.</summary>
    public static float[] PinkNoise(double rmsDbfs, double seconds, int sampleRate = 48000, int channels = 2, int seed = 7)
    {
        var frames = (int)Math.Round(seconds * sampleRate);
        var samples = new float[frames * channels];
        var random = new Random(seed);
        for (var c = 0; c < channels; c++)
        {
            double b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0, b6 = 0;
            for (var f = 0; f < frames; f++)
            {
                var white = random.NextDouble() * 2 - 1;
                b0 = 0.99886 * b0 + white * 0.0555179;
                b1 = 0.99332 * b1 + white * 0.0750759;
                b2 = 0.96900 * b2 + white * 0.1538520;
                b3 = 0.86650 * b3 + white * 0.3104856;
                b4 = 0.55000 * b4 + white * 0.5329522;
                b5 = -0.7616 * b5 - white * 0.0168980;
                samples[f * channels + c] = (float)(b0 + b1 + b2 + b3 + b4 + b5 + b6 + white * 0.5362);
                b6 = white * 0.115926;
            }
        }

        var sum = 0.0;
        foreach (var s in samples)
            sum += (double)s * s;

        var rms = Math.Sqrt(sum / samples.Length);
        var gain = (float)(DbToLinear(rmsDbfs) / rms);
        for (var i = 0; i < samples.Length; i++)
            samples[i] *= gain;

        return samples;
    }

    public static float[] Silence(double seconds, int sampleRate = 48000, int channels = 2) =>
        new float[(int)Math.Round(seconds * sampleRate) * channels];

    public static float[] Concat(params float[][] parts)
    {
        var result = new float[parts.Sum(p => p.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }

    public static float[] Scaled(float[] samples, double gainDb)
    {
        var gain = (float)DbToLinear(gainDb);
        var result = new float[samples.Length];
        for (var i = 0; i < samples.Length; i++)
            result[i] = samples[i] * gain;

        return result;
    }

    public static EditableAudio Audio(float[] samples, int sampleRate = 48000, int channels = 2) =>
        new(samples, sampleRate, channels);
}
