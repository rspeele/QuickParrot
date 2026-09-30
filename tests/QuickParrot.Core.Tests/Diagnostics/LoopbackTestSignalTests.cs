using QuickParrot.Core.Diagnostics;

namespace QuickParrot.Core.Tests.Diagnostics;

public class LoopbackTestSignalTests
{
    private readonly LoopbackTestSignal _signal = LoopbackTestSignal.Default;

    [Fact]
    public void Render_IsDeterministic_AndSizedToTheChime()
    {
        var first = _signal.Render(48000);

        Assert.Equal(first, _signal.Render(48000));
        Assert.Equal((int)Math.Ceiling(_signal.DurationSeconds * 48000), first.Length);
        Assert.Equal((int)Math.Ceiling(_signal.DurationSeconds * 44100), _signal.Render(44100).Length);
    }

    [Fact]
    public void Render_StaysBelowPeakAmplitude_AndStartsAndEndsSilent()
    {
        var samples = _signal.Render(48000);

        Assert.All(samples, s => Assert.InRange(Math.Abs(s), 0, _signal.PeakAmplitude + 1e-6));
        Assert.True(samples.Max() > _signal.PeakAmplitude * 0.9);
        Assert.Equal(0f, samples[0]);
        Assert.True(Math.Abs(samples[^1]) < 1e-3);
    }

    [Fact]
    public void Default_PlaysTwoPitchesTwice_WithAGap()
    {
        Assert.Equal(2, _signal.Frequencies.Count);
        Assert.Equal(4, _signal.Notes.Count);
        Assert.True(_signal.Notes[2].StartSeconds > _signal.Notes[1].EndSeconds);
        Assert.InRange(_signal.DurationSeconds, 3, 4);
    }

    [Fact]
    public void Amplitude_RisesQuickly_ThenDecays_AndIsZeroOutsideTheNote()
    {
        var note = _signal.Notes[1];

        Assert.Equal(0, _signal.AmplitudeAt(note, -0.01));
        Assert.Equal(0, _signal.AmplitudeAt(note, note.DurationSeconds + 0.01));
        Assert.True(_signal.AmplitudeAt(note, 0.02) > _signal.AmplitudeAt(note, 0.5));
        Assert.True(_signal.AmplitudeAt(note, 0.5) > _signal.PeakAmplitude * 0.3);
    }

    [Fact]
    public void Constructor_RejectsAnEmptyChime()
    {
        Assert.Throws<ArgumentException>(() => new LoopbackTestSignal([], 0.2));
    }
}
