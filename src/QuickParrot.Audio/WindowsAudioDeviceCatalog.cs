using NAudio.CoreAudioApi;
using QuickParrot.Core.Devices;

namespace QuickParrot.Audio;

/// <summary>
/// Lists render and capture endpoints, caching the lists until Windows reports a device change. Thread-safe.
/// </summary>
public sealed class WindowsAudioDeviceCatalog : IAudioDeviceCatalog, ICaptureDeviceCatalog, IDisposable
{
    private const DeviceState ListedStates = DeviceState.Active | DeviceState.Disabled | DeviceState.Unplugged;

    private readonly MMDeviceEnumerator _notificationEnumerator;
    private readonly MMDeviceNotificationClient _notifications;
    private Snapshot? _snapshot;
    private CaptureSnapshot? _captureSnapshot;
    private int _version;

    public WindowsAudioDeviceCatalog()
    {
        _notificationEnumerator = new MMDeviceEnumerator();
        _notifications = _notificationEnumerator.CreateNotificationClient(useSynchronizationContext: false);
        _notifications.DeviceAdded += (_, _) => InvalidateAndRaise(devicesChanged: true);
        _notifications.DeviceRemoved += (_, _) => InvalidateAndRaise(devicesChanged: false);
        _notifications.DeviceStateChanged += (_, _) => InvalidateAndRaise(devicesChanged: true);
        _notifications.DefaultDeviceChanged += (_, _) => InvalidateAndRaise(devicesChanged: false);
        _notifications.PropertyValueChanged += (_, e) =>
        {
            if (e.PropertyKey.Equals(PropertyKeys.PKEY_Device_FriendlyName) || ListenProperties.IsListenKey(e.PropertyKey))
                InvalidateAndRaise(devicesChanged: false);
        };
    }

    /// <summary>
    /// Raised when a device is added or changes state, on a Windows audio thread that holds a lock: handlers must
    /// return quickly and not touch the audio stack.
    /// </summary>
    public event Action? DevicesChanged;

    /// <summary>
    /// Raised, like <see cref="DevicesChanged"/>, for anything the audio setup diagnosis looks at: devices, defaults,
    /// names and Listen settings. Not raised for mute or volume changes.
    /// </summary>
    public event Action? SetupChanged;

    public IReadOnlyList<AudioDeviceInfo> GetRenderDevices() => GetSnapshot().Devices;

    public string? GetDefaultRenderDeviceId() => GetSnapshot().DefaultId;

    public IReadOnlyList<CaptureDeviceInfo> GetCaptureDevices() => GetCaptureSnapshot().Devices;

    public string? GetDefaultCaptureDeviceId() => GetCaptureSnapshot().DefaultId;

    public string? GetDefaultCommunicationsCaptureDeviceId() => GetCaptureSnapshot().CommunicationsId;

    public void Dispose()
    {
        _notifications.Dispose();
        _notificationEnumerator.Dispose();
    }

    // Runs on a Windows audio thread that holds a lock, so it must not block or touch the audio stack.
    private void Invalidate() => Interlocked.Increment(ref _version);

    private void InvalidateAndRaise(bool devicesChanged)
    {
        Invalidate();
        if (devicesChanged)
            RaiseSafely(DevicesChanged);
        RaiseSafely(SetupChanged);
    }

    private static void RaiseSafely(Action? handlers)
    {
        try
        {
            handlers?.Invoke();
        }
        catch (Exception)
        {
            // Must not escape into the COM callback.
        }
    }

    // A snapshot taken while a change notification arrived is tagged with the old version, so it's never reused.
    private Snapshot GetSnapshot()
    {
        var version = Volatile.Read(ref _version);
        if (Volatile.Read(ref _snapshot) is { } cached && cached.Version == version)
            return cached;

        var fresh = new Snapshot(version, EnumerateRenderDevices(), ReadDefaultDeviceId(DataFlow.Render, Role.Multimedia));
        Volatile.Write(ref _snapshot, fresh);
        return fresh;
    }

    private CaptureSnapshot GetCaptureSnapshot()
    {
        var version = Volatile.Read(ref _version);
        if (Volatile.Read(ref _captureSnapshot) is { } cached && cached.Version == version)
            return cached;

        var fresh = new CaptureSnapshot(
            version,
            EnumerateCaptureDevices(),
            ReadDefaultDeviceId(DataFlow.Capture, Role.Multimedia),
            ReadDefaultDeviceId(DataFlow.Capture, Role.Communications));
        Volatile.Write(ref _captureSnapshot, fresh);
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

    private static List<CaptureDeviceInfo> EnumerateCaptureDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = new List<CaptureDeviceInfo>();
        // Not-present included: a pulled USB mic is not-present, and its restore record must survive until it's back.
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.All))
        {
            using (device)
            {
                if (TryDescribe(device) is { } info)
                    devices.Add(new CaptureDeviceInfo(info.Id, info.Name, info.State, ReadListenEnabled(device)));
            }
        }

        return devices;
    }

    // The property store is opened read-only.
    private static bool ReadListenEnabled(MMDevice device)
    {
        try
        {
            var properties = device.Properties;
            return properties.Contains(ListenProperties.Enabled) && properties[ListenProperties.Enabled].Value is true;
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            return false;
        }
    }

    private static string? ReadDefaultDeviceId(DataFlow flow, Role role)
    {
        using var enumerator = new MMDeviceEnumerator();
        if (!enumerator.TryGetDefaultAudioEndpoint(flow, role, out var device))
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

    private sealed record CaptureSnapshot(
        int Version, IReadOnlyList<CaptureDeviceInfo> Devices, string? DefaultId, string? CommunicationsId);
}
