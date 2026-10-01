using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using QuickParrot.Core.Diagnostics;
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
    private readonly LoopbackSampleFormat _format;
    private readonly Action<string> _onError;
    private float[] _scratch = [];
    private volatile bool _disposing;
    private volatile bool _stopped;
    private bool _dataErrorReported;

    private LoopbackReplaySession(
        string deviceId, MMDevice device, WasapiRecorder recorder, ReplayBuffer buffer, LoopbackSampleFormat format,
        Action<string> onError, Action<LoopbackReplaySession, Exception?> onStopped)
    {
        DeviceId = deviceId;
        _device = device;
        _recorder = recorder;
        _buffer = buffer;
        _format = format;
        _onError = onError;
        _recorder.DataAvailable += OnDataAvailable;
        _recorder.RecordingStopped += (_, e) =>
        {
            _stopped = true;
            if (!_disposing)
                onStopped(this, e.Exception);
        };
    }

    public string DeviceId { get; }

    public bool HasStopped => _stopped;

    public long StartedAtMilliseconds { get; } = Environment.TickCount64;

    /// <param name="onStopped">Raised on the capture thread if recording stops by itself, e.g. the device vanished.</param>
    public static LoopbackReplaySession Start(
        string deviceId, ReplayBuffer buffer, Action<string> onError, Action<LoopbackReplaySession, Exception?> onStopped)
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

            buffer.Begin(waveFormat.SampleRate, waveFormat.Channels);
            var session = new LoopbackReplaySession(deviceId, device, recorder, buffer, format, onError, onStopped);
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

    // Runs on the capture thread, so it must not throw.
    private void OnDataAvailable(ReadOnlySpan<byte> data, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        try
        {
            var replayFlags = CaptureFormats.ToReplayFlags(flags);
            if (_format == LoopbackSampleFormat.Float32)
            {
                _buffer.Write(MemoryMarshal.Cast<byte, float>(data), qpcPosition, replayFlags);
                return;
            }

            var samples = data.Length / SampleConverter.BytesPerSample(_format);
            if (_scratch.Length < samples)
                _scratch = new float[samples];

            var converted = SampleConverter.ToFloat(data, _format, _scratch);
            _buffer.Write(_scratch.AsSpan(0, converted), qpcPosition, replayFlags);
        }
        catch (Exception e)
        {
            if (!_dataErrorReported)
            {
                _dataErrorReported = true;
                _onError($"The replay buffer couldn't store audio: {e.Message}");
            }
        }
    }
}
