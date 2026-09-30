using QuickParrot.Core.Devices;

namespace QuickParrot.Core.Tests.Mic;

public class MicDeviceMenuTests
{
    private static readonly CaptureDeviceInfo Usb = new("usb", "Microphone (USB Audio Device)", AudioDeviceState.Active, false);
    private static readonly CaptureDeviceInfo Webcam = new("cam", "Microphone (Webcam)", AudioDeviceState.Active, false);
    private static readonly CaptureDeviceInfo Cable = new("cable", "CABLE Output (VB-Audio Virtual Cable)", AudioDeviceState.Active, false);

    [Fact]
    public void AutoEntry_ShowsWhatAutoResolvesTo()
    {
        var choices = MicDeviceMenu.Build([Usb, Webcam, Cable], defaultId: "usb", communicationsId: null);

        Assert.Equal("", choices[0].Id);
        Assert.Equal("Auto (Microphone (USB Audio Device))", choices[0].Name);
    }

    [Fact]
    public void AutoEntry_WhenNoMicFound_SaysSo()
    {
        var choices = MicDeviceMenu.Build([Cable], defaultId: null, communicationsId: null);

        Assert.Equal("Auto (no microphone found)", choices[0].Name);
    }

    [Fact]
    public void ExcludesTheCable_ButListsOtherDevices()
    {
        var choices = MicDeviceMenu.Build([Usb, Webcam, Cable], defaultId: "usb", communicationsId: null);

        Assert.DoesNotContain(choices, c => c.Id == "cable");
        Assert.Contains(choices, c => c.Id == "usb");
        Assert.Contains(choices, c => c.Id == "cam");
    }

    [Fact]
    public void MarksListenEnabledDevices()
    {
        var listening = Webcam with { ListenEnabled = true };

        var choices = MicDeviceMenu.Build([Usb, listening], defaultId: "usb", communicationsId: null);

        Assert.Equal("Microphone (USB Audio Device)", choices.Single(c => c.Id == "usb").Name);
        Assert.Equal("Microphone (Webcam) · Listen on", choices.Single(c => c.Id == "cam").Name);
    }

    [Fact]
    public void ExcludesNotPresentDevices()
    {
        var gone = Usb with { State = AudioDeviceState.NotPresent };

        var choices = MicDeviceMenu.Build([gone, Webcam], defaultId: null, communicationsId: null);

        Assert.DoesNotContain(choices, c => c.Id == "usb");
    }

    [Fact]
    public void InactiveDevices_ShowTheirState()
    {
        var disabled = Usb with { State = AudioDeviceState.Disabled };

        var choices = MicDeviceMenu.Build([disabled], defaultId: null, communicationsId: null);

        Assert.Equal("Microphone (USB Audio Device) (disabled)", choices.Single(c => c.Id == "usb").Name);
    }
}
