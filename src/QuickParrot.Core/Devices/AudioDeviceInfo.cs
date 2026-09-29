namespace QuickParrot.Core.Devices;

public enum AudioDeviceState
{
    Active,
    Disabled,
    NotPresent,
    Unplugged,
}

public sealed record AudioDeviceInfo(string Id, string Name, AudioDeviceState State)
{
    public bool IsActive => State == AudioDeviceState.Active;
}

/// <summary>Lists the system's render (playback) endpoints.</summary>
public interface IAudioDeviceCatalog
{
    IReadOnlyList<AudioDeviceInfo> GetRenderDevices();

    /// <summary>The Windows default multimedia output, or null if there is none.</summary>
    string? GetDefaultRenderDeviceId();
}
