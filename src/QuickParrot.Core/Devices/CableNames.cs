namespace QuickParrot.Core.Devices;

/// <summary>How VB-CABLE and Muzychenko's Virtual Audio Cable name their endpoints.</summary>
internal static class CableNames
{
    private const string VbCableRender = "CABLE Input";
    private const string VbCableCapture = "CABLE Output";
    private const string VbAudio = "VB-Audio";
    private const string Muzychenko = "Virtual Audio Cable";

    /// <summary>A cable's recording side, which must never be treated as the real mic.</summary>
    public static bool IsCableCapture(string name) =>
        Has(name, VbCableCapture) || Has(name, VbAudio) || Has(name, Muzychenko);

    /// <summary>An output that plays into a virtual cable.</summary>
    public static bool IsCableRender(string name) => IsCableCapture(name) || Has(name, VbCableRender);

    public static bool IsVbCableRender(string name) => Has(name, VbCableRender);

    public static bool IsVbCableCapture(string name) => Has(name, VbCableCapture);

    /// <summary>Muzychenko's cable uses one name for both its playback and recording sides.</summary>
    public static bool IsMuzychenko(string name) => Has(name, Muzychenko);

    /// <summary>VB-CABLE's "CABLE Input (x)" records on "CABLE Output (x)"; other names map to themselves.</summary>
    public static string CapturePartnerOf(string renderName)
    {
        var index = renderName.IndexOf("Input", StringComparison.OrdinalIgnoreCase);
        return index < 0 ? renderName : renderName[..index] + "Output" + renderName[(index + "Input".Length)..];
    }

    private static bool Has(string name, string marker) => name.Contains(marker, StringComparison.OrdinalIgnoreCase);
}
