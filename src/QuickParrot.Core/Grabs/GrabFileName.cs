using System.Globalization;

namespace QuickParrot.Core.Grabs;

/// <summary>Grab ids like "grab-20260930-142501", with "-2", "-3"... for more grabs in the same second.</summary>
public static class GrabFileName
{
    public const string Prefix = "grab-";
    private const string TimeFormat = "yyyyMMdd-HHmmss";

    public static string Format(DateTimeOffset grabbedAt, int sequence = 1)
    {
        var id = Prefix + grabbedAt.ToLocalTime().ToString(TimeFormat, CultureInfo.InvariantCulture);
        return sequence > 1 ? $"{id}-{sequence}" : id;
    }

    /// <summary>Parses an id as written by <see cref="Format"/>; the time is local, to the second.</summary>
    public static bool TryParse(string id, out DateTimeOffset grabbedAt, out int sequence)
    {
        grabbedAt = default;
        sequence = 1;
        if (!id.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) || id.Length < Prefix.Length + TimeFormat.Length)
            return false;

        var time = id.AsSpan(Prefix.Length, TimeFormat.Length);
        var rest = id.AsSpan(Prefix.Length + TimeFormat.Length);
        if (!DateTime.TryParseExact(time, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            return false;

        if (rest.Length > 0 && !TryParseSequence(rest, out sequence))
            return false;

        grabbedAt = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        return true;
    }

    private static bool TryParseSequence(ReadOnlySpan<char> suffix, out int sequence)
    {
        sequence = 1;
        return suffix[0] == '-'
            && int.TryParse(suffix[1..], NumberStyles.None, CultureInfo.InvariantCulture, out sequence)
            && sequence > 1;
    }
}
