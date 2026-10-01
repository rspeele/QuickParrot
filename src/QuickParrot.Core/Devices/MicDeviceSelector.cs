namespace QuickParrot.Core.Devices;

/// <param name="ConfiguredUnavailable">A mic was configured but isn't active, so <see cref="Device"/> is a fallback.</param>
public sealed record MicSelection(CaptureDeviceInfo? Device, bool ConfiguredUnavailable);

/// <summary>Pure rules for finding the user's real microphone among the capture endpoints.</summary>
public static class MicDeviceSelector
{
    /// <summary>
    /// An explicitly configured active device wins. Otherwise an active non-cable input with "Listen to this device"
    /// on, then the default input, then the default communications input, skipping the cable.
    /// </summary>
    public static MicSelection Select(
        IReadOnlyList<CaptureDeviceInfo> devices, string? configuredId, string? defaultId, string? communicationsId)
    {
        if (FindActive(devices, configuredId) is { } configured)
            return new MicSelection(configured, false);

        var candidates = devices.Where(d => d.IsActive && !d.IsCable).ToList();
        var listening = candidates.Where(d => d.ListenEnabled).ToList();
        var device = FindActive(listening, defaultId)
            ?? FindActive(listening, communicationsId)
            ?? listening.FirstOrDefault()
            ?? FindActive(candidates, defaultId)
            ?? FindActive(candidates, communicationsId);
        return new MicSelection(device, configuredId is not null);
    }

    private static CaptureDeviceInfo? FindActive(IEnumerable<CaptureDeviceInfo> devices, string? id) =>
        id is null ? null : devices.FirstOrDefault(d => d.IsActive && string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
}
