using NAudio.CoreAudioApi;
using QuickParrot.Core.Devices;

namespace QuickParrot.Audio;

/// <summary>
/// Lists render endpoints, caching the list until Windows reports a device change. Thread-safe.
/// </summary>
public sealed class WindowsAudioDeviceCatalog : IAudioDeviceCatalog, IDisposable
{
    private const DeviceState ListedStates = DeviceState.Active | DeviceState.Disabled | DeviceState.Unplugged;

    private readonly MMDeviceEnumerator _notificationEnumerator;
    private readonly MMDeviceNotificationClient _notifications;
    private Snapshot? _snapshot;
    private int _version;

    public WindowsAudioDeviceCatalog()
    {
        _notificationEnumerator = new MMDeviceEnumerator();
        _notifications = _notificationEnumerator.CreateNotificationClient(useSynchronizationContext: false);
        _notifications.DeviceAdded += (_, _) => Invalidate();
        _notifications.DeviceRemoved += (_, _) => Invalidate();
        _notifications.DeviceStateChanged += (_, _) => Invalidate();
        _notifications.DefaultDeviceChanged += (_, _) => Invalidate();
        _notifications.PropertyValueChanged += (_, e) =>
        {
            if (e.PropertyKey.Equals(PropertyKeys.PKEY_Device_FriendlyName))
                Invalidate();
        };
    }

    public IReadOnlyList<AudioDeviceInfo> GetRenderDevices() => GetSnapshot().Devices;

    public string? GetDefaultRenderDeviceId() => GetSnapshot().DefaultId;

    public void Dispose()
    {
        _notifications.Dispose();
        _notificationEnumerator.Dispose();
    }

    // Runs on a Windows audio thread that holds a lock, so it must not block or touch the audio stack.
    private void Invalidate() => Interlocked.Increment(ref _version);

    // A snapshot taken while a change notification arrived is tagged with the old version, so it's never reused.
    private Snapshot GetSnapshot()
    {
        var version = Volatile.Read(ref _version);
        if (Volatile.Read(ref _snapshot) is { } cached && cached.Version == version)
            return cached;

        var fresh = new Snapshot(version, EnumerateRenderDevices(), ReadDefaultRenderDeviceId());
        Volatile.Write(ref _snapshot, fresh);
        return fresh;
    }

    private static List<AudioDeviceInfo> EnumerateRenderDevices()
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

    private static string? ReadDefaultRenderDeviceId()
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

    private sealed record Snapshot(int Version, IReadOnlyList<AudioDeviceInfo> Devices, string? DefaultId);
}
