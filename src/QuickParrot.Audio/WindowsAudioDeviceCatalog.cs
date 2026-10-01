using System.Collections.Immutable;
using NAudio.CoreAudioApi;
using QuickParrot.Core.Devices;

namespace QuickParrot.Audio;

/// <summary>
/// Lists render and capture endpoints, caching the (immutable) lists until Windows reports a device change.
/// Thread-safe.
/// </summary>
public sealed class WindowsAudioDeviceCatalog : IAudioDeviceCatalog, ICaptureDeviceCatalog, IDisposable
{
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

        using var enumerator = new MMDeviceEnumerator();
        var fresh = new Snapshot(
            version,
            EndpointReader.Describe(enumerator, DataFlow.Render, EndpointReader.ListedStates, (_, info) => info),
            EndpointReader.DefaultId(enumerator, DataFlow.Render, Role.Multimedia));
        Volatile.Write(ref _snapshot, fresh);
        return fresh;
    }

    private CaptureSnapshot GetCaptureSnapshot()
    {
        var version = Volatile.Read(ref _version);
        if (Volatile.Read(ref _captureSnapshot) is { } cached && cached.Version == version)
            return cached;

        using var enumerator = new MMDeviceEnumerator();
        var fresh = new CaptureSnapshot(
            version,
            EnumerateCaptureDevices(enumerator),
            EndpointReader.DefaultId(enumerator, DataFlow.Capture, Role.Multimedia),
            EndpointReader.DefaultId(enumerator, DataFlow.Capture, Role.Communications));
        Volatile.Write(ref _captureSnapshot, fresh);
        return fresh;
    }

    // Not-present included: a pulled USB mic is not-present, and its restore record must survive until it's back.
    private static ImmutableArray<CaptureDeviceInfo> EnumerateCaptureDevices(MMDeviceEnumerator enumerator) =>
        EndpointReader.Describe(
            enumerator,
            DataFlow.Capture,
            DeviceState.All,
            (device, info) => new CaptureDeviceInfo(
                info.Id, info.Name, info.State, EndpointReader.ReadListenEnabled(device)));

    private sealed record Snapshot(int Version, IReadOnlyList<AudioDeviceInfo> Devices, string? DefaultId);

    private sealed record CaptureSnapshot(
        int Version, IReadOnlyList<CaptureDeviceInfo> Devices, string? DefaultId, string? CommunicationsId);
}
