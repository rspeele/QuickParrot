namespace QuickParrot.Core.Diagnostics;

/// <summary>The device work behind <see cref="ClipCableTest"/>: decoding the clip, recording the cable, playing back.</summary>
public interface IClipCableAudio
{
    /// <summary>Decodes a clip to mono; throws if it can't be read.</summary>
    Task<MonoAudio> LoadClipAsync(string fullPath, CancellationToken cancellationToken);

    /// <summary>Starts recording a capture device as mono, keeping at most <paramref name="maxDuration"/>.</summary>
    ICableRecording StartRecording(string captureDeviceId, TimeSpan maxDuration);

    /// <summary>Plays <paramref name="audio"/> on a render device to its end; returns why it failed, or null.</summary>
    Task<string?> PlayAsync(string renderDeviceId, MonoAudio audio, CancellationToken cancellationToken);
}

public interface ICableRecording : IDisposable
{
    /// <summary>Stops recording and returns what was captured; throws with a user-facing message if capturing failed.</summary>
    Task<MonoAudio> StopAsync();
}
