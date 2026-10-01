using QuickParrot.Core.Naming;

namespace QuickParrot.Core.Tests.Naming;

public class WavEncoderTests
{
    [Fact]
    public void Encodes_StandardFortyFourByteHeader_PlusTwoBytesPerSample()
    {
        float[] samples = [0f, 0.5f, -0.5f, 1f];

        var wav = WavEncoder.EncodePcm16(samples, 16_000);

        Assert.Equal(44 + samples.Length * 2, wav.Length);
    }

    [Fact]
    public void Header_HasRiffWaveFmtAndDataChunkIds()
    {
        var wav = WavEncoder.EncodePcm16([0f], 16_000);

        Assert.Equal("RIFF", Ascii(wav, 0, 4));
        Assert.Equal("WAVE", Ascii(wav, 8, 4));
        Assert.Equal("fmt ", Ascii(wav, 12, 4));
        Assert.Equal("data", Ascii(wav, 36, 4));
    }

    [Fact]
    public void Header_EncodesSampleRateChannelsAndBitDepth()
    {
        var wav = WavEncoder.EncodePcm16([0f, 0f], 22_050);

        Assert.Equal(1, BitConverter.ToInt16(wav, 22)); // mono
        Assert.Equal(22_050, BitConverter.ToInt32(wav, 24));
        Assert.Equal(16, BitConverter.ToInt16(wav, 34)); // bits per sample
        Assert.Equal(4, BitConverter.ToInt32(wav, 40)); // data chunk size: 2 samples * 2 bytes
    }

    [Fact]
    public void RiffChunkSize_IsThirtySixPlusDataSize()
    {
        float[] samples = new float[100];

        var wav = WavEncoder.EncodePcm16(samples, 16_000);

        Assert.Equal(36 + 200, BitConverter.ToInt32(wav, 4));
    }

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(1f, short.MaxValue)]
    [InlineData(-1f, -short.MaxValue)]
    [InlineData(2f, short.MaxValue)] // clamped
    [InlineData(-2f, -short.MaxValue)] // clamped
    public void SamplesAreEncodedAsClampedSixteenBitPcm(float sample, short expected)
    {
        var wav = WavEncoder.EncodePcm16([sample], 16_000);

        Assert.Equal(expected, BitConverter.ToInt16(wav, 44));
    }

    private static string Ascii(byte[] data, int offset, int length) =>
        System.Text.Encoding.ASCII.GetString(data, offset, length);
}
