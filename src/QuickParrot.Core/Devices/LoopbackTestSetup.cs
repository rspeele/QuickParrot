namespace QuickParrot.Core.Devices;

/// <summary>The device IDs the one-click setup test needs, resolved from current devices and settings.</summary>
public sealed record LoopbackTestSetup(string? CableRenderId, string? CableCaptureId, string? MonitorRenderId)
{
    /// <summary>True once the cable's render and capture sides are both resolved to an active device.</summary>
    public bool IsReady => CableRenderId is not null && CableCaptureId is not null;

    public static LoopbackTestSetup Resolve(
        IReadOnlyList<AudioDeviceInfo> renderDevices,
        IReadOnlyList<CaptureDeviceInfo> captureDevices,
        string? cableId,
        string? monitorId,
        string? defaultRenderId)
    {
        var selection = OutputDeviceSelector.Select(renderDevices, cableId, monitorId, defaultRenderId);
        var cableCapture = CableCaptureSelector.Select(captureDevices, selection.Cable);
        var monitorRenderId = selection.MonitorIsCable ? null : selection.Monitor?.Id;
        return new LoopbackTestSetup(selection.Cable?.Id, cableCapture?.Id, monitorRenderId);
    }
}
