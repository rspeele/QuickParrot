using QuickParrot.Core.Devices;
using QuickParrot.Core.Diagnostics;

namespace QuickParrot.Core.Tests.Diagnostics;

public class DiagnosticRulesTests
{
    private static CaptureDeviceInfo Capture(string id, string name, AudioDeviceState state = AudioDeviceState.Active) =>
        new(id, name, state, false);

    private static AudioDeviceInfo Render(string id, string name, AudioDeviceState state = AudioDeviceState.Active) =>
        new(id, name, state);

    [Theory]
    [InlineData("CABLE Input (VB-Audio Virtual Cable)", "CABLE Output (VB-Audio Virtual Cable)")]
    [InlineData("CABLE-A Input (VB-Audio Cable A)", "CABLE-A Output (VB-Audio Cable A)")]
    [InlineData("Line 1 (Virtual Audio Cable)", "Line 1 (Virtual Audio Cable)")]
    public void PartnerName_MapsPlaybackToRecordingSide(string render, string capture)
    {
        Assert.Equal(capture, CableCaptureSelector.PartnerName(render));
    }

    [Fact]
    public void CableCapture_PrefersThePartnerOfTheSelectedCable()
    {
        var devices = new[]
        {
            Capture("a", "CABLE Output (VB-Audio Virtual Cable)"),
            Capture("b", "CABLE-B Output (VB-Audio Cable B)"),
        };

        Assert.Equal("b", CableCaptureSelector.Select(devices, Render("r", "CABLE-B Input (VB-Audio Cable B)"))?.Id);
    }

    [Fact]
    public void CableCapture_FallsBackToAnyCableOutput_ThenMuzychenko_ButNeverVoiceMeeter()
    {
        var voiceMeeter = Capture("vm", "VoiceMeeter Output (VB-Audio VoiceMeeter VAIO)");
        var vac = Capture("vac", "Line 1 (Virtual Audio Cable)");
        var vb = Capture("vb", "CABLE Output (VB-Audio Virtual Cable)");

        Assert.Equal("vb", CableCaptureSelector.Select([voiceMeeter, vac, vb], null)?.Id);
        Assert.Equal("vac", CableCaptureSelector.Select([voiceMeeter, vac], null)?.Id);
        Assert.Null(CableCaptureSelector.Select([voiceMeeter], null));
    }

    [Fact]
    public void CableCapture_IgnoresInactiveDevices()
    {
        var disabled = Capture("vb", "CABLE Output (VB-Audio Virtual Cable)", AudioDeviceState.Disabled);

        Assert.Null(CableCaptureSelector.Select([disabled], Render("r", "CABLE Input (VB-Audio Virtual Cable)")));
    }

    [Fact]
    public void RealPlaybackDevice_SkipsCablesAndInactiveDevices()
    {
        var devices = new[]
        {
            Render("cable", "CABLE Input (VB-Audio Virtual Cable)"),
            Render("vm", "VoiceMeeter Input (VB-Audio VoiceMeeter VAIO)"),
            Render("off", "Speakers", AudioDeviceState.Unplugged),
            Render("hp", "Headphones"),
        };

        Assert.Equal("hp", OutputDeviceSelector.SelectRealPlaybackDevice(devices, devices[0], null)?.Id);
        Assert.Equal("hp", OutputDeviceSelector.SelectRealPlaybackDevice(devices, devices[0], "cable")?.Id);
        Assert.Equal("hp", OutputDeviceSelector.SelectRealPlaybackDevice(devices, devices[0], "off")?.Id);
    }

    [Fact]
    public void RealPlaybackDevice_PrefersTheConfiguredMonitor_AndNeverTheSelectedCable()
    {
        var cable = Render("custom", "Soundboard (renamed cable)");
        var devices = new[] { cable, Render("hp", "Headphones"), Render("spk", "Speakers") };

        Assert.Equal("spk", OutputDeviceSelector.SelectRealPlaybackDevice(devices, cable, "SPK")?.Id);
        Assert.Equal("hp", OutputDeviceSelector.SelectRealPlaybackDevice(devices, cable, "custom")?.Id);
        Assert.Null(OutputDeviceSelector.SelectRealPlaybackDevice([cable], cable, null));
    }

    [Theory]
    [InlineData(null, CommunicationsDucking.ReduceBy80Percent)]
    [InlineData(0, CommunicationsDucking.MuteOthers)]
    [InlineData(1, CommunicationsDucking.ReduceBy80Percent)]
    [InlineData(2, CommunicationsDucking.ReduceBy50Percent)]
    [InlineData(3, CommunicationsDucking.DoNothing)]
    [InlineData(4, null)]
    [InlineData(-1, null)]
    [InlineData("3", null)]
    public void DuckingPreference_FromRegistryValue(object? raw, CommunicationsDucking? expected)
    {
        Assert.Equal(expected, CommunicationsDuckingPreference.FromRegistryValue(raw));
    }

    [Fact]
    public void ListenSetting_FromRawProperties()
    {
        Assert.Equal(new ListenSetting(true, "{0.0.0.00000000}.{x}"), ListenSetting.FromProperties(true, "{0.0.0.00000000}.{x}"));
        Assert.Equal(new ListenSetting(false, null), ListenSetting.FromProperties(null, null));
        Assert.Equal(new ListenSetting(true, null), ListenSetting.FromProperties(true, ""));
        Assert.Equal(new ListenSetting(false, null), ListenSetting.FromProperties(1, 42));
    }
}
