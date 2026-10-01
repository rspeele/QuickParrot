using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using QuickParrot.Core.Playback;
using QuickParrot.Core.Replay;

namespace QuickParrot.Audio;

/// <summary>One loopback recording of one render endpoint, writing into the replay buffer.</summary>
internal sealed class LoopbackReplaySession : IDisposable
{
    // Polling, not event sync: event-driven loopback isn't signalled on older Windows builds.
    private const int BufferMilliseconds = 100;

    private readonly MMDevice _device;
    private readonly WasapiRecorder _recorder;
    private readonly ReplayBuffer _buffer;
    private readonly SampleFormat _format;
    private readonly ChannelMixer? _mixer; // null when the device is already mono or stereo
    private readonly TaskCompletionSource<Exception?> _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<Exception> _dataFailed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private float[] _scratch = [];
    private float[] _mixScratch = [];
    private volatile bool _disposing;

    private LoopbackReplaySession(
        string deviceId, MMDevice device, WasapiRecorder recorder, ReplayBuffer buffer, SampleFormat format,
        ChannelMixer? mixer)
    {
        DeviceId = deviceId;
        _device = device;
        _recorder = recorder;
        _buffer = buffer;
        _format = format;
        _mixer = mixer;
        _recorder.DataAvailable += OnDataAvailable;
        _recorder.RecordingStopped += (_, e) =>
        {
            if (!_disposing)
                _stopped.TrySetResult(e.Exception);
        };
    }

    public string DeviceId { get; }

    public long StartedAtMilliseconds { get; } = Environment.TickCount64;

    /// <summary>Completes if recording stops by itself, e.g. the device vanished; never once disposed.</summary>
    public Task<Exception?> Stopped => _stopped.Task;

    /// <summary>Completes with the first failure to store captured audio; recording carries on regardless.</summary>
    public Task<Exception> DataFailed => _dataFailed.Task;

    public static LoopbackReplaySession Start(string deviceId, ReplayBuffer buffer)
    {
        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDevice(deviceId);
        WasapiRecorder? recorder = null;
        try
        {
            recorder = new WasapiRecorderBuilder()
                .WithDevice(device)
                .WithLoopbackCapture()
                .WithSharedMode()
                .WithPollingSync()
                .WithBufferLength(BufferMilliseconds)
                .Build();

            var waveFormat = recorder.WaveFormat;
            var format = CaptureFormats.ToSampleFormat(waveFormat)
                ?? throw new NotSupportedException($"the output uses an unsupported format ({waveFormat}).");

            // Keeps the replay buffer's memory use bounded regardless of how many channels the device captures
            // (e.g. a virtual 7.1 headset): everything past stereo is folded down before it ever reaches the ring.
            var mixer = waveFormat.Channels > 2 ? new ChannelMixer(waveFormat.Channels, 2) : null;
            buffer.Begin(waveFormat.SampleRate, mixer?.OutputChannels ?? waveFormat.Channels);
            var session = new LoopbackReplaySession(deviceId, device, recorder, buffer, format, mixer);
            recorder.StartRecording();
            return session;
        }
        catch
        {
            recorder?.Dispose();
            device.Dispose();
            throw;
        }
    }

    /// <summary>Stops recording and waits for the capture thread to exit (at most one polling interval).</summary>
    public void Dispose()
    {
        _disposing = true;
        try
        {
            _recorder.Dispose();
        }
        catch (Exception e)
        {
            Debug.WriteLine($"QuickParrot: disposing replay capture failed: {e.Message}");
        }
        finally
        {
            _device.Dispose();
        }
    }

    // Runs on the capture thread, so it must not throw or allocate (past the scratch buffers' one-time growth).
    private void OnDataAvailable(ReadOnlySpan<byte> data, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        try
        {
            var replayFlags = CaptureFormats.ToReplayFlags(flags);
            ReadOnlySpan<float> samples;
            if (_format == SampleFormat.Float32)
            {
                samples = MemoryMarshal.Cast<byte, float>(data);
            }
            else
            {
                var count = data.Length / SampleConverter.BytesPerSample(_format);
                if (_scratch.Length < count)
                    _scratch = new float[count];

                samples = _scratch.AsSpan(0, SampleConverter.ToFloat(data, _format, _scratch));
            }

            if (_mixer is null)
            {
                _buffer.Write(samples, qpcPosition, replayFlags);
                return;
            }

            var frames = samples.Length / _mixer.InputChannels;
            var mixedLength = frames * _mixer.OutputChannels;
            if (_mixScratch.Length < mixedLength)
                _mixScratch = new float[mixedLength];

            // The buffer discards the actual content for a silent packet anyway, so skip the mixing work.
            if (!replayFlags.HasFlag(ReplayPacketFlags.Silent))
            {
                for (var frame = 0; frame < frames; frame++)
                {
                    _mixer.MixFrame(
                        samples.Slice(frame * _mixer.InputChannels, _mixer.InputChannels),
                        _mixScratch.AsSpan(frame * _mixer.OutputChannels, _mixer.OutputChannels));
                }
            }

            _buffer.Write(_mixScratch.AsSpan(0, mixedLength), qpcPosition, replayFlags);
        }
        catch (Exception e)
        {
            _dataFailed.TrySetResult(e);
        }
    }
}
