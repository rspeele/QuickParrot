using System.Diagnostics;
using QuickParrot.Core.Devices;
using QuickParrot.Core.Playback;

namespace QuickParrot.Audio;

/// <summary>
/// Plays clips to the virtual cable and the monitor device simultaneously. The cable output drives
/// <see cref="Finished"/>; with no cable installed, the monitor does.
/// </summary>
public sealed class NAudioClipPlayer : IClipPlayer, IDisposable
{
    private readonly IAudioDeviceCatalog _devices;
    private volatile OutputSettings _settings = new(null, null, 1f, 1f);
    private PreparedClip? _current;

    public NAudioClipPlayer(IAudioDeviceCatalog devices)
    {
        _devices = devices;
    }

    public event Action<ClipFinished>? Finished;

    /// <summary>
    /// Decodes a silent MP3, lists devices and opens (without playing) the outputs once, so the first real
    /// play doesn't pay those one-off costs. Never faults.
    /// </summary>
    public Task WarmUpAsync() => Task.Run(() =>
    {
        try
        {
            Open(DecodedClip.DecodeWarmUpClip(), _settings).Dispose();
        }
        catch (Exception e)
        {
            Debug.WriteLine($"QuickParrot: audio warm-up failed: {e.Message}");
        }
    });

    public Task<IPreparedClip> PrepareAsync(string fullPath, CancellationToken cancellationToken)
    {
        var settings = _settings;
        return Task.Run<IPreparedClip>(
            () =>
            {
                var clip = DecodedClip.Decode(fullPath, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                return Open(clip, settings);
            },
            cancellationToken);
    }

    public void Play(IPreparedClip clip, long playId)
    {
        Stop();
        if (clip is not PreparedClip prepared)
        {
            clip.Dispose();
            throw new ArgumentException("Clip wasn't prepared by this player.", nameof(clip));
        }

        _current = prepared;
        prepared.SetVolumes(_settings);
        prepared.Start(playId, finished => Finished?.Invoke(finished));
    }

    public void Stop()
    {
        _current?.Dispose();
        _current = null;
    }

    public void Configure(OutputSettings settings)
    {
        _settings = settings;
        _current?.SetVolumes(settings);
    }

    public void Dispose() => Stop();

    private PreparedClip Open(DecodedClip clip, OutputSettings settings)
    {
        var selection = OutputDeviceSelector.Select(
            _devices.GetRenderDevices(), settings.CableDeviceId, settings.MonitorDeviceId, _devices.GetDefaultRenderDeviceId());
        return PreparedClip.Open(clip, selection, settings);
    }
}
