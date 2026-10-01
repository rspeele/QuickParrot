namespace QuickParrot.Core.Editing;

/// <summary>
/// ITU-R BS.1770-4 integrated loudness (EBU R128): K-weighting, 400 ms blocks every 100 ms, an absolute gate at
/// -70 LUFS and a relative gate 10 LU below the absolute-gated level. Silence measures as negative infinity.
/// </summary>
public static class LoudnessMeter
{
    public const double AbsoluteGateLufs = -70;
    public const double RelativeGateLu = -10;

    private const double LoudnessOffset = -0.691;
    private const int StepsPerBlock = 4;

    public static double IntegratedLufs(EditableAudio audio) =>
        IntegratedLufs(audio.Samples.Span, audio.Channels, audio.SampleRate);

    /// <summary>
    /// Integrated loudness in LUFS. Audio shorter than one 400 ms block is measured as a single block spanning all of it
    /// (absolute gate only), so short bites still get a sensible reading.
    /// </summary>
    public static double IntegratedLufs(ReadOnlySpan<float> interleaved, int channels, int sampleRate)
    {
        var frames = interleaved.Length / channels;
        if (frames == 0)
            return double.NegativeInfinity;

        var step = Math.Max(1, (int)Math.Round(sampleRate * 0.1));
        var steps = frames / step;
        var stepEnergy = WeightedStepEnergy(interleaved, channels, sampleRate, step, steps, out var total);
        if (steps < StepsPerBlock)
            return Gate(BlockLoudness(total / frames));

        var blocks = steps - StepsPerBlock + 1;
        var blockPower = new double[blocks];
        var running = 0.0;
        for (var s = 0; s < steps; s++)
        {
            running += stepEnergy[s];
            if (s >= StepsPerBlock)
                running -= stepEnergy[s - StepsPerBlock];
            if (s >= StepsPerBlock - 1)
                blockPower[s - StepsPerBlock + 1] = Math.Max(0, running) / (StepsPerBlock * step);
        }

        var absoluteGated = MeanPower(blockPower, power => BlockLoudness(power) > AbsoluteGateLufs);
        if (absoluteGated is not { } ungatedMean)
            return double.NegativeInfinity;

        var relativeGate = BlockLoudness(ungatedMean) + RelativeGateLu;
        var gated = MeanPower(blockPower, power =>
        {
            var loudness = BlockLoudness(power);
            return loudness > AbsoluteGateLufs && loudness > relativeGate;
        });
        return gated is { } mean ? BlockLoudness(mean) : double.NegativeInfinity;
    }

    /// <summary>BS.1770 channel weights in WAVE speaker order: surrounds count 1.41×, LFE not at all.</summary>
    public static double ChannelWeight(int channel, int channels) => (channels, channel) switch
    {
        (4, >= 2) => 1.41, // quad: FL FR BL BR
        (5, >= 3) => 1.41, // 5.0: FL FR C BL BR
        (>= 6, 3) => 0,    // 5.1 / 7.1 LFE
        (>= 6, >= 4) => 1.41,
        _ => 1,
    };

    /// <summary>The two K-weighting stages for any sample rate (a high shelf, then the RLB high-pass), per libebur128's derivation.</summary>
    public static (Biquad Shelf, Biquad HighPass) KWeighting(int sampleRate)
    {
        var k = Math.Tan(Math.PI * 1681.974450955533 / sampleRate);
        const double q = 0.7071752369554196;
        var vh = Math.Pow(10, 3.999843853973347 / 20);
        var vb = Math.Pow(vh, 0.4996667741545416);
        var a0 = 1 + k / q + k * k;
        var shelf = new Biquad(
            (vh + vb * k / q + k * k) / a0,
            2 * (k * k - vh) / a0,
            (vh - vb * k / q + k * k) / a0,
            2 * (k * k - 1) / a0,
            (1 - k / q + k * k) / a0);

        k = Math.Tan(Math.PI * 38.13547087602444 / sampleRate);
        const double q2 = 0.5003270373238773;
        a0 = 1 + k / q2 + k * k;
        var highPass = new Biquad(1, -2, 1, 2 * (k * k - 1) / a0, (1 - k / q2 + k * k) / a0);
        return (shelf, highPass);
    }

    public static double BlockLoudness(double weightedMeanSquare) =>
        weightedMeanSquare > 0 ? LoudnessOffset + 10 * Math.Log10(weightedMeanSquare) : double.NegativeInfinity;

    private static double Gate(double loudness) => loudness > AbsoluteGateLufs ? loudness : double.NegativeInfinity;

    private static double? MeanPower(double[] blockPower, Func<double, bool> include)
    {
        double sum = 0;
        var count = 0;
        foreach (var power in blockPower)
        {
            if (!include(power))
                continue;

            sum += power;
            count++;
        }

        return count == 0 ? null : sum / count;
    }

    // Channel-weighted sum of squared K-weighted samples per 100 ms step; a final extra bin holds any partial step.
    private static double[] WeightedStepEnergy(
        ReadOnlySpan<float> interleaved, int channels, int sampleRate, int step, int steps, out double total)
    {
        var (shelf, highPass) = KWeighting(sampleRate);
        var energy = new double[steps + 1];
        total = 0;
        for (var c = 0; c < channels; c++)
        {
            var weight = ChannelWeight(c, channels);
            if (weight == 0)
                continue;

            // Both stages inlined as transposed direct form II, in double so the 38 Hz high-pass stays accurate.
            double s1z1 = 0, s1z2 = 0, s2z1 = 0, s2z2 = 0;
            var frames = interleaved.Length / channels;
            for (var s = 0; s <= steps; s++)
            {
                var end = s < steps ? (s + 1) * step : frames;
                var sum = 0.0;
                for (var i = s * step * channels + c; i < end * channels; i += channels)
                {
                    double x = interleaved[i];
                    var y1 = shelf.B0 * x + s1z1;
                    s1z1 = shelf.B1 * x - shelf.A1 * y1 + s1z2;
                    s1z2 = shelf.B2 * x - shelf.A2 * y1;
                    var y = highPass.B0 * y1 + s2z1;
                    s2z1 = highPass.B1 * y1 - highPass.A1 * y + s2z2;
                    s2z2 = highPass.B2 * y1 - highPass.A2 * y;
                    sum += y * y;
                }

                energy[s] += weight * sum;
                total += weight * sum;
            }
        }

        return energy;
    }
}

public readonly record struct Biquad(double B0, double B1, double B2, double A1, double A2);

