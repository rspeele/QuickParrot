using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using QuickParrot.Core.Diagnostics;

namespace QuickParrot.Audio;

/// <summary>Records a capture device in shared mode, as mono float, for the setup test.</summary>
internal sealed class LoopbackTesterCapture : IDisposable
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private readonly MMDevice _device;
    private readonly WasapiRecorder _recorder;
    private readonly TimeSpan _maxDuration;
    private readonly TaskCompletionSource<Exception?> _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private LoopbackTestRecording? _recording;
    private volatile string? _captureError;

    private LoopbackTesterCapture(MMDevice device, WasapiRecorder recorder, TimeSpan maxDuration)
    {
        _device = device;
        _recorder = recorder;
        _maxDuration = maxDuration;
        _recorder.DataAvailable += OnDataAvailable;
        _recorder.RecordingStopped += (_, e) => _stopped.TrySetResult(e.Exception);
    }

    public static LoopbackTesterCapture Start(string deviceId, TimeSpan maxDuration)
    {
        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDevice(deviceId);
        WasapiRecorder? recorder = null;
        try
        {
            recorder = new WasapiRecorderBuilder().WithDevice(device).WithSharedMode().WithEventSync().Build();
            var capture = new LoopbackTesterCapture(device, recorder, maxDuration);
            recorder.StartRecording();
            return capture;
        }
        catch
        {
            recorder?.Dispose();
            device.Dispose();
            throw;
        }
    }

    /// <summary>Stops recording and returns what was captured, or why capturing failed.</summary>
    public async Task<(float[] Samples, int SampleRate, string? Error)> StopAsync()
    {
        await Task.Run(_recorder.StopRecording);
        Exception? stopError;
        try
        {
            stopError = await _stopped.Task.WaitAsync(StopTimeout);
        }
        catch (TimeoutException)
        {
            stopError = null;
        }

        var recording = Volatile.Read(ref _recording);
        var error = stopError?.Message ?? _captureError;
        return (recording?.ToArray() ?? [], recording?.SampleRate ?? 0, error);
    }

    public void Dispose()
    {
        try
        {
            _recorder.Dispose();
        }
        catch (Exception e)
        {
            Debug.WriteLine($"QuickParrot: disposing test capture failed: {e.Message}");
        }
        finally
        {
            _device.Dispose();
        }
    }

    // Runs on the capture thread, so it must not throw.
    private void OnDataAvailable(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        try
        {
            if (_recording is null)
            {
                var format = _recorder.WaveFormat;
                if (ToSampleFormat(format) is not { } sampleFormat)
                {
                    _captureError ??= $"CABLE Output uses an unsupported format ({format}).";
                    return;
                }

                Volatile.Write(ref _recording, new LoopbackTestRecording(format.SampleRate, format.Channels, sampleFormat, _maxDuration));
            }

            _recording.Append(buffer, flags.HasFlag(AudioClientBufferFlags.Silent));
        }
        catch (Exception e)
        {
            _captureError ??= e.Message;
        }
    }

    private static LoopbackSampleFormat? ToSampleFormat(WaveFormat format)
    {
        var encoding = format is WaveFormatExtensible extensible
            ? extensible.SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT ? WaveFormatEncoding.IeeeFloat
            : extensible.SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_PCM ? WaveFormatEncoding.Pcm
            : WaveFormatEncoding.Unknown
            : format.Encoding;

        return (encoding, format.BitsPerSample) switch
        {
            (WaveFormatEncoding.IeeeFloat, 32) => LoopbackSampleFormat.Float32,
            (WaveFormatEncoding.Pcm, 16) => LoopbackSampleFormat.Pcm16,
            (WaveFormatEncoding.Pcm, 24) => LoopbackSampleFormat.Pcm24,
            (WaveFormatEncoding.Pcm, 32) => LoopbackSampleFormat.Pcm32,
            _ => null,
        };
    }
}
