using NAudio.CoreAudioApi;
using QuickParrot.Core.Mic;

namespace QuickParrot.Audio;

/// <summary>
/// Reads and sets a connected endpoint's mute and master volume. Each call creates and releases its own COM objects,
/// so it's safe from any thread and never holds a device between calls.
/// </summary>
internal static class EndpointVolumes
{
    public static MicLevel Read(string deviceId)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = OpenActive(enumerator, deviceId);
        var endpoint = device.AudioEndpointVolume;
        return new MicLevel(endpoint.Mute, endpoint.MasterVolumeLevelScalar);
    }

    /// <summary>Applies whichever of <paramref name="muted"/> and <paramref name="volume"/> are given, mute first.</summary>
    public static void Set(string deviceId, bool? muted, float? volume)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = OpenActive(enumerator, deviceId);
        var endpoint = device.AudioEndpointVolume;
        if (muted is { } mute)
            endpoint.Mute = mute;
        if (volume is { } level)
            endpoint.MasterVolumeLevelScalar = Math.Clamp(level, 0f, 1f);
    }

    private static MMDevice OpenActive(MMDeviceEnumerator enumerator, string deviceId)
    {
        var device = enumerator.GetDevice(deviceId);
        if (device.State == DeviceState.Active)
            return device;

        using (device)
            throw new InvalidOperationException($"{device.FriendlyName} isn't connected.");
    }
}
