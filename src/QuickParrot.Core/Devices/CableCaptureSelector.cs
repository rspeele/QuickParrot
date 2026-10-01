namespace QuickParrot.Core.Devices;

/// <summary>Finds the recording side of the virtual cable, which the game uses as its mic.</summary>
public static class CableCaptureSelector
{
    /// <summary>
    /// Prefers the partner of <paramref name="cableRender"/>: VB-CABLE's "CABLE Input (x)" records on "CABLE Output (x)",
    /// and Muzychenko's "Line 1 (Virtual Audio Cable)" uses one name for both. Otherwise any VB-CABLE or Muzychenko input.
    /// </summary>
    public static CaptureDeviceInfo? Select(IReadOnlyList<CaptureDeviceInfo> devices, AudioDeviceInfo? cableRender)
    {
        var active = devices.Where(d => d.IsActive).ToList();
        if (cableRender is not null)
        {
            var partner = PartnerName(cableRender.Name);
            if (active.FirstOrDefault(d => SameName(d.Name, partner) || SameName(d.Name, cableRender.Name)) is { } paired)
                return paired;
        }

        return active.FirstOrDefault(d => d.Name.Contains("CABLE Output", StringComparison.OrdinalIgnoreCase))
            ?? active.FirstOrDefault(d => d.Name.Contains("Virtual Audio Cable", StringComparison.OrdinalIgnoreCase));
    }

    internal static string PartnerName(string renderName)
    {
        var index = renderName.IndexOf("Input", StringComparison.OrdinalIgnoreCase);
        return index < 0 ? renderName : renderName[..index] + "Output" + renderName[(index + "Input".Length)..];
    }

    private static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
