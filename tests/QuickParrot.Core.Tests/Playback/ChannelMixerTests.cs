using QuickParrot.Core.Playback;

namespace QuickParrot.Core.Tests.Playback;

public class ChannelMixerTests
{
    private const float MinusThreeDb = 0.70710677f;

    // 5.1 order: FL, FR, C, LFE, BL, BR
    [Fact]
    public void FiveOneToStereo_KeepsCenterDialogueInBothSides()
    {
        var output = Mix(new ChannelMixer(6, 2), [0, 0, 0.5f, 0, 0, 0]);

        Assert.Equal(0.5f * MinusThreeDb, output[0], 5);
        Assert.Equal(0.5f * MinusThreeDb, output[1], 5);
    }

    [Fact]
    public void FiveOneToStereo_FoldsSurroundsIntoTheirSide_AndDropsLfe()
    {
        var mixer = new ChannelMixer(6, 2);

        Assert.Equal([0.4f, 0f], Mix(mixer, [0.4f, 0, 0, 0, 0, 0]));
        Assert.Equal([0f, 0f], Mix(mixer, [0, 0, 0, 0.9f, 0, 0]));
        Assert.Equal(0.4f * MinusThreeDb, Mix(mixer, [0, 0, 0, 0, 0.4f, 0])[0], 5);
        Assert.Equal(0f, Mix(mixer, [0, 0, 0, 0, 0.4f, 0])[1]);
        Assert.Equal(0.4f * MinusThreeDb, Mix(mixer, [0, 0, 0, 0, 0, 0.4f])[1], 5);
    }

    [Fact]
    public void FiveOneToMono_AveragesTheStereoDownmix()
    {
        var mixer = new ChannelMixer(6, 1);

        Assert.Equal(MinusThreeDb, mixer.Gain(0, 2), 5);
        Assert.Equal(0.5f, mixer.Gain(0, 0));
        Assert.Equal(0f, mixer.Gain(0, 3));
        Assert.Equal(MinusThreeDb / 2, mixer.Gain(0, 4), 5);
    }

    [Fact]
    public void SevenOneToStereo_FoldsSideAndBackSurrounds()
    {
        var mixer = new ChannelMixer(8, 2);

        Assert.Equal(MinusThreeDb, mixer.Gain(0, 6), 5);
        Assert.Equal(MinusThreeDb, mixer.Gain(1, 7), 5);
        Assert.Equal(MinusThreeDb, mixer.Gain(0, 4), 5);
    }

    [Fact]
    public void FiveOneToQuadDevice_KeepsSurroundsInPlace_FoldsCenterToFront()
    {
        var mixer = new ChannelMixer(6, 4);

        Assert.Equal(1f, mixer.Gain(2, 4));
        Assert.Equal(1f, mixer.Gain(3, 5));
        Assert.Equal(MinusThreeDb, mixer.Gain(0, 2), 5);
        Assert.Equal(0f, mixer.Gain(2, 2));
    }

    [Fact]
    public void MonoToStereo_Duplicates()
    {
        Assert.Equal([0.3f, 0.3f], Mix(new ChannelMixer(1, 2), [0.3f]));
    }

    [Fact]
    public void StereoToMono_Averages()
    {
        Assert.Equal([0.5f], Mix(new ChannelMixer(2, 1), [0.4f, 0.6f]));
    }

    [Fact]
    public void StereoToSurroundDevice_UsesFrontPairOnly()
    {
        Assert.Equal([0.1f, 0.2f, 0, 0, 0, 0], Mix(new ChannelMixer(2, 6), [0.1f, 0.2f]));
    }

    [Fact]
    public void MonoToSurroundDevice_PlaysOnFrontPair()
    {
        Assert.Equal([0.3f, 0.3f, 0, 0, 0, 0, 0, 0], Mix(new ChannelMixer(1, 8), [0.3f]));
    }

    [Fact]
    public void SimpleMappings_DoNotLimit()
    {
        Assert.False(new ChannelMixer(1, 2).Limits);
        Assert.False(new ChannelMixer(2, 1).Limits);
        Assert.False(new ChannelMixer(2, 6).Limits);
        Assert.Equal([0.95f, -0.95f], Mix(new ChannelMixer(2, 2), [0.95f, -0.95f]));
    }

    [Fact]
    public void Downmix_IsSoftLimited_NeverExceedingFullScale()
    {
        var mixer = new ChannelMixer(6, 2);
        Assert.True(mixer.Limits);

        var loud = Mix(mixer, [1, 1, 1, 1, 1, 1]);
        var quiet = Mix(mixer, [0.2f, 0, 0.1f, 0, 0.1f, 0]);

        Assert.All(loud, s => Assert.InRange(s, 0.8f, 1f));
        Assert.Equal(0.2f + 0.2f * MinusThreeDb, quiet[0], 5); // below the threshold, untouched
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.5f)]
    [InlineData(-0.8f)]
    public void SoftLimit_PassesQuietSamples(float sample)
    {
        Assert.Equal(sample, ChannelMixer.SoftLimit(sample));
    }

    [Fact]
    public void SoftLimit_IsMonotonic_SymmetricAndBounded()
    {
        var previous = ChannelMixer.SoftLimit(0.8f);
        for (var x = 0.81f; x < 4f; x += 0.01f)
        {
            var y = ChannelMixer.SoftLimit(x);
            Assert.True(y >= previous, $"decreasing at {x}");
            Assert.True(y <= 1f);
            Assert.Equal(-y, ChannelMixer.SoftLimit(-x));
            previous = y;
        }
    }

    [Fact]
    public void ManyChannels_BeyondSevenOne_AreDropped()
    {
        var mixer = new ChannelMixer(10, 2);

        Assert.Equal(0f, mixer.Gain(0, 9));
        Assert.Equal(1f, mixer.Gain(0, 0));
    }

    private static float[] Mix(ChannelMixer mixer, float[] frame)
    {
        var output = new float[mixer.OutputChannels];
        mixer.MixFrame(frame, output);
        return output;
    }
}
