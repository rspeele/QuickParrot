using QuickParrot.Core.Devices;

namespace QuickParrot.Core.Editing;

/// <param name="Problem">Why there's no device to preview on, for the user; null when <paramref name="Device"/> is set.</param>
public sealed record PreviewDevice(AudioDeviceInfo? Device, string? Problem);

/// <summary>Picks the editor's preview output: the user's monitor device, and never anything that leads into voice chat.</summary>
public static class PreviewDeviceSelector
{
    public static PreviewDevice Select(
        IReadOnlyList<AudioDeviceInfo> devices, string? cableId, string? monitorId, string? defaultId)
    {
        var selection = OutputDeviceSelector.Select(devices, cableId, monitorId, defaultId);
        if (selection.Monitor is null)
            return new PreviewDevice(null, "No output device is available to preview on.");

        if (selection.MonitorIsCable || OutputDeviceSelector.IsVirtualCable(selection.Monitor.Name))
        {
            return new PreviewDevice(null,
                "Your output device is the virtual cable, so preview is off to keep it out of voice chat. "
                + "Choose your speakers or headphones as the monitor device in Settings.");
        }

        return new PreviewDevice(selection.Monitor, null);
    }
}
