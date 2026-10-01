using QuickParrot.Core.Devices;
using QuickParrot.Core.Editing;
using static QuickParrot.Core.Tests.Editing.TestSignals;

namespace QuickParrot.Core.Tests.Editing;

public class ClipRendererTests
{
    [Fact]
    public void Render_SlicesFadesAndKeepsTheSource()
    {
        var source = Audio(Sine(1000, -20, 1));
        var copy = (float[])source.Samples.Clone();

        var rendered = ClipRenderer.Render(source, new ClipSelection(4800, 9600), new ClipRenderOptions(false, new LoudnessOptions()));

        Assert.Equal(4800, rendered.Audio.FrameCount);
        Assert.Null(rendered.Normalization);
        Assert.Equal((0f, 0f), (rendered.Audio.Samples[0], rendered.Audio.Samples[^1]));
        Assert.Equal(source.Samples[4800 * 2 + 1000], rendered.Audio.Samples[1000]);
        Assert.Equal(copy, source.Samples);
    }

    [Fact]
    public void Render_FoldsSurroundToStereoBeforeNormalizing()
    {
        var mono = Sine(1000, -30, 1, channels: 1);
        var surround = new float[mono.Length * 6];
        for (var f = 0; f < mono.Length; f++)
            surround[f * 6] = surround[f * 6 + 2] = mono[f]; // front left and center

        var rendered = ClipRenderer.Render(Audio(surround, channels: 6), ClipSelection.All(mono.Length), new ClipRenderOptions(true, new LoudnessOptions()));

        Assert.Equal(2, rendered.Audio.Channels);
        Assert.Equal(-18, LoudnessMeter.IntegratedLufs(rendered.Audio), 0.1);
    }
}

public class PreviewDeviceSelectorTests
{
    private static readonly AudioDeviceInfo Speakers = new("speakers", "Speakers (Realtek)", AudioDeviceState.Active);
    private static readonly AudioDeviceInfo Cable = new("cable", "CABLE Input (VB-Audio Virtual Cable)", AudioDeviceState.Active);

    [Fact]
    public void PrefersTheConfiguredMonitor_ThenTheDefault()
    {
        Assert.Equal(Speakers, PreviewDeviceSelector.Select([Speakers, Cable], null, "speakers", "cable").Device);
        Assert.Equal(Speakers, PreviewDeviceSelector.Select([Speakers, Cable], null, null, "speakers").Device);
    }

    [Fact]
    public void RefusesTheCable_EvenAsTheDefault()
    {
        var result = PreviewDeviceSelector.Select([Speakers, Cable], null, null, "cable");

        Assert.Null(result.Device);
        Assert.Contains("virtual cable", result.Problem);
    }

    [Fact]
    public void RefusesAnUnrecognisedCableTheUserConfiguredAsTheCable()
    {
        var odd = new AudioDeviceInfo("odd", "Line 1 (Odd Cable)", AudioDeviceState.Active);

        Assert.Null(PreviewDeviceSelector.Select([Speakers, odd], "odd", "odd", null).Device);
    }

    [Fact]
    public void ReportsWhenThereIsNoOutput()
    {
        Assert.NotNull(PreviewDeviceSelector.Select([], null, null, null).Problem);
    }
}
