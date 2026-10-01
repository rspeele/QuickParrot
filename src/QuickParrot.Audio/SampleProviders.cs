using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using QuickParrot.Core.Editing;
using QuickParrot.Core.Playback;

namespace QuickParrot.Audio;

/// <summary>
/// Reads frames [start, end) of an interleaved buffer without copying it, optionally gained and soft-limited.
/// Each reader gets its own instance; the buffer can be shared.
/// </summary>
internal sealed class BufferSampleProvider : ISampleProvider
{
    private readonly ReadOnlyMemory<float> _samples;
    private readonly int _channels;
    private readonly int _endSample;
    private readonly float _gain;
    private int _position;

    public BufferSampleProvider(
        ReadOnlyMemory<float> samples, int sampleRate, int channels, int startFrame = 0, int? endFrame = null, float gain = 1f)
    {
        _samples = samples;
        _channels = channels;
        _position = startFrame * channels;
        _endSample = (endFrame ?? samples.Length / channels) * channels;
        _gain = gain;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
    }

    public WaveFormat WaveFormat { get; }

    /// <summary>The next frame to be read; safe to poll from another thread.</summary>
    public int FramePosition => Volatile.Read(ref _position) / _channels;

    public static BufferSampleProvider Of(EditableAudio audio, int startFrame, int endFrame, float gain = 1f) =>
        new(audio.Samples, audio.SampleRate, audio.Channels, startFrame, endFrame, gain);

    public int Read(Span<float> buffer)
    {
        var count = Math.Min(buffer.Length, _endSample - _position);
        var target = buffer[..count];
        _samples.Span.Slice(_position, count).CopyTo(target);
        if (_gain != 1f)
        {
            for (var i = 0; i < target.Length; i++)
                target[i] = ChannelMixer.SoftLimit(target[i] * _gain);
        }

        Volatile.Write(ref _position, _position + count);
        return count;
    }
}

internal static class SampleChains
{
    /// <summary>Maps channels (see <see cref="ChannelMapSampleProvider"/>), then resamples, only where needed.</summary>
    public static ISampleProvider Convert(ISampleProvider source, int channels, int sampleRate)
    {
        if (source.WaveFormat.Channels != channels)
            source = new ChannelMapSampleProvider(source, channels);
        if (source.WaveFormat.SampleRate != sampleRate)
            source = new WdlResamplingSampleProvider(source, sampleRate);

        return source;
    }
}

/// <summary>Maps a clip onto a device's channel count, downmixing surround sound (see <see cref="ChannelMixer"/>).</summary>
internal sealed class ChannelMapSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly ChannelMixer _mixer;
    private float[] _sourceBuffer = [];

    public ChannelMapSampleProvider(ISampleProvider source, int outChannels)
    {
        _source = source;
        _mixer = new ChannelMixer(source.WaveFormat.Channels, outChannels);
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, outChannels);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(Span<float> buffer)
    {
        var inChannels = _mixer.InputChannels;
        var outChannels = _mixer.OutputChannels;
        var frames = buffer.Length / outChannels;
        var needed = frames * inChannels;
        if (_sourceBuffer.Length < needed)
            _sourceBuffer = new float[needed];

        var framesRead = _source.Read(_sourceBuffer.AsSpan(0, needed)) / inChannels;
        for (var frame = 0; frame < framesRead; frame++)
        {
            _mixer.MixFrame(
                _sourceBuffer.AsSpan(frame * inChannels, inChannels),
                buffer.Slice(frame * outChannels, outChannels));
        }

        return framesRead * outChannels;
    }
}
