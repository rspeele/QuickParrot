namespace QuickParrot.Core.Diagnostics;

public enum ElevatedRunStatus
{
    Completed,
    Declined,
    TimedOut,
    CouldNotStart,
}

/// <param name="ExitCode">The helper's <see cref="RepairExitCode"/>, when it completed.</param>
public sealed record ElevatedRunResult(ElevatedRunStatus Status, int ExitCode = 0, string? Error = null);

/// <summary>The thin Windows layer behind <see cref="AudioSetupRepairer"/>. Methods throw on failure.</summary>
public interface IAudioSystemWriter
{
    /// <param name="role">A single role, not a combination.</param>
    void SetDefaultDevice(string deviceId, DeviceRoles role);

    /// <summary>Turns on Listen without elevation; throws <see cref="UnauthorizedAccessException"/> if Windows refuses.</summary>
    void SetListen(string micId, string targetId);

    /// <summary>Runs the elevated helper (a UAC prompt) and waits for it. Never throws.</summary>
    Task<ElevatedRunResult> SetListenElevatedAsync(string micId, string targetId, TimeSpan timeout);

    /// <param name="volume">Null leaves the volume alone.</param>
    void SetLevel(string deviceId, bool muted, float? volume);

    void SetCommunicationsDucking(CommunicationsDucking value);

    void OpenUrl(string url);
}
