using QuickParrot.Core.Devices;

namespace QuickParrot.Core.Tests.Devices;

public class OutputDeviceMenuTests
{
    private static readonly AudioDeviceInfo Speakers = new("spk", "Speakers", AudioDeviceState.Active);
    private static readonly AudioDeviceInfo Headset = new("hs", "Headset", AudioDeviceState.Unplugged);
    private static readonly AudioDeviceInfo Gone = new("gone", "Old DAC", AudioDeviceState.NotPresent);

    [Fact]
    public void EachMenu_StartsWithItsAutomaticEntry()
    {
        var menu = OutputDeviceMenu.Build([Speakers]);

        Assert.Equal(new OutputDeviceChoice("", "(Auto-detect cable)"), menu.Cable[0]);
        Assert.Equal(new OutputDeviceChoice("", "(Windows default output)"), menu.Monitor[0]);
    }

    [Fact]
    public void ListsPresentDevices_MarkingInactiveOnes()
    {
        var menu = OutputDeviceMenu.Build([Speakers, Headset, Gone]);

        var expected = new[] { new OutputDeviceChoice("spk", "Speakers"), new OutputDeviceChoice("hs", "Headset (unplugged)") };
        Assert.Equal(expected, menu.Cable.Skip(1));
        Assert.Equal(expected, menu.Monitor.Skip(1));
    }
}
