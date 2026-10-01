using System.Buffers.Binary;

namespace QuickParrot.Core.Replay;

/// <summary>Converts little-endian captured sample bytes to float.</summary>
public static class SampleConverter
{
    public static int BytesPerSample(SampleFormat format) => format switch
    {
        SampleFormat.Pcm16 => 2,
        SampleFormat.Pcm24 => 3,
        _ => 4,
    };

    /// <summary>Converts as many whole samples as fit in <paramref name="target"/>; returns how many.</summary>
    public static int ToFloat(ReadOnlySpan<byte> source, SampleFormat format, Span<float> target)
    {
        var size = BytesPerSample(format);
        var count = Math.Min(source.Length / size, target.Length);
        for (var i = 0; i < count; i++)
            target[i] = Read(source.Slice(i * size, size), format);

        return count;
    }

    public static float Read(ReadOnlySpan<byte> sample, SampleFormat format) => format switch
    {
        SampleFormat.Float32 => BinaryPrimitives.ReadSingleLittleEndian(sample),
        SampleFormat.Pcm16 => BinaryPrimitives.ReadInt16LittleEndian(sample) / 32768f,
        SampleFormat.Pcm24 => ((sample[2] << 24) | (sample[1] << 16) | (sample[0] << 8)) / 2147483648f,
        _ => BinaryPrimitives.ReadInt32LittleEndian(sample) / 2147483648f,
    };
}
