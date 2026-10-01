namespace QuickParrot.Core.Dsp;

/// <summary>Conversions between decibels and amplitude or power ratios.</summary>
public static class Decibels
{
    public static double ToAmplitude(double db) => Math.Pow(10, db / 20);

    public static double ToPower(double db) => Math.Pow(10, db / 10);

    /// <summary>Negative infinity for silence (zero, negative or NaN).</summary>
    public static double FromAmplitude(double amplitude) =>
        amplitude > 0 ? 20 * Math.Log10(amplitude) : double.NegativeInfinity;

    /// <summary>As <see cref="FromAmplitude(double)"/>, but never below <paramref name="floorDb"/>.</summary>
    public static double FromAmplitude(double amplitude, double floorDb) => Math.Max(floorDb, FromAmplitude(amplitude));

    /// <summary>Negative infinity for silence (zero, negative or NaN).</summary>
    public static double FromPower(double power) => power > 0 ? 10 * Math.Log10(power) : double.NegativeInfinity;

    public static double FromPower(double power, double floorDb) => Math.Max(floorDb, FromPower(power));
}
