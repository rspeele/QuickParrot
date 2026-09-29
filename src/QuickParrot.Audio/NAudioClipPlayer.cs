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
    private OutputSettings _settings = new(null, null, 1f, 1f);
    private PreparedClip? _current;

    public NAudioClipPlayer(IAudioDeviceCatalog devices)
    {
        _devices = devices;
    }

    public event Action<ClipFinished>? Finished;

    public IPreparedClip Prepare(string fullPath)
    {
        var clip = DecodedClip.Decode(fullPath);
        var selection = OutputDeviceSelector.Select(
            _devices.GetRenderDevices(), _settings.CableDeviceId, _settings.MonitorDeviceId, _devices.GetDefaultRenderDeviceId());
        return PreparedClip.Open(clip, selection, _settings);
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
}
