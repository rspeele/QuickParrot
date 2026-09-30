using QuickParrot.Core.Devices;

namespace QuickParrot.Core.Tests;

public class LoopbackTestSetupTests
{
    private static readonly AudioDeviceInfo Speakers = new("speakers", "Speakers (Realtek(R) Audio)", AudioDeviceState.Active);
    private static readonly AudioDeviceInfo Headset = new("headset", "Headphones (USB Headset)", AudioDeviceState.Active);
    private static readonly AudioDeviceInfo VbCableIn = new("vb-in", "CABLE Input (VB-Audio Virtual Cable)", AudioDeviceState.Active);
    private static readonly CaptureDeviceInfo VbCableOut = new("vb-out", "CABLE Output (VB-Audio Virtual Cable)", AudioDeviceState.Active, false);
    private static readonly CaptureDeviceInfo RealMic = new("mic", "Microphone (USB Headset)", AudioDeviceState.Active, true);

    [Fact]
    public void Resolve_NormalSetup_FindsAllThreeDevices()
    {
        var setup = LoopbackTestSetup.Resolve(
            [Speakers, Headset, VbCableIn], [VbCableOut, RealMic], null, "headset", "speakers");

        Assert.Equal(VbCableIn.Id, setup.CableRenderId);
        Assert.Equal(VbCableOut.Id, setup.CableCaptureId);
        Assert.Equal(Headset.Id, setup.MonitorRenderId);
        Assert.True(setup.IsReady);
    }

    [Fact]
    public void Resolve_NoCable_NotReady()
    {
        var setup = LoopbackTestSetup.Resolve([Speakers, Headset], [RealMic], null, "headset", "speakers");

        Assert.Null(setup.CableRenderId);
        Assert.Null(setup.CableCaptureId);
        Assert.False(setup.IsReady);
    }

    [Fact]
    public void Resolve_CableRenderButNoCaptureSide_NotReady()
    {
        var setup = LoopbackTestSetup.Resolve([Speakers, VbCableIn], [RealMic], null, null, "speakers");

        Assert.Equal(VbCableIn.Id, setup.CableRenderId);
        Assert.Null(setup.CableCaptureId);
        Assert.False(setup.IsReady);
    }

    [Fact]
    public void Resolve_MonitorIsCable_MonitorRenderIdIsNull()
    {
        var setup = LoopbackTestSetup.Resolve([VbCableIn], [VbCableOut], null, null, VbCableIn.Id);

        Assert.Equal(VbCableIn.Id, setup.CableRenderId);
        Assert.Equal(VbCableOut.Id, setup.CableCaptureId);
        Assert.Null(setup.MonitorRenderId);
        Assert.True(setup.IsReady); // playback is skipped, but the test itself can still run
    }

    [Fact]
    public void Resolve_NoMonitorConfiguredOrDefault_MonitorRenderIdIsNull()
    {
        var setup = LoopbackTestSetup.Resolve([VbCableIn], [VbCableOut], null, null, null);

        Assert.True(setup.IsReady);
        Assert.Null(setup.MonitorRenderId);
    }

    [Fact]
    public void Resolve_ConfiguredCableWins()
    {
        // Distinctly named so CableCaptureSelector's partner-name matching can't confuse it with VbCableIn/Out.
        var otherCable = new AudioDeviceInfo("other", "CABLE Input (VB-Audio Virtual Cable) (2)", AudioDeviceState.Active);
        var otherCapture = new CaptureDeviceInfo("other-out", "CABLE Output (VB-Audio Virtual Cable) (2)", AudioDeviceState.Active, false);

        var setup = LoopbackTestSetup.Resolve(
            [VbCableIn, otherCable], [VbCableOut, otherCapture], "other", null, null);

        Assert.Equal(otherCable.Id, setup.CableRenderId);
        Assert.Equal(otherCapture.Id, setup.CableCaptureId);
    }
}
