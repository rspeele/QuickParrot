using System.Collections.Immutable;
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
    public AudioSetupSnapshot Read()
    {
        var settings = Safely(configured, ConfiguredDevices.Auto);
        var pending = Safely(micRestorePending, true); // unknown: don't blame the user for QuickParrot's own mute
        var ducking = CommunicationsDuckingRegistry.Read();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var listen = new Dictionary<string, ListenSetting>(Endpoints.IdComparer);
            var levels = new Dictionary<string, EndpointLevel>(Endpoints.IdComparer);
            var render = ReadDevices(enumerator, DataFlow.Render, levels, null);
            var capture = ReadDevices(enumerator, DataFlow.Capture, levels, listen)
                .Select(d => new CaptureDeviceInfo(d.Id, d.Name, d.State, listen.GetValueOrDefault(d.Id)?.Enabled == true))
                .ToList();

            return new AudioSetupSnapshot
            {
                RenderDevices = render,
                CaptureDevices = capture,
                Defaults = ReadDefaults(enumerator),
                Listen = AudioSetupSnapshot.ById(listen),
                Levels = AudioSetupSnapshot.ById(levels),
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

    private static ImmutableArray<AudioDeviceInfo> ReadDevices(
        MMDeviceEnumerator enumerator, DataFlow flow, Dictionary<string, EndpointLevel> levels, Dictionary<string, ListenSetting>? listen) =>
        EndpointReader.Describe(enumerator, flow, EndpointReader.ListedStates, (device, info) =>
        {
            if (info.IsActive)
            {
                if (TryReadLevel(device) is { } level)
                    levels[info.Id] = level;
                if (listen is not null && EndpointReader.TryReadListen(device) is { } setting)
                    listen[info.Id] = setting;
            }

            return info;
        });

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

    private static DefaultEndpoints? ReadDefaults(MMDeviceEnumerator enumerator)
    {
        try
        {
            return new DefaultEndpoints(
                EndpointReader.DefaultId(enumerator, DataFlow.Render, Role.Console),
                EndpointReader.DefaultId(enumerator, DataFlow.Render, Role.Multimedia),
                EndpointReader.DefaultId(enumerator, DataFlow.Render, Role.Communications),
                EndpointReader.DefaultId(enumerator, DataFlow.Capture, Role.Console),
                EndpointReader.DefaultId(enumerator, DataFlow.Capture, Role.Multimedia),
                EndpointReader.DefaultId(enumerator, DataFlow.Capture, Role.Communications));
        }
        catch (Exception)
        {
            return null;
        }
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
