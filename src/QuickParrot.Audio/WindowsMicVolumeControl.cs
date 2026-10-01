using QuickParrot.Core.Mic;

namespace QuickParrot.Audio;

/// <summary>Reads and sets a capture endpoint's mute and master volume; safe from any thread (see <see cref="EndpointVolumes"/>).</summary>
public sealed class WindowsMicVolumeControl : IMicVolumeControl
{
    public MicLevel Read(string deviceId) => EndpointVolumes.Read(deviceId);

    public void SetMute(string deviceId, bool muted) => EndpointVolumes.Set(deviceId, muted, null);

    public void SetVolume(string deviceId, float level) => EndpointVolumes.Set(deviceId, null, level);
}
