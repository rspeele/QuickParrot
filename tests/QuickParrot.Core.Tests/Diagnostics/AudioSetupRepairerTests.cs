using QuickParrot.Core.Diagnostics;

namespace QuickParrot.Core.Tests.Diagnostics;

public sealed class FakeAudioSystemWriter : IAudioSystemWriter
{
    public List<string> Calls { get; } = [];

    public Exception? ThrowOnSetListen { get; set; }

    public Exception? ThrowOnAnyChange { get; set; }

    public ElevatedRunResult ElevatedResult { get; set; } = new(ElevatedRunStatus.Completed, (int)RepairExitCode.Success);

    public void SetDefaultDevice(string deviceId, DeviceRoles role) => Record($"default:{deviceId}:{role}");

    public void SetListen(string micId, string targetId)
    {
        Record($"listen:{micId}->{targetId}");
        if (ThrowOnSetListen is { } e)
            throw e;
    }

    public Task<ElevatedRunResult> SetListenElevatedAsync(string micId, string targetId, TimeSpan timeout)
    {
        Record($"elevated-listen:{micId}->{targetId}");
        return Task.FromResult(ElevatedResult);
    }

    public void SetLevel(string deviceId, bool muted, float? volume) => Record($"level:{deviceId}:muted={muted}:volume={volume}");

    public void SetCommunicationsDucking(CommunicationsDucking value) => Record($"ducking:{value}");

    public void OpenUrl(string url) => Record($"open:{url}");

    private void Record(string call)
    {
        Calls.Add(call);
        if (ThrowOnAnyChange is { } e)
            throw e;
    }
}

public class AudioSetupRepairerTests
{
    private readonly FakeAudioSystemWriter _writer = new();

    private Task<RepairResult> Repair(DiagnosticFix fix) => new AudioSetupRepairer(_writer).RepairAsync(fix);

    [Fact]
    public async Task OpenDownloadPage_OpensTheVbCableSite()
    {
        var result = await Repair(new DiagnosticFix(FixKind.OpenCableDownloadPage, "Get"));

        Assert.True(result.Succeeded);
        Assert.Equal(["open:https://vb-audio.com/Cable/"], _writer.Calls);
    }

    [Fact]
    public async Task SetDefaultPlayback_SetsEachRequestedRole()
    {
        var result = await Repair(new DiagnosticFix(
            FixKind.SetDefaultPlayback, "Play", "hp", Roles: DeviceRoles.Console | DeviceRoles.Communications));

        Assert.True(result.Succeeded);
        Assert.Equal(["default:hp:Console", "default:hp:Communications"], _writer.Calls);
    }

    [Fact]
    public async Task SetDefaultRecording_SetsAllThreeRoles()
    {
        await Repair(new DiagnosticFix(FixKind.SetDefaultRecording, "Rec", "cable-out", Roles: (DeviceRoles.Console | DeviceRoles.Multimedia | DeviceRoles.Communications)));

        Assert.Equal(["default:cable-out:Console", "default:cable-out:Multimedia", "default:cable-out:Communications"], _writer.Calls);
    }

    [Fact]
    public async Task SetDefaultRecording_CommunicationsOnly_SetsJustThatRole()
    {
        var result = await Repair(new DiagnosticFix(FixKind.SetDefaultRecording, "Rec", "mic", Roles: DeviceRoles.Communications));

        Assert.True(result.Succeeded);
        Assert.Equal(["default:mic:Communications"], _writer.Calls);
    }

    [Fact]
    public async Task SetDefaultPlayback_CommunicationsOnly_SetsJustThatRole()
    {
        var result = await Repair(new DiagnosticFix(FixKind.SetDefaultPlayback, "Play", "hp", Roles: DeviceRoles.Communications));

        Assert.True(result.Succeeded);
        Assert.Equal(["default:hp:Communications"], _writer.Calls);
    }

