namespace QuickParrot.Core.Diagnostics;

/// <summary>Small spectral helpers for the setup test's analysis. Arrays rather than spans keep debug builds fast.</summary>
internal static class LoopbackTestSpectrum
{
    public const double SilenceDb = -120;

    public static double[] Hann(int length)
    {
        var window = new double[length];
        for (var i = 0; i < length; i++)
            window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / length);

        return window;
    }

    // Sidelobes below -92 dB, so a loud tone can't leak into bins a few steps away.
    public static double[] BlackmanHarris(int length)
    {
        var window = new double[length];
        for (var i = 0; i < length; i++)
        {
            var x = 2 * Math.PI * i / length;
            window[i] = 0.35875 - 0.48829 * Math.Cos(x) + 0.14128 * Math.Cos(2 * x) - 0.01168 * Math.Cos(3 * x);
        }

        return window;
    }

    public static double GoertzelCoefficient(double frequencyHz, int sampleRate) =>
        2 * Math.Cos(2 * Math.PI * frequencyHz / sampleRate);

    /// <summary>Squared DFT magnitudes at any frequencies, not just bin centers, from one pass over the samples.</summary>
    public static void GoertzelPowers(double[] samples, double[] coefficients, double[] powers)
    {
        var i = 0;
        for (; i + 4 <= coefficients.Length; i += 4)
            GoertzelFour(samples, coefficients, powers, i);
        for (; i < coefficients.Length; i++)
            powers[i] = GoertzelOne(samples, coefficients[i]);
    }

    // Four independent recurrences side by side run several times faster than one after another.
    private static void GoertzelFour(double[] samples, double[] coefficients, double[] powers, int first)
    {
        double c0 = coefficients[first], c1 = coefficients[first + 1];
        double c2 = coefficients[first + 2], c3 = coefficients[first + 3];
        double a0 = 0, a1 = 0, a2 = 0, a3 = 0, b0 = 0, b1 = 0, b2 = 0, b3 = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var x = samples[i];
            var n0 = x + c0 * a0 - b0;
            var n1 = x + c1 * a1 - b1;
            var n2 = x + c2 * a2 - b2;
            var n3 = x + c3 * a3 - b3;
            b0 = a0;
            b1 = a1;
            b2 = a2;
            b3 = a3;
            a0 = n0;
            a1 = n1;
            a2 = n2;
            a3 = n3;
        }

        powers[first] = a0 * a0 + b0 * b0 - c0 * a0 * b0;
        powers[first + 1] = a1 * a1 + b1 * b1 - c1 * a1 * b1;
        powers[first + 2] = a2 * a2 + b2 * b2 - c2 * a2 * b2;
        powers[first + 3] = a3 * a3 + b3 * b3 - c3 * a3 * b3;
    }

    private static double GoertzelOne(double[] samples, double coefficient)
    {
        double previous = 0, beforePrevious = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var current = samples[i] + coefficient * previous - beforePrevious;
            beforePrevious = previous;
            previous = current;
        }

        return previous * previous + beforePrevious * beforePrevious - coefficient * previous * beforePrevious;
    }

    /// <summary>In-place radix-2 FFT of a power-of-two length, with tables from <see cref="Twiddles"/>.</summary>
    public static void Fft(double[] real, double[] imaginary, double[] cosTable, double[] sinTable)
    {
        var n = real.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
            }
        }

        for (var size = 2; size <= n; size <<= 1)
        {
            var half = size / 2;
            var stride = n / size;
            for (var start = 0; start < n; start += size)
            {
                for (var k = 0; k < half; k++)
                {
                    var cos = cosTable[k * stride];
                    var sin = sinTable[k * stride];
                    var a = start + k;
                    var b = a + half;
                    var tr = real[b] * cos - imaginary[b] * sin;
                    var ti = real[b] * sin + imaginary[b] * cos;
                    real[b] = real[a] - tr;
                    imaginary[b] = imaginary[a] - ti;
                    real[a] += tr;
                    imaginary[a] += ti;
                }
            }
        }
    }

    public static (double[] Cos, double[] Sin) Twiddles(int length)
    {
        var cos = new double[length / 2];
        var sin = new double[length / 2];
        for (var k = 0; k < cos.Length; k++)
            (sin[k], cos[k]) = Math.SinCos(-2 * Math.PI * k / length);

        return (cos, sin);
    }

    public static int NextPowerOfTwo(int value)
    {
        var result = 1;
        while (result < value)
            result <<= 1;

        return result;
    }

    /// <summary>Level of a sine with this amplitude, in dB relative to a full-scale sine.</summary>
    public static double AmplitudeToDb(double amplitude) =>
        amplitude > 0 ? Math.Max(SilenceDb, 20 * Math.Log10(amplitude)) : SilenceDb;

    /// <summary>Mean-square power in dB relative to a full-scale sine (mean square 0.5).</summary>
    public static double MeanSquareToDb(double meanSquare) =>
        meanSquare > 0 ? Math.Max(SilenceDb, 10 * Math.Log10(meanSquare / 0.5)) : SilenceDb;

    public static double Percentile(IReadOnlyList<double> values, double fraction)
    {
        if (values.Count == 0)
            return double.NaN;

        var sorted = values.Order().ToArray();
        return sorted[(int)Math.Clamp(Math.Round(fraction * (sorted.Length - 1)), 0, sorted.Length - 1)];
    }
}
