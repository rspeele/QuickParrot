namespace QuickParrot.Core.Playback;

/// <summary>
/// Plays clips to the virtual cable and the user's own output at once. Methods are called from a single
/// thread; <see cref="Finished"/> may be raised on any thread.
/// </summary>
public interface IClipPlayer
{
    /// <summary>Raised when a clip ends naturally, or with <see cref="ClipFinished.Error"/> set if its output failed.</summary>
    event Action<ClipFinished>? Finished;

    /// <summary>
    /// Decodes a clip and opens its outputs so <see cref="Play"/> starts without delay. Throws if the file is
    /// missing or undecodable, or no output can be opened. The caller disposes the clip if it never plays it.
    /// </summary>
    IPreparedClip Prepare(string fullPath);

    /// <summary>Starts <paramref name="clip"/>, replacing anything playing. Takes ownership of the clip, even if it throws.</summary>
    void Play(IPreparedClip clip, long playId);

    void Stop();

    /// <summary>Applies device and volume settings; volumes also apply to a clip already playing.</summary>
    void Configure(OutputSettings settings);
}

public interface IPreparedClip : IDisposable
{
    string FullPath { get; }
}

public sealed record ClipFinished(long PlayId, Exception? Error = null);

/// <summary>Device IDs are null to auto-detect the cable / use the Windows default output.</summary>
public sealed record OutputSettings(string? CableDeviceId, string? MonitorDeviceId, float CableVolume, float MonitorVolume);
