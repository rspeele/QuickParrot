using NAudio.CoreAudioApi;
using QuickParrot.Core.Devices;
using QuickParrot.Core.Diagnostics;

namespace QuickParrot.Audio;

/// <summary>
/// Reads the audio setup for diagnosis, fresh each time and strictly read-only: property stores are opened for
/// reading and endpoint volumes are only queried. Never throws; whatever can't be read is left unknown.
/// </summary>
public sealed class WindowsAudioSetupReader(Func<ConfiguredDevices> configured, Func<bool> micRestorePending) : IAudioSetupReader
{
    private const DeviceState ListedStates = DeviceState.Active | DeviceState.Disabled | DeviceState.Unplugged;

    public AudioSetupSnapshot Read()
    {
        var settings = Safely(configured, ConfiguredDevices.Auto);
        var pending = Safely(micRestorePending, true); // unknown: don't blame the user for QuickParrot's own mute
        var ducking = CommunicationsDuckingRegistry.Read();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var listen = AudioSetupSnapshot.EmptyById<ListenSetting>();
            var levels = AudioSetupSnapshot.EmptyById<EndpointLevel>();
            var render = ReadDevices(enumerator, DataFlow.Render, levels, null);
            var capture = ReadDevices(enumerator, DataFlow.Capture, levels, listen)
                .Select(d => new CaptureDeviceInfo(d.Id, d.Name, d.State, listen.GetValueOrDefault(d.Id)?.Enabled == true))
                .ToList();

            return new AudioSetupSnapshot
            {
                RenderDevices = render,
                CaptureDevices = capture,
                Defaults = ReadDefaults(enumerator),
                Listen = listen,
                Levels = levels,
                MicRestorePending = pending,
                CommunicationsDucking = ducking,
                Configured = settings,
            };
        }
        catch (Exception)
        {
            return new AudioSetupSnapshot
            {
                DevicesReadable = false,
                MicRestorePending = pending,
                CommunicationsDucking = ducking,
                Configured = settings,
            };
        }
    }

    private static List<AudioDeviceInfo> ReadDevices(
        MMDeviceEnumerator enumerator, DataFlow flow, Dictionary<string, EndpointLevel> levels, Dictionary<string, ListenSetting>? listen)
    {
        var devices = new List<AudioDeviceInfo>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, ListedStates))
        {
            using (device)
            {
                if (TryDescribe(device) is not { } info)
                    continue;

                devices.Add(info);
                if (!info.IsActive)
                    continue;

                if (TryReadLevel(device) is { } level)
                    levels[info.Id] = level;
                if (listen is not null && TryReadListen(device) is { } setting)
                    listen[info.Id] = setting;
            }
        }

        return devices;
    }

    private static AudioDeviceInfo? TryDescribe(MMDevice device)
    {
        try
        {
            var state = device.State switch
            {
                DeviceState.Active => AudioDeviceState.Active,
                DeviceState.Disabled => AudioDeviceState.Disabled,
                DeviceState.Unplugged => AudioDeviceState.Unplugged,
                _ => AudioDeviceState.NotPresent,
            };
            return new AudioDeviceInfo(device.ID, device.FriendlyName, state);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static EndpointLevel? TryReadLevel(MMDevice device)
    {
        try
        {
            var volume = device.AudioEndpointVolume;
            return new EndpointLevel(volume.Mute, volume.MasterVolumeLevelScalar);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // MMDevice.Properties opens the store read-only.
    private static ListenSetting? TryReadListen(MMDevice device)
    {
        try
        {
            var properties = device.Properties;
            var enabled = properties.Contains(ListenProperties.Enabled) ? properties[ListenProperties.Enabled].Value : null;
            var target = properties.Contains(ListenProperties.Target) ? properties[ListenProperties.Target].Value : null;
            return ListenSetting.FromProperties(enabled, target);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static DefaultEndpoints? ReadDefaults(MMDeviceEnumerator enumerator)
    {
        try
        {
            return new DefaultEndpoints(
                DefaultId(enumerator, DataFlow.Render, Role.Console),
                DefaultId(enumerator, DataFlow.Render, Role.Multimedia),
                DefaultId(enumerator, DataFlow.Render, Role.Communications),
                DefaultId(enumerator, DataFlow.Capture, Role.Console),
                DefaultId(enumerator, DataFlow.Capture, Role.Multimedia),
                DefaultId(enumerator, DataFlow.Capture, Role.Communications));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? DefaultId(MMDeviceEnumerator enumerator, DataFlow flow, Role role)
    {
        if (!enumerator.TryGetDefaultAudioEndpoint(flow, role, out var device))
            return null;

        using (device)
            return device.ID;
    }

    private static T Safely<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return fallback;
        }
    }
}
