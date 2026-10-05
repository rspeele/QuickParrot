using QuickParrot.Core.Playback;

namespace QuickParrot.Core.Tests.Playback;

public class AudioPhraseComposerTests
{
    [Fact]
    public void MatchingFormats_ConcatenateWithoutChangingSamplesOrAddingSilence()
    {
        AudioPhrasePart[] parts = [new(new float[] { 0.1f, 0.2f }, 48000, 1),
            new(new float[] { -0.3f, 0f, 0.4f }, 48000, 1)];

        var phrase = AudioPhraseComposer.Compose(parts);

        Assert.Equal(48000, phrase.SampleRate);
        Assert.Equal(1, phrase.Channels);
        Assert.Equal(new float[] { 0.1f, 0.2f, -0.3f, 0f, 0.4f }, phrase.Samples.ToArray());
    }

    [Fact]
    public void MonoAndStereo_DuplicateMonoAndPreserveStereoChannels()
    {
        AudioPhrasePart[] parts = [new(new float[] { 0.1f, 0.2f }, 48000, 1),
            new(new float[] { -0.3f, 0.4f, -0.5f, 0.6f }, 48000, 2)];

        var phrase = AudioPhraseComposer.Compose(parts);

        Assert.Equal(2, phrase.Channels);
        Assert.Equal(new float[] { 0.1f, 0.1f, 0.2f, 0.2f, -0.3f, 0.4f, -0.5f, 0.6f }, phrase.Samples.ToArray());
    }

    [Fact]
    public void DifferentRates_UseHighestRateAndPreserveDurationAndChannelSeparation()
    {
        var stereo = Enumerable.Range(0, 64).SelectMany(_ => new float[] { 0.2f, 0f }).ToArray();
        var mono = Enumerable.Repeat(0.3f, 64).ToArray();

        var phrase = AudioPhraseComposer.Compose([new(stereo, 8000, 2), new(mono, 16000, 1)]);

        Assert.Equal(16000, phrase.SampleRate);
        Assert.Equal(2, phrase.Channels);
        Assert.Equal((128 + 64) * 2, phrase.Samples.Length);
        Assert.Equal(0.2f, phrase.Samples.Span[64 * 2], 5);
        for (var frame = 0; frame < 128; frame++)
            Assert.Equal(0f, phrase.Samples.Span[frame * 2 + 1]);
        Assert.All(phrase.Samples[256..].ToArray(), sample => Assert.Equal(0.3f, sample));
    }

    [Fact]
    public void SurroundFragments_FoldIntoStereoWithTheExistingChannelMixer()
    {
        var phrase = AudioPhraseComposer.Compose([new(new float[] { 0f, 0f, 0.5f, 0.8f, 0f, 0f }, 48000, 6)]);

        Assert.Equal(2, phrase.Channels);
        Assert.Equal(0.35355338f, phrase.Samples.Span[0], 5);
        Assert.Equal(phrase.Samples.Span[0], phrase.Samples.Span[1]);
    }

    [Fact]
    public void TotalDurationBeyondLimit_IsRejectedBeforeComposition()
    {
        var fragment = new AudioPhrasePart(new float[301], 1, 1);

        Assert.Throws<InvalidDataException>(() => AudioPhraseComposer.Compose([fragment, fragment]));
    }

    [Fact]
    public void Cancellation_IsHonoredBeforeAllocatingOrConverting()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            AudioPhraseComposer.Compose([new(new float[] { 0f }, 48000, 1)], cancellation.Token));
    }

    [Fact]
    public void IncompleteFrames_AreRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            AudioPhraseComposer.Compose([new(new float[] { 0f, 0f, 0f }, 48000, 2)]));
    }
}
