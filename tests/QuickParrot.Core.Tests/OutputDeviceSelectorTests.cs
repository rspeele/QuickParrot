using QuickParrot.Core.Devices;

namespace QuickParrot.Core.Tests;

public class OutputDeviceSelectorTests
{
    private static readonly AudioDeviceInfo Speakers = new("speakers", "Speakers (Realtek(R) Audio)", AudioDeviceState.Active);
    private static readonly AudioDeviceInfo Headset = new("headset", "Headphones (USB Headset)", AudioDeviceState.Active);
    private static readonly AudioDeviceInfo VbCable = new("vb", "CABLE Input (VB-Audio Virtual Cable)", AudioDeviceState.Active);
    private static readonly AudioDeviceInfo Muzychenko = new("vac", "Line 1 (Virtual Audio Cable)", AudioDeviceState.Active);

    [Fact]
    public void Cable_PrefersVbCableOverMuzychenko()
    {
        var cable = OutputDeviceSelector.SelectCable([Speakers, Muzychenko, VbCable], null);

        Assert.Equal(VbCable, cable);
    }

    [Fact]
    public void Cable_FallsBackToMuzychenko()
    {
        var cable = OutputDeviceSelector.SelectCable([Speakers, Muzychenko], null);

        Assert.Equal(Muzychenko, cable);
    }

    [Fact]
    public void Cable_NoneFound_ReturnsNull()
    {
        Assert.Null(OutputDeviceSelector.SelectCable([Speakers, Headset], null));
    }

    [Fact]
    public void Cable_IgnoresInactiveCables()
    {
        var disabledVb = VbCable with { State = AudioDeviceState.Disabled };

        var cable = OutputDeviceSelector.SelectCable([disabledVb, Muzychenko], null);

        Assert.Equal(Muzychenko, cable);
    }

    [Fact]
    public void Cable_ConfiguredActiveDeviceWins()
    {
        var cable = OutputDeviceSelector.SelectCable([VbCable, Muzychenko], "vac");

        Assert.Equal(Muzychenko, cable);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unplugged")]
    public void Cable_ConfiguredButUnavailable_FallsBackToAutoDetect(string configuredId)
    {
        var unplugged = new AudioDeviceInfo("unplugged", "Some cable", AudioDeviceState.Unplugged);

        var cable = OutputDeviceSelector.SelectCable([unplugged, VbCable], configuredId);

        Assert.Equal(VbCable, cable);
    }

    [Fact]
    public void Monitor_UsesConfiguredDeviceWhenActive()
    {
        var monitor = OutputDeviceSelector.SelectMonitor([Speakers, Headset], "headset", "speakers");

        Assert.Equal(Headset, monitor);
    }

    [Fact]
    public void Monitor_FallsBackToWindowsDefault()
    {
        var disabledHeadset = Headset with { State = AudioDeviceState.Disabled };

        var monitor = OutputDeviceSelector.SelectMonitor([Speakers, disabledHeadset], "headset", "speakers");

        Assert.Equal(Speakers, monitor);
    }

    [Fact]
    public void Monitor_NoDefaultAndNothingConfigured_ReturnsNull()
    {
        Assert.Null(OutputDeviceSelector.SelectMonitor([Speakers], null, null));
    }

    [Fact]
    public void Select_FlagsMonitorThatIsTheCable()
    {
        var selection = OutputDeviceSelector.Select([Speakers, VbCable], null, null, "vb");

        Assert.Equal(VbCable, selection.Cable);
        Assert.True(selection.MonitorIsCable);
    }

    [Fact]
    public void Select_DeviceIdsCompareCaseInsensitively()
    {
        var selection = OutputDeviceSelector.Select([Speakers, VbCable], null, "SPEAKERS", "VB");

        Assert.Equal(Speakers, selection.Monitor);
        Assert.False(selection.MonitorIsCable);
    }

    [Fact]
    public void Select_NormalSetup()
    {
        var selection = OutputDeviceSelector.Select([Speakers, Headset, VbCable], null, null, "headset");

        Assert.Equal(new OutputDeviceSelection(VbCable, Headset, false), selection);
    }

    [Fact]
    public void Describe_MentionsMissingCableAndMonitorIsCableWarning()
    {
        Assert.Contains("no virtual cable", new OutputDeviceSelection(null, Speakers, false).Describe());
        Assert.Contains("warning", new OutputDeviceSelection(VbCable, VbCable, true).Describe());
    }
}
