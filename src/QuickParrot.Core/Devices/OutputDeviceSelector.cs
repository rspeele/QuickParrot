namespace QuickParrot.Core.Devices;

/// <param name="MonitorIsCable">The monitor resolved to the cable itself, so it must not be played to twice.</param>
public sealed record OutputDeviceSelection(AudioDeviceInfo? Cable, AudioDeviceInfo? Monitor, bool MonitorIsCable)
{
    public string Describe()
    {
        var cable = Cable is null ? "no virtual cable found (clips play to your own output only)" : Cable.Name;
        var monitor = Monitor is null ? "no output device" : Monitor.Name;
        var warning = MonitorIsCable ? " — warning: your output is the cable itself, so you won't hear clips" : "";
        return $"Cable: {cable}. Monitor: {monitor}{warning}.";
    }
}

/// <summary>Pure device-picking rules for the cable and monitor outputs.</summary>
public static class OutputDeviceSelector
{
    private const string VbCableName = "CABLE Input";
    private const string MuzychenkoName = "Virtual Audio Cable";

    public static OutputDeviceSelection Select(
        IReadOnlyList<AudioDeviceInfo> devices, string? cableId, string? monitorId, string? defaultId)
    {
        var cable = SelectCable(devices, cableId);
        var monitor = SelectMonitor(devices, monitorId, defaultId);
        var monitorIsCable = cable is not null && monitor is not null && SameId(cable.Id, monitor.Id);
        return new OutputDeviceSelection(cable, monitor, monitorIsCable);
    }

    /// <summary>An explicitly configured active device wins; otherwise VB-CABLE, then Muzychenko's cable.</summary>
    public static AudioDeviceInfo? SelectCable(IReadOnlyList<AudioDeviceInfo> devices, string? configuredId) =>
        FindActive(devices, configuredId)
        ?? devices.FirstOrDefault(d => d.IsActive && d.Name.Contains(VbCableName, StringComparison.OrdinalIgnoreCase))
        ?? devices.FirstOrDefault(d => d.IsActive && d.Name.Contains(MuzychenkoName, StringComparison.OrdinalIgnoreCase));

    public static AudioDeviceInfo? SelectMonitor(IReadOnlyList<AudioDeviceInfo> devices, string? configuredId, string? defaultId) =>
        FindActive(devices, configuredId) ?? FindActive(devices, defaultId);

    /// <summary>
    /// A real output to make the Windows default in place of the cable: the configured monitor, else the first active
    /// output that isn't a virtual cable.
    /// </summary>
    public static AudioDeviceInfo? SelectRealPlaybackDevice(
        IReadOnlyList<AudioDeviceInfo> devices, AudioDeviceInfo? cable, string? monitorId)
    {
        var real = devices.Where(d => d.IsActive && !IsVirtualCable(d.Name) && (cable is null || !SameId(d.Id, cable.Id))).ToList();
        return FindActive(real, monitorId) ?? real.FirstOrDefault();
    }

    public static bool IsVirtualCable(string renderName) =>
        MicDeviceSelector.IsVirtualCable(renderName) || renderName.Contains(VbCableName, StringComparison.OrdinalIgnoreCase);

    private static AudioDeviceInfo? FindActive(IReadOnlyList<AudioDeviceInfo> devices, string? id) =>
        id is null ? null : devices.FirstOrDefault(d => d.IsActive && SameId(d.Id, id));

    private static bool SameId(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
