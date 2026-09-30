namespace QuickParrot.Core.Diagnostics;

/// <summary>The one-click setup test: records the cable while playing a chime into it, then plays the recording back.</summary>
public interface ILoopbackTester
{
    /// <summary>
    /// Runs the whole test (about 5 s of recording plus playback). Device problems come back as a
    /// <see cref="LoopbackTestVerdict.Failed"/> result; cancelling stops everything and throws.
    /// </summary>
    /// <param name="monitorRenderId">Where to play the recording back; null (or the cable itself) skips playback.</param>
    /// <param name="progress">Short status lines for the user, e.g. "Say something for a few seconds…".</param>
    Task<LoopbackTestResult> RunAsync(
        string cableRenderId,
        string cableCaptureId,
        string? monitorRenderId,
        IProgress<string>? progress,
        CancellationToken cancellationToken);
}
