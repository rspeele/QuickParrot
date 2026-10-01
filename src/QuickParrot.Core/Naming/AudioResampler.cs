namespace QuickParrot.Core.Naming;

/// <summary>
/// Resamples mono audio with windowed-sinc (Lanczos) interpolation, small enough to keep in Core. When downsampling,
/// the kernel is widened by the rate ratio so it low-passes at the new Nyquist instead of aliasing high content.
/// </summary>
public static class AudioResampler
{
    private const int Lobes = 8;
    private const int TableResolution = 512; // kernel samples per unit distance, linearly interpolated

    private static readonly double[] Kernel = BuildKernel();

    public static float[] Resample(ReadOnlySpan<float> source, int sourceSampleRate, int targetSampleRate)
    {
        if (sourceSampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceSampleRate));
        if (targetSampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetSampleRate));

        if (source.Length == 0)
            return [];

        if (sourceSampleRate == targetSampleRate)
            return source.ToArray();

        var step = (double)sourceSampleRate / targetSampleRate;
        var scale = Math.Min(1.0, 1 / step);
        var output = new float[Math.Max((int)Math.Round(source.Length / step), 0)];
        for (var i = 0; i < output.Length; i++)
            output[i] = InterpolateAt(source, i * step, scale);

        return output;
    }

    // Weights are normalized by their sum so DC passes at exactly unity gain whatever the kernel width.
    private static float InterpolateAt(ReadOnlySpan<float> source, double position, double scale)
    {
        var halfWidth = Lobes / scale;
        var first = (int)Math.Ceiling(position - halfWidth);
        var last = (int)Math.Floor(position + halfWidth);
        double sum = 0, weights = 0;
        for (var tap = first; tap <= last; tap++)
        {
            var weight = KernelAt(Math.Abs(position - tap) * scale);
            weights += weight;
            if (tap >= 0 && tap < source.Length)
                sum += source[tap] * weight;
        }

        return weights == 0 ? 0f : (float)(sum / weights);
    }

    private static double KernelAt(double distance)
    {
        var t = distance * TableResolution;
        var index = (int)t;
        if (index >= Kernel.Length - 1)
            return 0;

        return Kernel[index] + (Kernel[index + 1] - Kernel[index]) * (t - index);
    }

    private static double[] BuildKernel()
    {
        var table = new double[Lobes * TableResolution + 1];
        for (var i = 0; i < table.Length - 1; i++)
        {
            var x = i / (double)TableResolution;
            var piX = Math.PI * x;
            table[i] = x == 0 ? 1 : Lobes * Math.Sin(piX) * Math.Sin(piX / Lobes) / (piX * piX);
        }

        return table;
    }
}