    [Fact]
    public async Task RestoreCableLevel_UnmutesAtFullVolume()
    {
        await Repair(new DiagnosticFix(FixKind.RestoreCableLevel, "Fix", "cable-in"));

        Assert.Equal(["level:cable-in:muted=False:volume=1"], _writer.Calls);
    }

    [Fact]
    public async Task UnmuteMic_LeavesVolumeAlone()
    {
        await Repair(new DiagnosticFix(FixKind.UnmuteMic, "Unmute", "mic"));

        Assert.Equal(["level:mic:muted=False:volume="], _writer.Calls);
    }

    [Fact]
    public async Task DisableDucking_SetsDoNothing()
    {
        await Repair(new DiagnosticFix(FixKind.DisableCommunicationsDucking, "Off"));

        Assert.Equal(["ducking:DoNothing"], _writer.Calls);
    }

    [Fact]
    public async Task EnableListen_WithoutElevationWhenAllowed()
    {
        var result = await Repair(new DiagnosticFix(FixKind.EnableListen, "Listen", "mic", "cable-in"));

        Assert.Equal(new RepairResult(true, AudioSetupRepairer.ListenDone), result);
        Assert.Equal(["listen:mic->cable-in"], _writer.Calls);
    }

    [Fact]
    public async Task EnableListen_FallsBackToElevation_OnAccessDenied()
    {
        _writer.ThrowOnSetListen = new UnauthorizedAccessException();

        var result = await Repair(new DiagnosticFix(FixKind.EnableListen, "Listen", "mic", "cable-in"));

        Assert.True(result.Succeeded);
        Assert.Equal(["listen:mic->cable-in", "elevated-listen:mic->cable-in"], _writer.Calls);
    }

    [Fact]
    public async Task EnableListen_OtherFailures_DoNotElevate()
    {
        _writer.ThrowOnSetListen = new InvalidOperationException("device gone");

        var result = await Repair(new DiagnosticFix(FixKind.EnableListen, "Listen", "mic", "cable-in"));

        Assert.False(result.Succeeded);
        Assert.Contains("device gone", result.Message);
        Assert.Equal(["listen:mic->cable-in"], _writer.Calls);
    }

    [Theory]
    [InlineData(ElevatedRunStatus.Completed, 1, "refused")]
    [InlineData(ElevatedRunStatus.Completed, 2, "didn't understand")]
    [InlineData(ElevatedRunStatus.Declined, 0, "declined")]
    [InlineData(ElevatedRunStatus.TimedOut, 0, "in time")]
    [InlineData(ElevatedRunStatus.CouldNotStart, 0, "didn't start")]
    public async Task EnableListen_ReportsElevatedFailures(ElevatedRunStatus status, int exitCode, string expected)
    {
        _writer.ThrowOnSetListen = new UnauthorizedAccessException();
        _writer.ElevatedResult = new ElevatedRunResult(status, exitCode);

        var result = await Repair(new DiagnosticFix(FixKind.EnableListen, "Listen", "mic", "cable-in"));

        Assert.False(result.Succeeded);
        Assert.Contains(expected, result.Message);
    }

    [Fact]
    public async Task WriterFailures_BecomeMessages()
    {
        _writer.ThrowOnAnyChange = new InvalidOperationException("nope");

        var result = await Repair(new DiagnosticFix(FixKind.UnmuteMic, "Unmute", "mic"));

        Assert.Equal(new RepairResult(false, "Couldn't fix it: nope"), result);
    }

    [Theory]
    [InlineData(FixKind.EnableListen)]
    [InlineData(FixKind.SetDefaultPlayback)]
    [InlineData(FixKind.RestoreCableLevel)]
    [InlineData(FixKind.UnmuteMic)]
    public async Task FixesWithoutADevice_FailWithoutChangingAnything(FixKind kind)
    {
        var result = await Repair(new DiagnosticFix(kind, "Fix"));

        Assert.False(result.Succeeded);
        Assert.Empty(_writer.Calls);
    }
}
