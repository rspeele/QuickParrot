namespace QuickParrot.Core.Dsp;

public static class RaisedCosine
{
    /// <summary>Rises smoothly from 0 at <paramref name="fraction"/> 0 to 1 at 1, and falls back to 0 at 2.</summary>
    public static double At(double fraction) => 0.5 - 0.5 * Math.Cos(Math.PI * fraction);

    /// <summary>A periodic Hann window: one whole raised-cosine cycle over <paramref name="length"/> points.</summary>
    public static double[] Hann(int length)
    {
        var window = new double[length];
        for (var i = 0; i < length; i++)
            window[i] = At(2.0 * i / length);

        return window;
    }
}
