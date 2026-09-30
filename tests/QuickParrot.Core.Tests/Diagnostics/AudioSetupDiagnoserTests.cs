using QuickParrot.Core.Devices;
using QuickParrot.Core.Diagnostics;
using static QuickParrot.Core.Diagnostics.AudioSetupDiagnoser;
using static QuickParrot.Core.Tests.Diagnostics.AudioSetups;

namespace QuickParrot.Core.Tests.Diagnostics;

public class AudioSetupDiagnoserTests
{
    private static DiagnosticFinding Single(AudioSetupSnapshot snapshot, string expectedId)
    {
        var findings = Diagnose(snapshot);
        Assert.Equal([expectedId], findings.Ids());
        return findings[0];
    }

    [Fact]
    public void HealthySetup_HasNoFindings()
    {
        Assert.Empty(Diagnose(Healthy));
    }

    [Fact]
    public void MuzychenkoCable_IsRecognised()
    {
        var render = new AudioDeviceInfo("vac-in", "Line 1 (Virtual Audio Cable)", AudioDeviceState.Active);
        var capture = new CaptureDeviceInfo("vac-out", "Line 1 (Virtual Audio Cable)", AudioDeviceState.Active, false);
        var snapshot = Healthy with
        {
            RenderDevices = [Headphones, render],
            CaptureDevices = [RealMic, capture],
            Defaults = new DefaultEndpoints("hp", "hp", "hp", "vac-out", "vac-out", "vac-out"),
        };

        Assert.Empty(Diagnose(snapshot.WithListen("mic", new ListenSetting(true, "vac-in"))));
    }

    [Fact]
    public void UnreadableDevices_GiveOneAdvisory()
    {
        var finding = Single(new AudioSetupSnapshot { DevicesReadable = false }, Ids.DevicesUnreadable);

        Assert.Equal(DiagnosticSeverity.Advisory, finding.Severity);
    }

    [Fact]
    public void NoCableAtAll_IsAnErrorWithTheDownloadPage()
    {
        var snapshot = Healthy with
        {
            RenderDevices = [Headphones],
            CaptureDevices = [RealMic],
            Defaults = new DefaultEndpoints("hp", "hp", "hp", "mic", "mic", "mic"),
        };

        var finding = Single(snapshot, Ids.CableMissing);

        Assert.Equal(DiagnosticSeverity.Error, finding.Severity);
        Assert.Equal(FixKind.OpenCableDownloadPage, finding.Fix?.Kind);
    }

    [Fact]
    public void DisabledCables_CountAsMissing()
    {
        var snapshot = Healthy with
        {
            RenderDevices = [Headphones, CableInput with { State = AudioDeviceState.Disabled }],
            CaptureDevices = [RealMic, CableOutput with { State = AudioDeviceState.Disabled }],
        };

        Single(snapshot, Ids.CableMissing);
    }

    [Fact]
    public void MissingCableInput_IsAnError()
    {
        var snapshot = Healthy with { RenderDevices = [Headphones, CableInput with { State = AudioDeviceState.Disabled }] };

        var finding = Single(snapshot, Ids.CableInputMissing);

        Assert.Equal(DiagnosticSeverity.Error, finding.Severity);
        Assert.Equal(FixKind.OpenCableDownloadPage, finding.Fix?.Kind);
    }

    [Fact]
    public void MissingCableOutput_IsAnError()
    {
        var snapshot = Healthy with { CaptureDevices = [RealMic] };

        Assert.Equal(DiagnosticSeverity.Error, Single(snapshot, Ids.CableOutputMissing).Severity);
    }

    [Fact]
    public void NoRealMic_IsAnError()
    {
        var snapshot = Healthy with { CaptureDevices = [RealMic with { State = AudioDeviceState.Unplugged }, CableOutput] };

        var finding = Single(snapshot, Ids.MicMissing);

        Assert.Equal(DiagnosticSeverity.Error, finding.Severity);
        Assert.Null(finding.Fix);
    }

