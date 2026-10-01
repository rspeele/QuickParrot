using System.Buffers.Binary;
using System.Text;
using QuickParrot.Core.Grabs;

namespace QuickParrot.Core.Tests.Grabs;

public sealed class WavFileTests
{
    [Fact]
    public void WriteFloat32_WritesAFloatWaveHeaderAndData()
    {
        using var stream = new MemoryStream();

        WavFile.WriteFloat32(stream, [0.5f, -0.5f, 0.25f, -0.25f], 48_000, 2);

        var bytes = stream.ToArray();
        Assert.Equal("RIFF", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal(bytes.Length - 8, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(bytes, 8, 4));
        Assert.Equal(WavFile.IeeeFloatFormat, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(20)));
        Assert.Equal(48_000 * 2 * 4, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(28)));
        Assert.Equal("data", Encoding.ASCII.GetString(bytes, bytes.Length - 16 - 8, 4));
        Assert.Equal(-0.25f, BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(bytes.Length - 4)));
    }

    [Fact]
    public void WrittenFile_ReadsBack()
    {
        using var stream = new MemoryStream();
        WavFile.WriteFloat32(stream, new float[44_100 * 2], 44_100, 1);
        stream.Position = 0;

        var info = WavFile.TryReadInfo(stream);

        Assert.Equal(new WavInfo(WavFile.IeeeFloatFormat, 1, 44_100, 32, 44_100 * 2 * 4), info);
        Assert.Equal(TimeSpan.FromSeconds(2), info!.Value.Duration);
    }

    [Fact]
    public void PartialFrames_AreNotWritten()
    {
        using var stream = new MemoryStream();
        WavFile.WriteFloat32(stream, [1f, 2f, 3f], 1000, 2);
        stream.Position = 0;

        Assert.Equal(1, WavFile.TryReadInfo(stream)!.Value.Frames);
    }

    [Fact]
    public void TryReadInfo_SkipsUnknownChunks()
    {
        using var stream = new MemoryStream();
        var w = new BinaryWriter(stream);
        w.Write("RIFF"u8);
        w.Write(0);
        w.Write("WAVE"u8);
        w.Write("LIST"u8);
        w.Write(3);
        w.Write(new byte[4]); // 3 bytes plus the pad byte
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)2);
        w.Write(8000);
        w.Write(8000 * 4);
        w.Write((short)4);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(8000 * 4);
        w.Flush();
        stream.Position = 0;

        Assert.Equal(TimeSpan.FromSeconds(1), WavFile.TryReadInfo(stream)!.Value.Duration);
    }

    [Theory]
    [InlineData("")]
    [InlineData("RIFF....WAVE")]
    [InlineData("RIFX....WAVEfmt ")]
    [InlineData("RIFF....WAVEdata\0\0\0\0")]
    public void TryReadInfo_RejectsNonWavData(string content)
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(content));

        Assert.Null(WavFile.TryReadInfo(stream));
    }

    [Fact]
    public void ReadFloat32_RoundTrips()
    {
        using var stream = new MemoryStream();
        WavFile.WriteFloat32(stream, [0.5f, -0.5f, 0.25f, -0.25f], 48_000, 2);
        stream.Position = 0;

        var audio = WavFile.ReadFloat32(stream);

        Assert.Equal([0.5f, -0.5f, 0.25f, -0.25f], audio.Samples);
        Assert.Equal(48_000, audio.SampleRate);
        Assert.Equal(2, audio.Channels);
    }

    [Fact]
    public void ReadFloat32_RejectsOtherFormats()
    {
        using var stream = new MemoryStream();
        var w = new BinaryWriter(stream);
        w.Write("RIFF"u8);
        w.Write(0);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1); // PCM, not IEEE float
        w.Write((short)1);
        w.Write(8000);
        w.Write(8000 * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(4);
        w.Write(new byte[4]);
        w.Flush();
        stream.Position = 0;

        Assert.Throws<NotSupportedException>(() => WavFile.ReadFloat32(stream));
    }
}
