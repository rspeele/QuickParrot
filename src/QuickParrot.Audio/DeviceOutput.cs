using NAudio.Wave.SampleProviders;

namespace QuickParrot.Audio;

/// <summary>One clip opened on one device at an adjustable volume, ready to start the moment it's needed.</summary>
internal sealed class DeviceOutput : IDisposable
{
    private readonly WasapiOutput _output;
    private readonly VolumeSampleProvider _volume;

    private DeviceOutput(WasapiOutput output, VolumeSampleProvider volume)
    {
        _output = output;
        _volume = volume;
    }

    /// <summary>Completes when playback ends by itself, with the error if it failed; never once disposed.</summary>
    public Task<Exception?> Stopped => _output.Stopped;

    public float Volume
    {
        set => _volume.Volume = value;
    }

    public static DeviceOutput Open(string deviceId, DecodedClip clip, float volume)
    {
        VolumeSampleProvider? volumeStage = null;
        var output = WasapiOutput.Open(
            deviceId,
            new BufferSampleProvider(clip.Samples, clip.Format.SampleRate, clip.Format.Channels),
            matched => volumeStage = new VolumeSampleProvider(matched) { Volume = volume });
        return new DeviceOutput(output, volumeStage!);
    }

    public void Play() => _output.Play();

    // Asynchronous so replacing a clip doesn't wait for the old stream to wind down (~25-50 ms per device).
    public void Dispose() => _output.Dispose();
}
