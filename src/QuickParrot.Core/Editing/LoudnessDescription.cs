using System.Globalization;

namespace QuickParrot.Core.Editing;

/// <summary>Where a selection's loudness measurement stands: in progress, failed, or measured at <see cref="Lufs"/>.</summary>
public readonly record struct LoudnessReading
{
    private LoudnessReading(bool failed, double? lufs)
    {
        Failed = failed;
        Lufs = lufs;
    }

    public static LoudnessReading Measuring => default;

    public static LoudnessReading Unavailable => new(true, null);

    public bool Failed { get; }

    /// <summary>Null until measured; negative infinity for silence.</summary>
    public double? Lufs { get; }

    public static LoudnessReading Of(double lufs) => new(false, lufs);
}

/// <summary>Plain words for what normalizing a selection will do to its level.</summary>
public static class LoudnessDescription
{
    public static string Describe(LoudnessReading reading, LoudnessOptions options)
    {
        if (reading.Failed)
            return "Loudness unknown";
        if (reading.Lufs is not { } lufs)
            return "Measuring…";
        if (!double.IsFinite(lufs))
            return "Selection is silent";

        var gain = LoudnessNormalizer.GainDbFor(lufs, options);
        if (Math.Abs(gain) < 0.5)
            return "Already about right";

        var direction = gain > 0 ? "turned up" : "turned down";
        return string.Create(CultureInfo.CurrentCulture, $"Will be {direction} {Math.Abs(gain):0.#} dB");
    }
}
