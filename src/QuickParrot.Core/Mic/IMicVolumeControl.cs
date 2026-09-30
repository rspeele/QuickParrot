namespace QuickParrot.Core.Mic;

/// <param name="Volume">The endpoint's master volume, 0 to 1.</param>
public sealed record MicLevel(bool Muted, float Volume);

/// <summary>Reads and changes a capture endpoint's mute and volume. Methods throw if the device can't be reached.</summary>
public interface IMicVolumeControl
{
    MicLevel Read(string deviceId);

    void SetMute(string deviceId, bool muted);

    void SetVolume(string deviceId, float volume);
}
