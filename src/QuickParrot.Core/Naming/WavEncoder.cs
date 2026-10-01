namespace QuickParrot.Core.Naming;

/// <summary>Encodes mono float samples as an in-memory 16-bit PCM WAV file (no disk I/O).</summary>
public static class WavEncoder
{
    private const int Channels = 1;
    private const int BitsPerSample = 16;

    public static byte[] EncodePcm16(ReadOnlySpan<float> samples, int sampleRate)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));

        var byteRate = sampleRate * Channels * BitsPerSample / 8;
        var blockAlign = (short)(Channels * BitsPerSample / 8);
        var dataSize = samples.Length * 2;

        using var stream = new MemoryStream(44 + dataSize);
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            writer.Write("RIFF"u8);
            writer.Write(36 + dataSize);
            writer.Write("WAVE"u8);
            writer.Write("fmt "u8);
            writer.Write(16);
            writer.Write((short)1); // PCM
            writer.Write((short)Channels);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write(blockAlign);
            writer.Write((short)BitsPerSample);
            writer.Write("data"u8);
            writer.Write(dataSize);

            foreach (var sample in samples)
            {
                var clamped = Math.Clamp(sample, -1f, 1f);
                writer.Write((short)(clamped * short.MaxValue));
            }
        }

        return stream.ToArray();
    }
}
