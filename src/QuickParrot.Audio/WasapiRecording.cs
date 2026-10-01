using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace QuickParrot.Audio;

/// <summary>Starting and releasing a shared-mode WASAPI recorder together with the device it records.</summary>
internal static class WasapiRecording
{
    /// <summary>
    /// Builds a recorder on <paramref name="deviceId"/>, wraps it with <paramref name="create"/> (which attaches the
    /// handlers) and starts recording; releases both if any step fails.
    /// </summary>
    public static T Start<T>(
        string deviceId,
        Func<WasapiRecorderBuilder, WasapiRecorderBuilder> configure,
        Func<MMDevice, WasapiRecorder, T> create)
    {
        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDevice(deviceId);
        WasapiRecorder? recorder = null;
        try
        {
            recorder = configure(new WasapiRecorderBuilder().WithDevice(device).WithSharedMode()).Build();
            var owner = create(device, recorder);
            recorder.StartRecording();
            return owner;
        }
        catch
        {
            recorder?.Dispose();
            device.Dispose();
            throw;
        }
    }

    /// <summary>Disposes the recorder (logging, not throwing, if that fails), then releases the device.</summary>
    public static void Release(WasapiRecorder recorder, MMDevice device, string what)
    {
        try
        {
            recorder.Dispose();
        }
        catch (Exception e)
        {
            Debug.WriteLine($"QuickParrot: disposing {what} failed: {e.Message}");
        }
        finally
        {
            device.Dispose();
        }
    }
}