    [Fact]
    public void ListenOff_IsAnErrorWithListenFix()
    {
        var snapshot = Healthy.WithListen("mic", new ListenSetting(false, "cable-in")) with
        {
            CaptureDevices = [RealMic with { ListenEnabled = false }, CableOutput],
        };

        var finding = Single(snapshot, Ids.ListenDisabled);

        Assert.Equal(DiagnosticSeverity.Error, finding.Severity);
        Assert.Equal(new DiagnosticFix(FixKind.EnableListen, finding.Fix!.Label, "mic", "cable-in"), finding.Fix);
    }

    [Fact]
    public void ListenToAnotherDevice_IsAnError()
    {
        var finding = Single(Healthy.WithListen("mic", new ListenSetting(true, "hp")), Ids.ListenWrongTarget);

        Assert.Equal(DiagnosticSeverity.Error, finding.Severity);
        Assert.Equal(FixKind.EnableListen, finding.Fix?.Kind);
        Assert.Equal("cable-in", finding.Fix?.TargetDeviceId);
        Assert.Contains(Headphones.Name, finding.Explanation);
    }

    [Fact]
    public void ListenTargetMatch_IgnoresCase()
    {
        Assert.Empty(Diagnose(Healthy.WithListen("mic", new ListenSetting(true, "CABLE-IN"))));
    }

    [Fact]
    public void ListenWithoutTarget_FollowsTheDefaultPlaybackDevice()
    {
        Single(Healthy.WithListen("mic", new ListenSetting(true, null)), Ids.ListenWrongTarget);
    }

    [Fact]
    public void ListenWithoutTarget_WhenDefaultIsTheCable_OnlyReportsTheDefault()
    {
        var snapshot = Healthy.WithListen("mic", new ListenSetting(true, null)) with
        {
            Defaults = new DefaultEndpoints("cable-in", "cable-in", "hp", "cable-out", "cable-out", "cable-out"),
        };

        Single(snapshot, Ids.DefaultPlaybackIsCable);
    }

    [Fact]
    public void ListenWithoutTarget_AndUnknownDefaults_IsNotReported()
    {
        var snapshot = Healthy.WithListen("mic", new ListenSetting(true, null)) with { Defaults = null };

        Assert.Empty(Diagnose(snapshot));
    }

    [Fact]
    public void UnknownListen_IsNotReported()
    {
        var snapshot = Healthy.WithListen("mic", null) with { CaptureDevices = [RealMic with { ListenEnabled = false }, CableOutput] };

        Assert.Empty(Diagnose(snapshot));
    }

    [Fact]
    public void ChecksTheConfiguredMic()
    {
        var webcam = new CaptureDeviceInfo("cam", "Microphone (Webcam)", AudioDeviceState.Active, false);
        var snapshot = Healthy.WithListen("cam", new ListenSetting(false, null)) with
        {
            CaptureDevices = [RealMic, webcam, CableOutput],
            Configured = new ConfiguredDevices(null, null, "cam"),
        };

        Assert.Equal("cam", Single(snapshot, Ids.ListenDisabled).Fix?.DeviceId);
    }

    [Fact]
    public void ListenOff_PrefersTheMicStillTargetingTheCable()
    {
        var webcam = new CaptureDeviceInfo("cam", "Microphone (Webcam)", AudioDeviceState.Active, false);
        var snapshot = Healthy
            .WithListen("mic", new ListenSetting(false, "cable-in"))
            .WithListen("cam", new ListenSetting(false, null)) with
        {
            CaptureDevices = [webcam, RealMic with { ListenEnabled = false }, CableOutput],
        };

        Assert.Equal("mic", Single(snapshot, Ids.ListenDisabled).Fix?.DeviceId);
    }

    [Fact]
    public void ConfiguredMicThatIsTheCable_IsIgnored()
    {
        var snapshot = Healthy with { Configured = new ConfiguredDevices(null, null, "cable-out") };

        Assert.Empty(Diagnose(snapshot));
    }

