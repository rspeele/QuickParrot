using NAudio.Wave;
using QuickParrot.Core.Playback;

namespace QuickParrot.Audio;

/// <summary>Plays a <see cref="DecodedClip"/> from the start; each output gets its own instance.</summary>
internal sealed class ClipSampleProvider(DecodedClip clip) : ISampleProvider
{
    private int _position;

    public WaveFormat WaveFormat => clip.Format;

    public int Read(Span<float> buffer)
    {
        var count = Math.Min(buffer.Length, clip.Samples.Length - _position);
        clip.Samples.AsSpan(_position, count).CopyTo(buffer);
        _position += count;
        return count;
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
