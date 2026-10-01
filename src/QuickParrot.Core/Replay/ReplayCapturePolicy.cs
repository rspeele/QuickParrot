namespace QuickParrot.Core.Replay;

/// <summary>What replay capture should do to match the requested state.</summary>
public enum ReplayCaptureStep
{
    /// <summary>Capture is off: stop, free the buffer and forget any reported error.</summary>
    Disable,

    /// <summary>The running session already records the target.</summary>
    Keep,

    /// <summary>Stop, and report that there's no output device to record.</summary>
    NoDevice,

    /// <summary>Stop, and leave the target alone: it failed and nothing has changed since.</summary>
    Wait,

    /// <summary>Stop whatever is running and start recording the target.</summary>
    Start,
}

/// <summary>Decisions for loopback replay capture, kept apart from the device handling.</summary>
public static class ReplayCapturePolicy
{
    /// <summary>A session that stops sooner than this counts as a failure of its device.</summary>
    public const long HealthySessionMilliseconds = 5000;

    /// <param name="target">The device to record, resolved only when enabled; null if there's none.</param>
    /// <param name="runningDeviceId">The device a still-running session records, if any.</param>
    /// <param name="failedDeviceId">A device that failed and isn't retried until something changes.</param>
    public static ReplayCaptureStep Decide(bool enabled, string? target, string? runningDeviceId, string? failedDeviceId)
    {
        if (!enabled)
            return ReplayCaptureStep.Disable;
        if (runningDeviceId is not null && runningDeviceId == target)
            return ReplayCaptureStep.Keep;
        if (target is null)
            return ReplayCaptureStep.NoDevice;

        return target == failedDeviceId ? ReplayCaptureStep.Wait : ReplayCaptureStep.Start;
    }

    /// <summary>
    /// Whether a session that stopped by itself after <paramref name="ranMilliseconds"/> marks its device as failed;
    /// one that ran a while (e.g. stopped by a device format change) gets a quiet retry instead.
    /// </summary>
    public static bool StoppedTooSoon(long ranMilliseconds) => ranMilliseconds < HealthySessionMilliseconds;
}
