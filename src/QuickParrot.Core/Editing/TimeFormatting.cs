using System.Globalization;

namespace QuickParrot.Core.Editing;

/// <summary>Time readouts and ruler ticks for the editor.</summary>
public static class TimeFormatting
{
    private static readonly double[] RulerSteps =
        [0.001, 0.002, 0.005, 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300];

    /// <summary>"m:ss.fff", e.g. "1:02.345".</summary>
    public static string Position(double seconds)
    {
        var ms = (long)Math.Round(Math.Max(0, seconds) * 1000);
        return string.Create(CultureInfo.InvariantCulture, $"{ms / 60000}:{ms / 1000 % 60:00}.{ms % 1000:000}");
    }

    /// <summary>The smallest "nice" tick interval that keeps ticks at least <paramref name="minSpacing"/> pixels apart.</summary>
    public static double RulerStep(double secondsPerPixel, double minSpacing)
    {
        foreach (var step in RulerSteps)
        {
            if (step / secondsPerPixel >= minSpacing)
                return step;
        }

        return RulerSteps[^1];
    }

    /// <summary>A tick label with just enough decimals for the step: "0:05", "0:05.5", "0:05.25", "0:05.125".</summary>
    public static string Tick(double seconds, double step)
    {
        var decimals = step >= 1 ? 0 : step >= 0.1 ? 1 : step >= 0.01 ? 2 : 3;
        var scale = Math.Pow(10, decimals);
        var units = (long)Math.Round(Math.Max(0, seconds) * scale);
        var whole = units / (long)scale;
        var label = string.Create(CultureInfo.InvariantCulture, $"{whole / 60}:{whole % 60:00}");
        if (decimals == 0)
            return label;

        var fraction = (units % (long)scale).ToString(CultureInfo.InvariantCulture).PadLeft(decimals, '0');
        return $"{label}.{fraction}";
    }
}
