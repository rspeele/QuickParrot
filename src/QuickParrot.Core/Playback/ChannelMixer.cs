namespace QuickParrot.Core.Playback;

/// <summary>
/// Maps interleaved frames between channel counts, assuming the standard WAVE speaker order for each count.
/// Channels the output lacks fold into front left/right: center and surrounds at -3 dB, LFE dropped.
/// </summary>
public sealed class ChannelMixer
{
    private const float MinusThreeDb = 0.70710677f;
    private const float LimiterThreshold = 0.8f;

    private readonly float[] _gains;

    public ChannelMixer(int inputChannels, int outputChannels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(inputChannels, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(outputChannels, 1);
        InputChannels = inputChannels;
        OutputChannels = outputChannels;
        _gains = BuildGains(Layout(inputChannels), Layout(outputChannels));
        Limits = Enumerable.Range(0, outputChannels).Any(o => RowSum(o) > 1.0001f);
    }

    public int InputChannels { get; }

    public int OutputChannels { get; }

    /// <summary>True when summed channels could exceed full scale, so output is soft-limited.</summary>
    public bool Limits { get; }

    public float Gain(int output, int input) => _gains[output * InputChannels + input];

    public void MixFrame(ReadOnlySpan<float> input, Span<float> output)
    {
        for (var o = 0; o < OutputChannels; o++)
        {
            var row = _gains.AsSpan(o * InputChannels, InputChannels);
            var sum = 0f;
            for (var i = 0; i < row.Length; i++)
                sum += row[i] * input[i];

            output[o] = Limits ? SoftLimit(sum) : sum;
        }
    }

    /// <summary>Passes samples below the threshold untouched and bends louder ones smoothly towards full scale.</summary>
    public static float SoftLimit(float sample)
    {
        var magnitude = Math.Abs(sample);
        if (magnitude <= LimiterThreshold)
            return sample;

        const float headroom = 1f - LimiterThreshold;
        var limited = LimiterThreshold + headroom * MathF.Tanh((magnitude - LimiterThreshold) / headroom);
        return MathF.CopySign(limited, sample);
    }

    private float RowSum(int output)
    {
        var sum = 0f;
        for (var i = 0; i < InputChannels; i++)
            sum += Math.Abs(Gain(output, i));
        return sum;
    }

    private static float[] BuildGains(Speaker[] inputs, Speaker[] outputs)
    {
        var gains = new float[outputs.Length * inputs.Length];
        void Add(int output, int input, float gain) => gains[output * inputs.Length + input] += gain;

        for (var i = 0; i < inputs.Length; i++)
        {
            var speaker = inputs[i];
            var direct = IndexOf(outputs, speaker);
            if (direct < 0)
                direct = IndexOf(outputs, Counterpart(speaker));

            if (direct >= 0)
            {
                Add(direct, i, 1f);
                continue;
            }

            var (left, right) = StereoGains(speaker);
            if (outputs.Length == 1)
            {
                Add(0, i, (left + right) / 2);
            }
            else
            {
                Add(0, i, left);
                Add(1, i, right);
            }
        }

        return gains;
    }

    private static (float Left, float Right) StereoGains(Speaker speaker) => speaker switch
    {
        Speaker.Mono => (1f, 1f),
        Speaker.FrontLeft => (1f, 0f),
        Speaker.FrontRight => (0f, 1f),
        Speaker.Center => (MinusThreeDb, MinusThreeDb),
        Speaker.BackLeft or Speaker.SideLeft => (MinusThreeDb, 0f),
        Speaker.BackRight or Speaker.SideRight => (0f, MinusThreeDb),
        Speaker.BackCenter => (0.5f, 0.5f),
        _ => (0f, 0f), // LFE and anything unrecognised
    };

    private static Speaker Counterpart(Speaker speaker) => speaker switch
    {
        Speaker.SideLeft => Speaker.BackLeft,
        Speaker.SideRight => Speaker.BackRight,
        Speaker.BackLeft => Speaker.SideLeft,
        Speaker.BackRight => Speaker.SideRight,
        _ => Speaker.Unknown,
    };

    private static int IndexOf(Speaker[] layout, Speaker speaker) =>
        speaker == Speaker.Unknown ? -1 : Array.IndexOf(layout, speaker);

    private static Speaker[] Layout(int channels)
    {
        Speaker[] known = channels switch
        {
            1 => [Speaker.Mono],
            2 => [Speaker.FrontLeft, Speaker.FrontRight],
            3 => [Speaker.FrontLeft, Speaker.FrontRight, Speaker.Center],
            4 => [Speaker.FrontLeft, Speaker.FrontRight, Speaker.BackLeft, Speaker.BackRight],
            5 => [Speaker.FrontLeft, Speaker.FrontRight, Speaker.Center, Speaker.BackLeft, Speaker.BackRight],
            6 => [Speaker.FrontLeft, Speaker.FrontRight, Speaker.Center, Speaker.Lfe, Speaker.BackLeft, Speaker.BackRight],
            7 => [Speaker.FrontLeft, Speaker.FrontRight, Speaker.Center, Speaker.Lfe, Speaker.BackCenter,
                Speaker.SideLeft, Speaker.SideRight],
            _ => [Speaker.FrontLeft, Speaker.FrontRight, Speaker.Center, Speaker.Lfe, Speaker.BackLeft,
                Speaker.BackRight, Speaker.SideLeft, Speaker.SideRight],
        };

        var layout = new Speaker[channels];
        known.AsSpan(0, Math.Min(channels, known.Length)).CopyTo(layout);
        return layout; // channels past 7.1 stay Unknown and are dropped
    }

    private enum Speaker
    {
        Unknown,
        Mono,
        FrontLeft,
        FrontRight,
        Center,
        Lfe,
        BackLeft,
        BackRight,
        BackCenter,
        SideLeft,
        SideRight,
    }
}
