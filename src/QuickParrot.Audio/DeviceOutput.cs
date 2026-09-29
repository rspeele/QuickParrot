using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace QuickParrot.Audio;

/// <summary>One clip playing to one device, converted to that device's mix format.</summary>
internal sealed class DeviceOutput : IDisposable
{
    private const int LatencyMilliseconds = 50;

    private readonly MMDevice _device;
    private readonly WasapiPlayer _player;
    private readonly VolumeSampleProvider _volume;
    private readonly TaskCompletionSource _playThreadExited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _disposed;
    private bool _started;

    private DeviceOutput(MMDevice device, WasapiPlayer player, VolumeSampleProvider volume)
    {
        _device = device;
        _player = player;
        _volume = volume;
        _player.PlaybackStopped += (_, e) =>
        {
            _playThreadExited.TrySetResult();
            if (!_disposed)
                Stopped?.Invoke(e.Exception);
        };
    }

    /// <summary>Raised on the audio thread when playback ends by itself; the exception is set if it failed.</summary>
    public event Action<Exception?>? Stopped;

    public float Volume
    {
        set => _volume.Volume = value;
    }

    public static DeviceOutput Open(string deviceId, DecodedClip clip, float volume)
    {
        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDevice(deviceId);
        WasapiPlayer? player = null;
        try
        {
            player = new WasapiPlayerBuilder()
                .WithDevice(device)
                .WithSharedMode()
                .WithEventSync()
                .WithLatency(LatencyMilliseconds)
                .Build();

            // Match the device's mix format ourselves rather than relying on the engine to resample.
            var mixFormat = player.DeviceMixFormat;
            ISampleProvider source = new ClipSampleProvider(clip);
            if (source.WaveFormat.SampleRate != mixFormat.SampleRate)
                source = new WdlResamplingSampleProvider(source, mixFormat.SampleRate);
            if (source.WaveFormat.Channels != mixFormat.Channels)
                source = new ChannelMapSampleProvider(source, mixFormat.Channels);

            var volumeProvider = new VolumeSampleProvider(source) { Volume = volume };
            player.Init(new SampleToWaveProvider(volumeProvider));
            return new DeviceOutput(device, player, volumeProvider);
        }
        catch
        {
            player?.Dispose();
            device.Dispose();
            throw;
        }
    }

    public void Play()
    {
        _player.Play();
        _started = true;
    }

    // Asynchronous so replacing a clip doesn't wait for the old stream to wind down (~25-50 ms per device).
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _ = DisposeInBackgroundAsync();
    }

    private async Task DisposeInBackgroundAsync()
    {
        try
        {
            // WasapiPlayer doesn't join a play thread that ended by itself, which may still be using the client.
            if (_started)
            {
                await Task.Run(_player.Stop);
                await _playThreadExited.Task;
            }

            _player.Dispose();
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine($"QuickParrot: disposing output failed: {e.Message}");
        }
        finally
        {
            _device.Dispose();
        }
    }
}
