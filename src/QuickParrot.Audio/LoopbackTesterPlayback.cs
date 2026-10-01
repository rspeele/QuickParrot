using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace QuickParrot.Audio;

/// <summary>Plays a mono buffer once to one device, converted to its mix format, for the setup test.</summary>
internal sealed class LoopbackTesterPlayback : IAsyncDisposable
{
    private const int LatencyMilliseconds = 50;
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private readonly MMDevice _device;
    private readonly WasapiPlayer _player;
    private readonly TaskCompletionSource<Exception?> _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _started;

    private LoopbackTesterPlayback(MMDevice device, WasapiPlayer player, TimeSpan duration)
    {
        _device = device;
        _player = player;
        Duration = duration;
        _player.PlaybackStopped += (_, e) => _stopped.TrySetResult(e.Exception);
    }

    public TimeSpan Duration { get; }

    /// <summary>Completes when playback ends by itself or is stopped, with the error if it failed.</summary>
    public Task<Exception?> Stopped => _stopped.Task;

    public static LoopbackTesterPlayback Open(string deviceId, float[] mono, int sampleRate)
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

            var mixFormat = player.DeviceMixFormat;
            ISampleProvider source = new MonoBufferSampleProvider(mono, sampleRate);
            if (mixFormat.Channels != 1)
                source = new ChannelMapSampleProvider(source, mixFormat.Channels);
            if (mixFormat.SampleRate != sampleRate)
                source = new WdlResamplingSampleProvider(source, mixFormat.SampleRate);

            player.Init(new SampleToWaveProvider(source));
            return new LoopbackTesterPlayback(device, player, TimeSpan.FromSeconds(mono.Length / (double)sampleRate));
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

    // WasapiPlayer doesn't join a play thread that ended by itself, so wait for it before disposing the client.
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_started)
            {
                await Task.Run(_player.Stop);
                await _stopped.Task.WaitAsync(StopTimeout);
            }

            _player.Dispose();
        }
        catch (Exception e)
        {
            Debug.WriteLine($"QuickParrot: abandoned test playback, stop or dispose failed: {e.Message}");
        }
        finally
        {
            _device.Dispose();
        }
    }

    private sealed class MonoBufferSampleProvider(float[] samples, int sampleRate) : ISampleProvider
    {
        private int _position;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);

        public int Read(Span<float> buffer)
        {
            var count = Math.Min(buffer.Length, samples.Length - _position);
            samples.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }
    }
}
