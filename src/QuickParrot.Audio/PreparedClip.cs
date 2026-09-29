using System.Diagnostics;
using QuickParrot.Core.Devices;
using QuickParrot.Core.Playback;

namespace QuickParrot.Audio;

/// <summary>A decoded clip with its cable and monitor outputs opened and ready to start.</summary>
internal sealed class PreparedClip : IPreparedClip
{
    private readonly DeviceOutput? _cable;
    private readonly DeviceOutput? _monitor;

    private PreparedClip(string fullPath, DeviceOutput? cable, DeviceOutput? monitor)
    {
        FullPath = fullPath;
        _cable = cable;
        _monitor = monitor;
    }

    public string FullPath { get; }

    // A cable that exists but won't open is an error worth showing; a monitor failure only costs local audio.
    public static PreparedClip Open(DecodedClip clip, OutputDeviceSelection devices, OutputSettings settings)
    {
        var cable = devices.Cable is { } cableDevice
            ? DeviceOutput.Open(cableDevice.Id, clip, settings.CableVolume)
            : null;

        DeviceOutput? monitor = null;
        if (devices.Monitor is { } monitorDevice && !devices.MonitorIsCable)
        {
            try
            {
                monitor = DeviceOutput.Open(monitorDevice.Id, clip, settings.MonitorVolume);
            }
            catch (Exception e) when (cable is not null)
            {
                Debug.WriteLine($"QuickParrot: monitor output failed to open: {e.Message}");
            }
        }

        if (cable is null && monitor is null)
            throw new InvalidOperationException("No audio output device is available.");

        return new PreparedClip(clip.FullPath, cable, monitor);
    }

    /// <summary>Starts both outputs; the cable (or the monitor, with no cable) reports the clip's end.</summary>
    public void Start(long playId, Action<ClipFinished> onFinished)
    {
        var primary = (_cable ?? _monitor)!;
        primary.Stopped += error => onFinished(new ClipFinished(playId, error));
        if (_monitor is not null && _monitor != primary)
            _monitor.Stopped += LogMonitorFailure;

        _cable?.Play();
        _monitor?.Play();
    }

    public void SetVolumes(OutputSettings settings)
    {
        if (_cable is not null)
            _cable.Volume = settings.CableVolume;
        if (_monitor is not null)
            _monitor.Volume = settings.MonitorVolume;
    }

    public void Dispose()
    {
        _cable?.Dispose();
        _monitor?.Dispose();
    }

    private static void LogMonitorFailure(Exception? error)
    {
        if (error is not null)
            Debug.WriteLine($"QuickParrot: monitor output failed: {error.Message}");
    }
}
