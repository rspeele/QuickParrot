using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using QuickParrot.Core.Replay;

namespace QuickParrot.Audio;

/// <summary>
/// Feeds a <see cref="ReplayBuffer"/> from WASAPI loopback of the monitor output (else the default output), following
/// device changes. Thread-safe; sessions start and stop on a background task.
/// </summary>
public sealed class LoopbackReplayCapture : IDisposable
{
    private const long HealthySessionMilliseconds = 5000;

    private readonly ReplayBuffer _buffer;
    private readonly object _gate = new(); // guards the requested state and reconcile scheduling
    private readonly object _captureLock = new(); // serializes starting and stopping sessions
    private readonly MMDeviceEnumerator _notificationEnumerator;
    private readonly MMDeviceNotificationClient _notifications;
    private bool _enabled;
    private string? _monitorDeviceId;
    private string? _failedDeviceId; // not retried until something changes, so a broken device can't spin
    private bool _dirty;
    private bool _reconciling;
    private bool _disposed;
    private LoopbackReplaySession? _session;
    private string? _lastError;

    public LoopbackReplayCapture(ReplayBuffer buffer)
    {
        _buffer = buffer;
        _notificationEnumerator = new MMDeviceEnumerator();
        _notifications = _notificationEnumerator.CreateNotificationClient(useSynchronizationContext: false);
        _notifications.DefaultDeviceChanged += (_, e) =>
        {
            if (e.Flow == DataFlow.Render)
                RequestReconcile(somethingChanged: true);
        };
        _notifications.DeviceStateChanged += (_, _) => RequestReconcile(somethingChanged: true);
        _notifications.DeviceAdded += (_, _) => RequestReconcile(somethingChanged: true);
        _notifications.DeviceRemoved += (_, _) => RequestReconcile(somethingChanged: true);
        _notifications.PropertyValueChanged += (_, e) =>
        {
            if (e.PropertyKey.Equals(PropertyKeys.PKEY_AudioEngine_DeviceFormat))
                RequestReconcile(somethingChanged: true);
        };
    }

    /// <summary>A user-facing error message, raised on a background thread. Repeats are suppressed.</summary>
    public event Action<string>? ErrorOccurred;

    /// <summary>The render endpoint being recorded, or null.</summary>
    public string? CaptureDeviceId => Volatile.Read(ref _session)?.DeviceId;

    /// <summary>
    /// Starts, stops or retargets capture to match; returns at once. <paramref name="monitorDeviceId"/> null follows
    /// the Windows default output. Disabling also frees the buffer's memory.
    /// </summary>
    public void Configure(bool enabled, string? monitorDeviceId)
    {
        lock (_gate)
        {
            _enabled = enabled;
            _monitorDeviceId = monitorDeviceId;
        }

        RequestReconcile(somethingChanged: true);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
        }

        _notifications.Dispose();
        _notificationEnumerator.Dispose();
        lock (_captureLock)
            StopSession();
    }

    // Called from Windows audio notification threads, so it only flags work and starts at most one loop.
    private void RequestReconcile(bool somethingChanged)
    {
        lock (_gate)
        {
            if (somethingChanged)
                _failedDeviceId = null;

            _dirty = true;
            if (_reconciling || _disposed)
                return;

            _reconciling = true;
        }

        _ = Task.Run(ReconcileLoop);
    }

    private void ReconcileLoop()
    {
        while (true)
        {
            bool enabled;
            string? monitorId, failedId;
            lock (_gate)
            {
                if (!_dirty || _disposed)
                {
                    _reconciling = false;
                    return;
                }

                _dirty = false;
                (enabled, monitorId, failedId) = (_enabled, _monitorDeviceId, _failedDeviceId);
            }

            try
            {
                lock (_captureLock)
                    Reconcile(enabled, monitorId, failedId);
            }
            catch (Exception e)
            {
                Report($"The replay buffer failed: {e.Message}");
            }
        }
    }

    private void Reconcile(bool enabled, string? monitorId, string? failedId)
    {
        if (Volatile.Read(ref _disposed))
            return;

        if (!enabled)
        {
            StopSession();
            _buffer.Clear();
            ClearLastError();
            return;
        }

        var target = ResolveTarget(monitorId);
        if (_session is { HasStopped: false } running && running.DeviceId == target)
            return;

        StopSession();
        if (target is null)
        {
            Report("The replay buffer has no output device to record.");
            return;
        }

        if (target == failedId)
            return;

        try
        {
            Volatile.Write(ref _session, LoopbackReplaySession.Start(target, _buffer, Report, OnSessionStopped));
            ClearLastError();
        }
        catch (Exception e)
        {
            lock (_gate)
                _failedDeviceId = target;

            Report($"Couldn't start the replay buffer: {e.Message}");
        }
    }

    // A session that had been running a while (e.g. stopped by a device format change) gets one quiet retry.
    private void OnSessionStopped(LoopbackReplaySession session, Exception? error)
    {
        var ranMilliseconds = Environment.TickCount64 - session.StartedAtMilliseconds;
        if (ranMilliseconds < HealthySessionMilliseconds)
        {
            lock (_gate)
                _failedDeviceId = session.DeviceId;

            Report($"The replay buffer stopped recording: {error?.Message ?? "the device stopped."}");
        }

        RequestReconcile(somethingChanged: false);
    }

    private void StopSession()
    {
        var session = _session;
        Volatile.Write(ref _session, null);
        session?.Dispose();
    }

    private void ClearLastError()
    {
        lock (_gate)
            _lastError = null;
    }

    private static string? ResolveTarget(string? monitorId)
    {
        using var enumerator = new MMDeviceEnumerator();
        if (monitorId is not null)
        {
            try
            {
                using var configured = enumerator.GetDevice(monitorId);
                if (configured.State == DeviceState.Active)
                    return configured.ID;
            }
            catch (Exception e) when (e is COMException or ArgumentException or InvalidOperationException)
            {
                // Gone: fall back to the default, like playback does.
            }
        }

        if (!enumerator.TryGetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia, out var device))
            return null;

        using (device)
            return device.ID;
    }

    private void Report(string message)
    {
        lock (_gate)
        {
            if (message == _lastError)
                return;

            _lastError = message;
        }

        try
        {
            ErrorOccurred?.Invoke(message);
        }
        catch (Exception)
        {
            // Raised from audio and pool threads, where an escaping exception would crash the process.
        }
    }
}
