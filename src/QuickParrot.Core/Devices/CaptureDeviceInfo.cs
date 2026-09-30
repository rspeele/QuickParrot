namespace QuickParrot.Core.Devices;

/// <param name="ListenEnabled">Windows' "Listen to this device" is on for this input.</param>
public sealed record CaptureDeviceInfo(string Id, string Name, AudioDeviceState State, bool ListenEnabled)
{
    public bool IsActive => State == AudioDeviceState.Active;

    /// <summary>The virtual cable's own recording side, which must never be treated as the real mic.</summary>
    public bool IsCable => MicDeviceSelector.IsVirtualCable(Name);
}

/// <summary>Lists the system's capture (recording) endpoints, including not-present ones.</summary>
public interface ICaptureDeviceCatalog
{
    IReadOnlyList<CaptureDeviceInfo> GetCaptureDevices();

    string? GetDefaultCaptureDeviceId();

    string? GetDefaultCommunicationsCaptureDeviceId();
}
