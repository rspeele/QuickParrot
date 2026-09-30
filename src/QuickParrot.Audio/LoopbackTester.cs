using System.Diagnostics;
using QuickParrot.Core.Diagnostics;

namespace QuickParrot.Audio;

/// <summary>
/// Records CABLE Output (what the game hears) while playing the test chime into CABLE Input and the user speaks, then
/// plays the recording back on the monitor device and analyzes it.
/// </summary>
public sealed class LoopbackTester : ILoopbackTester
{
    public const string SpeakPrompt = "Say something for a few seconds…";

    private const int ChimeSampleRate = 48000;
    private static readonly TimeSpan RecordingLength = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ChimeDelay = TimeSpan.FromSeconds(0.4);
    private static readonly TimeSpan PlaybackSlack = TimeSpan.FromSeconds(3);

    private readonly LoopbackTestSignal _signal = LoopbackTestSignal.Default;

    public Task<LoopbackTestResult> RunAsync(
        string cableRenderId,
        string cableCaptureId,
        string? monitorRenderId,
        IProgress<string>? progress,
        CancellationToken cancellationToken) =>
        Task.Run(
            async () =>
            {
                try
                {
                    return await RunCoreAsync(cableRenderId, cableCaptureId, monitorRenderId, progress, cancellationToken);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    return LoopbackTestResult.Failed($"The test failed: {e.Message}");
                }
            },
            cancellationToken);

    private async Task<LoopbackTestResult> RunCoreAsync(
        string cableRenderId, string cableCaptureId, string? monitorRenderId, IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(SpeakPrompt);
        var (samples, sampleRate, failure) = await RecordAsync(cableRenderId, cableCaptureId, cancellationToken);
        if (failure is not null)
            return failure;

        progress?.Report("Checking the recording…");
        var result = LoopbackTestAnalyzer.Analyze(samples, sampleRate, _signal);

        if (monitorRenderId is null || string.Equals(monitorRenderId, cableRenderId, StringComparison.OrdinalIgnoreCase))
            return result with { PlaybackError = "no headphones or speakers are selected apart from the cable." };

        progress?.Report("Playing back what the game hears…");
        var playbackError = await PlayBackAsync(monitorRenderId, samples, sampleRate, cancellationToken);
        return playbackError is null ? result : result with { PlaybackError = playbackError };
    }

    private async Task<(float[] Samples, int SampleRate, LoopbackTestResult? Failure)> RecordAsync(
        string cableRenderId, string cableCaptureId, CancellationToken cancellationToken)
    {
        LoopbackTesterCapture capture;
        try
        {
            capture = LoopbackTesterCapture.Start(cableCaptureId, RecordingLength + TimeSpan.FromSeconds(1));
        }
        catch (Exception e)
        {
            return ([], 0, LoopbackTestResult.Failed($"Couldn't record from CABLE Output: {e.Message}"));
        }

        using (capture)
        {
            var clock = Stopwatch.StartNew();
            await Task.Delay(ChimeDelay, cancellationToken);

            LoopbackTesterPlayback chime;
            try
            {
                chime = LoopbackTesterPlayback.Open(cableRenderId, _signal.Render(ChimeSampleRate), ChimeSampleRate);
            }
            catch (Exception e)
            {
                return ([], 0, LoopbackTestResult.Failed($"Couldn't play the test sound into CABLE Input: {e.Message}"));
            }

            await using (chime)
            {
                chime.Play();
                var remaining = RecordingLength - clock.Elapsed;
                if (remaining > TimeSpan.Zero)
                    await Task.Delay(remaining, cancellationToken);

                if (chime.Stopped is { IsCompleted: true, Result: { } chimeError })
                {
                    return ([], 0,
                        LoopbackTestResult.Failed($"Playing the test sound into CABLE Input failed: {chimeError.Message}"));
                }
            }

            var (samples, sampleRate, error) = await capture.StopAsync();
            if (error is not null)
                return ([], 0, LoopbackTestResult.Failed($"Recording from CABLE Output failed: {error}"));
            if (sampleRate == 0 || samples.Length < LoopbackTestAnalyzer.MinRecordingSeconds * sampleRate)
            {
                return ([], 0, LoopbackTestResult.Failed(
                    "CABLE Output didn't deliver any audio. Check that it's enabled and not in use by another app in "
                    + "exclusive mode."));
            }

            return (samples, sampleRate, null);
        }
    }

    private static async Task<string?> PlayBackAsync(
        string monitorRenderId, float[] samples, int sampleRate, CancellationToken cancellationToken)
    {
        LoopbackTesterPlayback playback;
        try
        {
            playback = LoopbackTesterPlayback.Open(monitorRenderId, samples, sampleRate);
        }
        catch (Exception e)
        {
            return e.Message;
        }

        await using (playback)
        {
            playback.Play();
            try
            {
                return (await playback.Stopped.WaitAsync(playback.Duration + PlaybackSlack, cancellationToken))?.Message;
            }
            catch (TimeoutException)
            {
                return null;
            }
        }
    }
}
