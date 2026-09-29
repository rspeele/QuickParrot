using NAudio.CoreAudioApi;
using QuickParrot.Core.Devices;

namespace QuickParrot.Audio;

public sealed class WindowsAudioDeviceCatalog : IAudioDeviceCatalog
{
    private const DeviceState ListedStates = DeviceState.Active | DeviceState.Disabled | DeviceState.Unplugged;

    public IReadOnlyList<AudioDeviceInfo> GetRenderDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = new List<AudioDeviceInfo>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, ListedStates))
        {
            using (device)
            {
                if (TryDescribe(device) is { } info)
                    devices.Add(info);
            }
        }

        return devices;
    }

    public string? GetDefaultRenderDeviceId()
    {
        using var enumerator = new MMDeviceEnumerator();
        if (!enumerator.TryGetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia, out var device))
            return null;

        using (device)
            return device.ID;
    }

    // A device can vanish mid-enumeration, making its property store throw.
    private static AudioDeviceInfo? TryDescribe(MMDevice device)
    {
        try
        {
            return new AudioDeviceInfo(device.ID, device.FriendlyName, ToState(device.State));
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            return null;
        }
    }

    private static AudioDeviceState ToState(DeviceState state) => state switch
    {
        DeviceState.Active => AudioDeviceState.Active,
        DeviceState.Disabled => AudioDeviceState.Disabled,
        DeviceState.Unplugged => AudioDeviceState.Unplugged,
        _ => AudioDeviceState.NotPresent,
    };
}
