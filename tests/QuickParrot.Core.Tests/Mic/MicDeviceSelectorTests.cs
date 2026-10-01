using QuickParrot.Core.Devices;

namespace QuickParrot.Core.Tests.Mic;

public class MicDeviceSelectorTests
{
    private static readonly CaptureDeviceInfo Usb = new("usb", "Microphone (USB Audio Device)", AudioDeviceState.Active, false);
    private static readonly CaptureDeviceInfo Webcam = new("cam", "Microphone (Webcam)", AudioDeviceState.Active, false);
    private static readonly CaptureDeviceInfo Cable = new("cable", "CABLE Output (VB-Audio Virtual Cable)", AudioDeviceState.Active, false);

    [Fact]
    public void ConfiguredActiveDevice_Wins()
    {
        var selection = MicDeviceSelector.Select([Usb with { ListenEnabled = true }, Webcam], "CAM", "usb", null);

        Assert.Equal(new MicSelection(Webcam, false), selection);
    }

    [Fact]
    public void ConfiguredDeviceUnavailable_FallsBackToAutoDetection()
    {
        var unplugged = Webcam with { State = AudioDeviceState.Unplugged };

        var selection = MicDeviceSelector.Select([Usb, unplugged], "cam", "usb", null);

        Assert.Equal(new MicSelection(Usb, true), selection);
    }

    [Fact]
    public void PrefersListeningDevice_OverDefault()
    {
        var listening = Webcam with { ListenEnabled = true };

        Assert.Equal(listening, MicDeviceSelector.Select([Usb, listening], null, "usb", "usb").Device);
    }

    [Fact]
    public void AmongListeningDevices_PrefersDefaultThenCommunications()
    {
        var usb = Usb with { ListenEnabled = true };
        var cam = Webcam with { ListenEnabled = true };

        Assert.Equal(cam, MicDeviceSelector.Select([usb, cam], null, "cam", "usb").Device);
        Assert.Equal(cam, MicDeviceSelector.Select([usb, cam], null, "other", "cam").Device);
        Assert.Equal(usb, MicDeviceSelector.Select([usb, cam], null, null, null).Device);
    }

    [Fact]
    public void SkipsCableAndInactiveDevices_EvenWithListenOn()
    {
        var cable = Cable with { ListenEnabled = true };
        var disabled = Webcam with { ListenEnabled = true, State = AudioDeviceState.Disabled };

        Assert.Equal(Usb, MicDeviceSelector.Select([cable, disabled, Usb], null, "usb", null).Device);
    }

    [Fact]
    public void DefaultIsCable_FallsBackToCommunicationsDevice()
    {
        Assert.Equal(Usb, MicDeviceSelector.Select([Cable, Usb], null, "cable", "usb").Device);
    }

    [Fact]
    public void NothingSuitable_SelectsNothing()
    {
        Assert.Equal(new MicSelection(null, false), MicDeviceSelector.Select([Cable, Usb], null, "cable", "cable"));
        Assert.Null(MicDeviceSelector.Select([], null, null, null).Device);
    }

    [Fact]
    public void DeviceIsCable_FollowsName()
    {
        Assert.True(Cable.IsCable);
        Assert.False(Usb.IsCable);
    }
}
