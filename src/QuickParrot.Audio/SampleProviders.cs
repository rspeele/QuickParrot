using NAudio.Wave;

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

/// <summary>Maps mono/stereo audio onto a device's channel count (mono is duplicated, extra channels silent).</summary>
internal sealed class ChannelMapSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _inChannels;
    private readonly int _outChannels;
    private float[] _sourceBuffer = [];

    public ChannelMapSampleProvider(ISampleProvider source, int outChannels)
    {
        _source = source;
        _inChannels = source.WaveFormat.Channels;
        _outChannels = outChannels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, outChannels);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(Span<float> buffer)
    {
        var frames = buffer.Length / _outChannels;
        var needed = frames * _inChannels;
        if (_sourceBuffer.Length < needed)
            _sourceBuffer = new float[needed];

        var framesRead = _source.Read(_sourceBuffer.AsSpan(0, needed)) / _inChannels;
        for (var frame = 0; frame < framesRead; frame++)
        {
            MapFrame(
                _sourceBuffer.AsSpan(frame * _inChannels, _inChannels),
                buffer.Slice(frame * _outChannels, _outChannels));
        }

        return framesRead * _outChannels;
    }

    private static void MapFrame(ReadOnlySpan<float> input, Span<float> output)
    {
        output.Clear();
        if (output.Length == 1)
            output[0] = input.Length == 1 ? input[0] : (input[0] + input[1]) / 2;
        else if (input.Length == 1)
            output[0] = output[1] = input[0];
        else
            input[..Math.Min(input.Length, output.Length)].CopyTo(output);
    }
}
