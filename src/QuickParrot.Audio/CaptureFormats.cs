using NAudio.CoreAudioApi;
using NAudio.Wave;
using QuickParrot.Core.Replay;

namespace QuickParrot.Audio;

internal static class CaptureFormats
{
    public static SampleFormat? ToSampleFormat(WaveFormat format)
    {
        var encoding = format is WaveFormatExtensible extensible
            ? extensible.SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT ? WaveFormatEncoding.IeeeFloat
            : extensible.SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_PCM ? WaveFormatEncoding.Pcm
            : WaveFormatEncoding.Unknown
            : format.Encoding;

        return (encoding, format.BitsPerSample) switch
        {
            (WaveFormatEncoding.IeeeFloat, 32) => SampleFormat.Float32,
            (WaveFormatEncoding.Pcm, 16) => SampleFormat.Pcm16,
            (WaveFormatEncoding.Pcm, 24) => SampleFormat.Pcm24,
            (WaveFormatEncoding.Pcm, 32) => SampleFormat.Pcm32,
            _ => null,
        };
    }

    public static ReplayPacketFlags ToReplayFlags(AudioClientBufferFlags flags)
    {
        var result = ReplayPacketFlags.None;
        if (flags.HasFlag(AudioClientBufferFlags.Silent))
            result |= ReplayPacketFlags.Silent;
        if (flags.HasFlag(AudioClientBufferFlags.DataDiscontinuity))
            result |= ReplayPacketFlags.Discontinuity;
        if (flags.HasFlag(AudioClientBufferFlags.TimestampError))
            result |= ReplayPacketFlags.TimestampError;

        return result;
    }
}
