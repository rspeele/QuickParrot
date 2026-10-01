using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace QuickParrot.Core.Grabs;

/// <summary>The parts of a WAV header needed to describe a file.</summary>
public readonly record struct WavInfo(int FormatTag, int Channels, int SampleRate, int BitsPerSample, long DataBytes)
{
    public long Frames => Channels * (BitsPerSample / 8) is var frameBytes and > 0 ? DataBytes / frameBytes : 0;

    public TimeSpan Duration => SampleRate == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds((double)Frames / SampleRate);
}

/// <summary>32-bit float samples read back from a WAV file, with the format they were recorded in.</summary>
public sealed record WavAudio(float[] Samples, int SampleRate, int Channels);

/// <summary>Minimal 32-bit float WAV writing, and reading.</summary>
public static class WavFile
{
    public const int IeeeFloatFormat = 3;
    private const int FmtChunkSize = 18; // non-PCM formats carry a (zero) cbSize field
    private const int HeaderBytes = 12 + 8 + FmtChunkSize + 12 + 8;
    private const int MaxChunksScanned = 64;

    // A sane upper bound on what ReadFloat32 will allocate for: 10 minutes of 8-channel 192 kHz float32 audio.
    // Protects against a corrupt or hostile header causing a huge (or merely implausible) allocation.
    private const long MaxReadableDataBytes = 600L * 192_000 * 8 * sizeof(float);

    public static void WriteFloat32(Stream stream, ReadOnlySpan<float> samples, int sampleRate, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        var frames = samples.Length / channels;
        var dataBytes = (long)frames * channels * sizeof(float);
        if (dataBytes > uint.MaxValue - HeaderBytes)
            throw new ArgumentException("Too much audio for a WAV file.", nameof(samples));

        Span<byte> header = stackalloc byte[HeaderBytes];
        var w = new HeaderWriter(header);
        w.Tag("RIFF");
        w.U32((uint)(HeaderBytes - 8 + dataBytes));
        w.Tag("WAVE");
        w.Tag("fmt ");
        w.U32(FmtChunkSize);
        w.U16(IeeeFloatFormat);
        w.U16((ushort)channels);
        w.U32((uint)sampleRate);
        w.U32((uint)(sampleRate * channels * sizeof(float)));
        w.U16((ushort)(channels * sizeof(float)));
        w.U16(32);
        w.U16(0);
        w.Tag("fact");
        w.U32(4);
        w.U32((uint)frames);
        w.Tag("data");
        w.U32((uint)dataBytes);
        stream.Write(header);

        var data = samples[..(frames * channels)];
        if (BitConverter.IsLittleEndian)
        {
            stream.Write(MemoryMarshal.AsBytes(data));
            return;
        }

        Span<byte> sample = stackalloc byte[4];
        foreach (var value in data)
        {
            BinaryPrimitives.WriteSingleLittleEndian(sample, value);
            stream.Write(sample);
        }
    }

    /// <summary>Reads the format and data size, or null if this isn't a WAV file it understands.</summary>
    public static WavInfo? TryReadInfo(Stream stream)
    {
        Span<byte> chunk = stackalloc byte[12];
        if (!TryReadExactly(stream, chunk) || !Is(chunk[..4], "RIFF"u8) || !Is(chunk[8..12], "WAVE"u8))
            return null;

        WavInfo? format = null;
        Span<byte> header = stackalloc byte[8];
        Span<byte> fmt = stackalloc byte[16];
        for (var i = 0; i < MaxChunksScanned && TryReadExactly(stream, header); i++)
        {
            var size = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
            if (Is(header[..4], "data"u8))
                return format is { } f ? f with { DataBytes = size } : null;

            var skip = size + (size & 1); // chunks are word-aligned
            if (Is(header[..4], "fmt "u8) && size >= 16)
            {
                if (!TryReadExactly(stream, fmt))
                    return null;

                format = new WavInfo(
                    BinaryPrimitives.ReadUInt16LittleEndian(fmt),
                    BinaryPrimitives.ReadUInt16LittleEndian(fmt[2..]),
                    (int)BinaryPrimitives.ReadUInt32LittleEndian(fmt[4..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(fmt[14..]),
                    0);
                skip -= 16;
            }

            if (!Skip(stream, skip))
                return null;
        }

        return null;
    }

    /// <summary>Reads a whole 32-bit float WAV written by <see cref="WriteFloat32"/>.</summary>
    public static WavAudio ReadFloat32(Stream stream)
    {
        var info = TryReadInfo(stream) ?? throw new InvalidDataException("Not a WAV file this app understands.");
        if (info.FormatTag != IeeeFloatFormat || info.BitsPerSample != 32)
            throw new NotSupportedException("Only 32-bit float WAV files can be read back.");

        if (info.DataBytes > MaxReadableDataBytes)
            throw new InvalidDataException("The WAV file's data chunk is larger than this app will read.");

        if (stream.CanSeek && info.DataBytes > Math.Max(0, stream.Length - stream.Position))
            throw new InvalidDataException("The WAV file's header claims more data than the file actually has.");

        var samples = new float[info.Frames * info.Channels];
        var bytes = MemoryMarshal.AsBytes(samples.AsSpan());
        if (!TryReadExactly(stream, bytes))
            throw new EndOfStreamException("The WAV file's data chunk was shorter than its header said.");

        if (!BitConverter.IsLittleEndian)
            for (var i = 0; i < samples.Length; i++)
                samples[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(i * sizeof(float)));

        return new WavAudio(samples, info.SampleRate, info.Channels);
    }

    private static bool Is(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> tag) => bytes.SequenceEqual(tag);

    private static bool TryReadExactly(Stream stream, Span<byte> buffer) =>
        stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) == buffer.Length;

    private static bool Skip(Stream stream, long bytes)
    {
        if (stream.CanSeek)
        {
            stream.Seek(bytes, SeekOrigin.Current);
            return stream.Position <= stream.Length;
        }

        Span<byte> scratch = stackalloc byte[256];
        while (bytes > 0)
        {
            var read = stream.Read(scratch[..(int)Math.Min(bytes, scratch.Length)]);
            if (read == 0)
                return false;

            bytes -= read;
        }

        return true;
    }

    private ref struct HeaderWriter(Span<byte> buffer)
    {
        private readonly Span<byte> _buffer = buffer;
        private int _at;

        public void Tag(string tag)
        {
            Encoding.ASCII.GetBytes(tag, _buffer[_at..]);
            _at += 4;
        }

        public void U16(ushort value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(_buffer[_at..], value);
            _at += 2;
        }

        public void U32(uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(_buffer[_at..], value);
            _at += 4;
        }
    }
}
