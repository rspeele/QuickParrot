using QuickParrot.Core.Naming;

namespace QuickParrot.Core.Tests.Naming;

public class AudioDownmixerTests
{
    [Fact]
    public void Mono_ReturnsSameSamples()
    {
        float[] source = [0.1f, -0.2f, 0.3f];

        Assert.Equal(source, AudioDownmixer.ToMono(source, 1));
    }

    [Fact]
    public void Stereo_AveragesChannels()
    {
        float[] interleaved = [1f, -1f, 0.4f, 0.6f];

        Assert.Equal([0f, 0.5f], AudioDownmixer.ToMono(interleaved, 2));
    }

    [Fact]
    public void InvalidChannelCount_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioDownmixer.ToMono([0f], 0));
    }
}
