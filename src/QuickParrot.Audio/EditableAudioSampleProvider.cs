using NAudio.Wave;
using QuickParrot.Core.Editing;
using QuickParrot.Core.Playback;

namespace QuickParrot.Audio;

/// <summary>Reads frames [start, end) of editor audio straight from its buffer, optionally gained and soft-limited.</summary>
internal sealed class EditableAudioSampleProvider : ISampleProvider
{
    private readonly EditableAudio _audio;
    private readonly int _endSample;
    private readonly float _gain;
    private int _position;

    public EditableAudioSampleProvider(EditableAudio audio, int startFrame, int endFrame, float gain = 1f)
    {
        _audio = audio;
        _position = startFrame * audio.Channels;
        _endSample = endFrame * audio.Channels;
        _gain = gain;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(audio.SampleRate, audio.Channels);
    }

    public WaveFormat WaveFormat { get; }

    /// <summary>The next frame to be read; safe to poll from another thread.</summary>
    public int FramePosition => Volatile.Read(ref _position) / _audio.Channels;

    public int Read(Span<float> buffer)
    {
        var count = Math.Min(buffer.Length, _endSample - _position);
        var target = buffer[..count];
        _audio.Samples.AsSpan(_position, count).CopyTo(target);
        if (_gain != 1f)
        {
            for (var i = 0; i < target.Length; i++)
                target[i] = ChannelMixer.SoftLimit(target[i] * _gain);
        }

        Volatile.Write(ref _position, _position + count);
        return count;
    }
}
