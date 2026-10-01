using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using QuickParrot.Core.Editing;

namespace QuickParrot.Audio;

/// <summary>One stretch of editor audio playing to one device, converted to that device's mix format.</summary>
internal sealed class EditorPreviewOutput : IDisposable
{
    private const int LatencyMilliseconds = 50;
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private readonly MMDevice _device;
    private readonly WasapiPlayer _player;
    private readonly EditableAudioSampleProvider _source;
    private readonly int _startFrame;
    private readonly int _endFrame;
    private readonly int _sampleRate;
    private readonly TaskCompletionSource _playThreadExited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _disposed;
    private bool _started;

    private EditorPreviewOutput(MMDevice device, WasapiPlayer player, EditableAudio audio, EditableAudioSampleProvider source, int startFrame, int endFrame)
    {
        Audio = audio;
        _device = device;
        _player = player;
        _source = source;
        _startFrame = startFrame;
        _endFrame = endFrame;
        _sampleRate = audio.SampleRate;
        _player.PlaybackStopped += (_, e) =>
        {
            _playThreadExited.TrySetResult();
            if (!_disposed)
                Stopped?.Invoke(e.Exception);
        };
    }

    /// <summary>Raised on the audio thread when playback ends by itself; the exception is set if it failed.</summary>
    public event Action<Exception?>? Stopped;

    public EditableAudio Audio { get; }

    public static EditorPreviewOutput Open(string deviceId, EditableAudio audio, int startFrame, int endFrame, float gain)
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
            var source = new EditableAudioSampleProvider(audio, startFrame, endFrame, gain);
            ISampleProvider chain = source;
            if (chain.WaveFormat.Channels != mixFormat.Channels)
                chain = new ChannelMapSampleProvider(chain, mixFormat.Channels);
            if (chain.WaveFormat.SampleRate != mixFormat.SampleRate)
                chain = new WdlResamplingSampleProvider(chain, mixFormat.SampleRate);

            player.Init(new SampleToWaveProvider(chain));
            return new EditorPreviewOutput(device, player, audio, source, startFrame, endFrame);
        }
        catch
        {
            player?.Dispose();
            device.Dispose();
            throw;
        }
    }

    /// <summary>The source frame being heard: the device clock where available, else how far the source has been read.</summary>
    public int PositionFrame
    {
        get
        {
            try
            {
                var format = _player.OutputWaveFormat;
                if (_started && format is { AverageBytesPerSecond: > 0 })
                {
                    var seconds = _player.GetPosition() / (double)format.AverageBytesPerSecond;
                    return Math.Min(_endFrame, _startFrame + (int)(seconds * _sampleRate));
                }
            }
            catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException)
            {
                // Fall through to the read position.
            }

            return _source.FramePosition;
        }
    }

    public void Play()
    {
        _player.Play();
        _started = true;
    }

    // Stops in the background so the UI never waits on the audio thread winding down.
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
            if (_started)
            {
                await Task.Run(_player.Stop);
                await _playThreadExited.Task.WaitAsync(StopTimeout);
            }

            _player.Dispose();
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine($"QuickParrot: disposing preview failed: {e.Message}");
        }
        finally
        {
            _device.Dispose();
        }
    }
}
