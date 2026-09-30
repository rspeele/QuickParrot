namespace QuickParrot.Core.Devices;

/// <summary>One entry in the mic-device dropdown; an empty <see cref="Id"/> means "Auto".</summary>
public sealed record MicDeviceChoice(string Id, string Name);

/// <summary>Builds the mic-device dropdown: "Auto (resolved name)" first, then the real capture devices.</summary>
public static class MicDeviceMenu
{
    /// <summary>
    /// <paramref name="devices"/> excludes the virtual cable and anything no longer present; devices with
    /// "Listen to this device" on are marked. The first entry shows what "Auto" currently resolves to.
    /// </summary>
    public static IReadOnlyList<MicDeviceChoice> Build(
        IReadOnlyList<CaptureDeviceInfo> devices, string? defaultId, string? communicationsId)
    {
        var auto = MicDeviceSelector.Select(devices, null, defaultId, communicationsId);
        var autoName = auto.Device is { } resolved ? $"Auto ({Describe(resolved)})" : "Auto (no microphone found)";

        var listed = devices
            .Where(d => d.State != AudioDeviceState.NotPresent && !d.IsCable)
            .Select(d => new MicDeviceChoice(d.Id, Describe(d)));

        return [new MicDeviceChoice("", autoName), .. listed];
    }

    private static string Describe(CaptureDeviceInfo device)
    {
        var name = device.IsActive ? device.Name : $"{device.Name} ({device.State.ToString().ToLowerInvariant()})";
        return device.ListenEnabled ? $"{name} · Listen on" : name;
    }
}