    [Fact]
    public void DefaultPlaybackIsTheCable_IsAnErrorFixedWithFirstRealOutput()
    {
        var snapshot = Healthy with { Defaults = new DefaultEndpoints("cable-in", "cable-in", "hp", "cable-out", "cable-out", "cable-out") };

        var finding = Single(snapshot, Ids.DefaultPlaybackIsCable);

        Assert.Equal(DiagnosticSeverity.Error, finding.Severity);
        Assert.Equal(FixKind.SetDefaultPlayback, finding.Fix?.Kind);
        Assert.Equal("hp", finding.Fix?.DeviceId);
        Assert.Equal(DeviceRoles.Console | DeviceRoles.Multimedia, finding.Fix?.Roles);
    }

    [Fact]
    public void DefaultPlaybackFix_PrefersTheConfiguredMonitor()
    {
        var snapshot = Healthy with
        {
            Defaults = new DefaultEndpoints("cable-in", "cable-in", "cable-in", "cable-out", "cable-out", "cable-out"),
            Configured = new ConfiguredDevices(null, "spk", null),
        };

        var fix = Single(snapshot, Ids.DefaultPlaybackIsCable).Fix;

        Assert.Equal("spk", fix?.DeviceId);
        Assert.Equal(DeviceRoles.All, fix?.Roles);
    }

    [Fact]
    public void OnlyCommunicationsPlaybackIsTheCable_IsAWarning()
    {
        var snapshot = Healthy with { Defaults = new DefaultEndpoints("hp", "hp", "cable-in", "cable-out", "cable-out", "cable-out") };

        var finding = Single(snapshot, Ids.DefaultPlaybackIsCable);

        Assert.Equal(DiagnosticSeverity.Warning, finding.Severity);
        Assert.Equal(DeviceRoles.Communications, finding.Fix?.Roles);
    }

    [Fact]
    public void DefaultPlaybackIsTheCable_WithNoRealOutput_HasNoFix()
    {
        var snapshot = Healthy with
        {
            RenderDevices = [CableInput],
            Defaults = new DefaultEndpoints("cable-in", "cable-in", "cable-in", "cable-out", "cable-out", "cable-out"),
        };

        Assert.Null(Single(snapshot, Ids.DefaultPlaybackIsCable).Fix);
    }

    [Theory]
    [InlineData(true, 1f)]
    [InlineData(false, 0.05f)]
    public void QuietCableInput_IsAWarning(bool muted, float volume)
    {
        var finding = Single(Healthy.WithLevel("cable-in", new EndpointLevel(muted, volume)), Ids.CableInputLevel);

        Assert.Equal(DiagnosticSeverity.Warning, finding.Severity);
        Assert.Equal(new DiagnosticFix(FixKind.RestoreCableLevel, finding.Fix!.Label, "cable-in"), finding.Fix);
    }

    [Fact]
    public void QuietCableOutput_IsAWarning()
    {
        var finding = Single(Healthy.WithLevel("cable-out", new EndpointLevel(false, 0.02f)), Ids.CableOutputLevel);

        Assert.Equal("cable-out", finding.Fix?.DeviceId);
        Assert.Contains("2%", finding.Title);
    }

    [Fact]
    public void CableAtTheThreshold_IsFine()
    {
        Assert.Empty(Diagnose(Healthy.WithLevel("cable-in", new EndpointLevel(false, LowVolumeThreshold))));
    }

    [Fact]
    public void UnknownLevels_AreNotReported()
    {
        var snapshot = Healthy with { Levels = AudioSetupSnapshot.EmptyById<EndpointLevel>() };

        Assert.Empty(Diagnose(snapshot));
    }

    [Fact]
    public void MutedMic_IsAWarning()
    {
        var finding = Single(Healthy.WithLevel("mic", new EndpointLevel(true, 0.6f)), Ids.MicMuted);

        Assert.Equal(new DiagnosticFix(FixKind.UnmuteMic, finding.Fix!.Label, "mic"), finding.Fix);
    }

    [Fact]
    public void MicMutedByQuickParrot_IsNotReported()
    {
        var snapshot = Healthy.WithLevel("mic", new EndpointLevel(true, 0.6f)) with { MicRestorePending = true };

        Assert.Empty(Diagnose(snapshot));
    }

