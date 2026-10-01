namespace QuickParrot.Core.Diagnostics;

public sealed record RepairResult(bool Succeeded, string Message);

/// <summary>Carries out a <see cref="DiagnosticFix"/> and words the outcome for the user.</summary>
public sealed class AudioSetupRepairer(IAudioSystemWriter writer)
{
    public static readonly TimeSpan ElevationTimeout = TimeSpan.FromMinutes(2);

    // Whether Windows starts or retargets an already-running Listen stream right away is unverified.
    public const string ListenDone =
        "Turned on \"Listen to this device\". If the cable still doesn't hear you, turn Listen off and on in Windows Sound settings.";

    public async Task<RepairResult> RepairAsync(DiagnosticFix fix)
    {
        try
        {
            return fix.Kind switch
            {
                FixKind.OpenCableDownloadPage => OpenDownloadPage(),
                FixKind.EnableListen => await EnableListenAsync(fix),
                FixKind.SetDefaultPlayback => SetDefaults(fix, "playback"),
                FixKind.SetDefaultRecording => SetDefaults(fix, "recording"),
                FixKind.RestoreCableLevel => SetLevel(fix, 1f, "Unmuted and set to 100%."),
                FixKind.UnmuteMic => SetLevel(fix, null, "Unmuted your microphone."),
                FixKind.DisableCommunicationsDucking => DisableDucking(),
                _ => Fail("QuickParrot doesn't know how to make this fix."),
            };
        }
        catch (Exception e)
        {
            return Fail($"Couldn't fix it: {e.Message}");
        }
    }

    private RepairResult OpenDownloadPage()
    {
        writer.OpenUrl(DiagnosticFix.CableDownloadUrl);
        return Ok("Opened the VB-CABLE download page. After installing it, restart your PC and re-check.");
    }

    private async Task<RepairResult> EnableListenAsync(DiagnosticFix fix)
    {
        if (fix.DeviceId is not { } micId || fix.TargetDeviceId is not { } targetId)
            return MissingDevice();

        try
        {
            writer.SetListen(micId, targetId);
            return Ok(ListenDone);
        }
        catch (UnauthorizedAccessException)
        {
            // Falls through to the elevated helper.
        }

        var run = await writer.SetListenElevatedAsync(micId, targetId, ElevationTimeout);
        return run.Status switch
        {
            ElevatedRunStatus.Completed when run.ExitCode == (int)RepairExitCode.Success => Ok(ListenDone),
            ElevatedRunStatus.Completed => Fail($"Couldn't turn on Listen: {RepairCommandLine.Describe(run.ExitCode)}."),
            ElevatedRunStatus.Declined => Fail("Turning on Listen needs administrator permission, which was declined."),
            ElevatedRunStatus.TimedOut => Fail("Turning on Listen didn't finish in time."),
            _ => Fail($"Couldn't turn on Listen: {run.Error ?? "the administrator helper didn't start"}."),
        };
    }

    private RepairResult SetDefaults(DiagnosticFix fix, string kind)
    {
        if (fix.DeviceId is not { } deviceId || fix.Roles == DeviceRoles.None)
            return MissingDevice();

        foreach (var role in DeviceRoleSet.Of(fix.Roles))
            writer.SetDefaultDevice(deviceId, role);

        return Ok($"Changed the default {kind} device.");
    }

    private RepairResult SetLevel(DiagnosticFix fix, float? volume, string done)
    {
        if (fix.DeviceId is not { } deviceId)
            return MissingDevice();

        writer.SetLevel(deviceId, false, volume);
        return Ok(done);
    }

    private RepairResult DisableDucking()
    {
        writer.SetCommunicationsDucking(CommunicationsDucking.DoNothing);
        return Ok("Windows will no longer turn sounds down during voice chat.");
    }

    private static RepairResult MissingDevice() => Fail("This fix doesn't say which device to change.");

    private static RepairResult Ok(string message) => new(true, message);

    private static RepairResult Fail(string message) => new(false, message);
}
