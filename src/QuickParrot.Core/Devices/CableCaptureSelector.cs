namespace QuickParrot.Core.Devices;

/// <summary>Finds the recording side of the virtual cable, which the game uses as its mic.</summary>
public static class CableCaptureSelector
{
    /// <summary>
    /// Prefers the recording partner of <paramref name="cableRender"/>, or one with the same name. Otherwise any
    /// VB-CABLE or Muzychenko input.
    /// </summary>
    public static CaptureDeviceInfo? Select(IReadOnlyList<CaptureDeviceInfo> devices, AudioDeviceInfo? cableRender)
    {
        var active = devices.Where(d => d.IsActive).ToList();
        if (cableRender is not null)
        {
            var partner = CableNames.CapturePartnerOf(cableRender.Name);
            if (active.FirstOrDefault(d => SameName(d.Name, partner) || SameName(d.Name, cableRender.Name)) is { } paired)
                return paired;
        }

        return active.FirstOrDefault(d => CableNames.IsVbCableCapture(d.Name))
            ?? active.FirstOrDefault(d => CableNames.IsMuzychenko(d.Name));
    }

    private static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