    [Fact]
    public void QuietButUnmutedMic_IsNotReported()
    {
        Assert.Empty(Diagnose(Healthy.WithLevel("mic", new EndpointLevel(false, 0.01f))));
    }

    [Theory]
    [InlineData(CommunicationsDucking.MuteOthers)]
    [InlineData(CommunicationsDucking.ReduceBy80Percent)]
    [InlineData(CommunicationsDucking.ReduceBy50Percent)]
    public void CommunicationsDucking_IsAWarning(CommunicationsDucking ducking)
    {
        var finding = Single(Healthy with { CommunicationsDucking = ducking }, Ids.CommunicationsDucking);

        Assert.Equal(DiagnosticSeverity.Warning, finding.Severity);
        Assert.Equal(FixKind.DisableCommunicationsDucking, finding.Fix?.Kind);
    }

    [Fact]
    public void UnknownDucking_IsNotReported()
    {
        Assert.Empty(Diagnose(Healthy with { CommunicationsDucking = null }));
    }

    [Fact]
    public void RealMicAsDefaultRecording_IsOnlyAnAdvisory()
    {
        var snapshot = Healthy with { Defaults = new DefaultEndpoints("hp", "hp", "hp", "mic", "mic", "mic") };

        var finding = Single(snapshot, Ids.DefaultRecordingNotCable);

        Assert.Equal(DiagnosticSeverity.Advisory, finding.Severity);
        Assert.Equal(new DiagnosticFix(FixKind.SetDefaultRecording, finding.Fix!.Label, "cable-out", Roles: DeviceRoles.All), finding.Fix);
        Assert.Null(new DiagnosticsReport([finding]).StatusLine);
    }

    [Fact]
    public void OnlyCommunicationsRecordingDiffers_FixesJustThatRole()
    {
        var snapshot = Healthy with { Defaults = new DefaultEndpoints("hp", "hp", "hp", "cable-out", "cable-out", "mic") };

        Assert.Equal(DeviceRoles.Communications, Single(snapshot, Ids.DefaultRecordingNotCable).Fix?.Roles);
    }

    [Fact]
    public void UnknownDefaults_SkipDefaultChecks()
    {
        Assert.Empty(Diagnose(Healthy with { Defaults = null }));
    }

    [Fact]
    public void Findings_AreOrderedBySeverityThenCheckOrder()
    {
        var snapshot = Healthy
            .WithListen("mic", new ListenSetting(false, null))
            .WithLevel("cable-in", new EndpointLevel(true, 1f))
            .WithLevel("mic", new EndpointLevel(true, 1f)) with
        {
            Defaults = new DefaultEndpoints("cable-in", "cable-in", "hp", "mic", "mic", "mic"),
            CommunicationsDucking = CommunicationsDucking.ReduceBy80Percent,
        };

        var findings = Diagnose(snapshot);

        Assert.Equal(
            [Ids.ListenDisabled, Ids.DefaultPlaybackIsCable, Ids.CableInputLevel, Ids.MicMuted, Ids.CommunicationsDucking, Ids.DefaultRecordingNotCable],
            findings.Ids());
        Assert.Equal(findings, Diagnose(snapshot));
    }

    [Fact]
    public void ConfiguredCable_IsTheOneChecked()
    {
        var second = new AudioDeviceInfo("cable-b-in", "CABLE-B Input (VB-Audio Cable B)", AudioDeviceState.Active);
        var secondOut = new CaptureDeviceInfo("cable-b-out", "CABLE-B Output (VB-Audio Cable B)", AudioDeviceState.Active, false);
        var snapshot = Healthy with
        {
            RenderDevices = [Headphones, CableInput, second],
            CaptureDevices = [RealMic, CableOutput, secondOut],
            Configured = new ConfiguredDevices("cable-b-in", null, null),
        };

        var findings = Diagnose(snapshot);

        Assert.Equal([Ids.ListenWrongTarget, Ids.DefaultRecordingNotCable], findings.Ids());
        Assert.Equal("cable-b-in", findings[0].Fix?.TargetDeviceId);
        Assert.Equal("cable-b-out", findings[1].Fix?.DeviceId);
    }
}
