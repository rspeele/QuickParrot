using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace QuickParrot.Audio;

/// <summary>
/// One sample stream playing to one device in shared mode, converted to the device's mix format. Disposing stops
/// and releases it on a background task, so callers never wait on the audio thread winding down.
/// </summary>
internal sealed class WasapiOutput : IDisposable, IAsyncDisposable
{
    private const int LatencyMilliseconds = 50;
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private readonly MMDevice _device;
    private readonly WasapiPlayer _player;
    private readonly TaskCompletionSource _playThreadExited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<Exception?> _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lazy<Task> _teardown;
    private volatile bool _disposed;
    private volatile bool _started;

    private WasapiOutput(MMDevice device, WasapiPlayer player)
    {
        _device = device;
        _player = player;
        _teardown = new Lazy<Task>(() =>
        {
            _disposed = true;
            return Task.Run(TearDownAsync);
        });
        _player.PlaybackStopped += (_, e) =>
        {
            _playThreadExited.TrySetResult();
            if (!_disposed)
                _stopped.TrySetResult(e.Exception);
        };
    }

    /// <summary>Completes when playback ends by itself, with the error if it failed; never once disposed.</summary>
    public Task<Exception?> Stopped => _stopped.Task;

    /// <summary>Seconds the device has played, or null before <see cref="Play"/> or if its clock can't be read.</summary>
    public double? PlayedSeconds
    {
        get
        {
            if (!_started)
                return null;

            try
            {
                var format = _player.OutputWaveFormat;
                return format is { AverageBytesPerSecond: > 0 }
                    ? _player.GetPosition() / (double)format.AverageBytesPerSecond
                    : null;
            }
            catch (Exception e) when (e is COMException or InvalidOperationException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Opens <paramref name="deviceId"/> ready to play <paramref name="source"/>, matched to the device's mix format;
    /// <paramref name="atMixFormat"/> can add a last stage after that.
    /// </summary>
    public static WasapiOutput Open(
        string deviceId, ISampleProvider source, Func<ISampleProvider, ISampleProvider>? atMixFormat = null)
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
            var matched = SampleChains.Convert(source, mixFormat.Channels, mixFormat.SampleRate);
            player.Init(new SampleToWaveProvider(atMixFormat?.Invoke(matched) ?? matched));
            return new WasapiOutput(device, player);
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

    public void Dispose() => _ = _teardown.Value;

    /// <summary>Completes once the output is released (or abandoned); never throws.</summary>
    public ValueTask DisposeAsync() => new(_teardown.Value);

    private async Task TearDownAsync()
    {
        try
        {
            if (!_started || await TryStopAsync())
                _player.Dispose();
        }
        catch (Exception e)
        {
            Debug.WriteLine($"QuickParrot: disposing an output failed: {e.Message}");
        }
        finally
        {
            _device.Dispose();
        }
    }

    // WasapiPlayer doesn't join a play thread that ended by itself, which may still be using the client: a player
    // whose thread isn't seen to exit is abandoned rather than disposed under it.
    private async Task<bool> TryStopAsync()
    {
        try
        {
            _player.Stop();
            await _playThreadExited.Task.WaitAsync(StopTimeout);
            return true;
        }
        catch (Exception e)
        {
            Debug.WriteLine($"QuickParrot: abandoned an output, stopping it failed: {e.Message}");
            return false;
        }
    }
}
