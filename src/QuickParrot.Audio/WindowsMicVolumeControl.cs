using NAudio.CoreAudioApi;
using QuickParrot.Core.Mic;

namespace QuickParrot.Audio;

/// <summary>
/// Reads and sets a capture endpoint's mute and master volume. Each call creates and releases its own COM objects,
/// so it's safe from any thread and never holds a device between calls.
/// </summary>
public sealed class WindowsMicVolumeControl : IMicVolumeControl
{
    public MicLevel Read(string deviceId) =>
        Use(deviceId, volume => new MicLevel(volume.Mute, volume.MasterVolumeLevelScalar));

    public void SetMute(string deviceId, bool muted) => Use(deviceId, volume => volume.Mute = muted);

    public void SetVolume(string deviceId, float level) =>
        Use(deviceId, volume => volume.MasterVolumeLevelScalar = Math.Clamp(level, 0f, 1f));

    private static T Use<T>(string deviceId, Func<AudioEndpointVolume, T> action)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDevice(deviceId);
        if (device.State != DeviceState.Active)
            throw new InvalidOperationException($"{device.FriendlyName} isn't connected.");

        return action(device.AudioEndpointVolume);
    }
}
