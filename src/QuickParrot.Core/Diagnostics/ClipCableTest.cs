namespace QuickParrot.Core.Diagnostics;

public sealed record ClipCableTestRequest(
    string ClipName, string ClipFullPath, string CableCaptureId, TimeSpan PreRoll, TimeSpan PostRoll);

/// <summary>What the clip test recorded; <see cref="Error"/> is set instead when it couldn't run.</summary>
public sealed record ClipCableTestResult(
    string? Error = null, MonoAudio? Recording = null, ClipLevelAnalysis? Analysis = null, string? PlaybackError = null)
{
    public static ClipCableTestResult Failed(string message) => new(Error: message);

    /// <summary>The result as lines for the user.</summary>
    public IReadOnlyList<string> Describe()
    {
        if (Error is not null)
            return [Error];

        var lines = new List<string>();
        if (Analysis is not null)
        {
            lines.Add(Analysis.Summary);
            if (Analysis.GainNote is { } gain)
                lines.Add(gain);
        }

        if (PlaybackError is not null)
            lines.Add($"Couldn't play the recording back: {PlaybackError}");

        return lines;
    }
}

/// <summary>
/// Records the cable while a library clip plays through the real playback path (started and stopped by the caller's
/// delegates), compares the recording's level with the clip's, and plays recordings back on the monitor device.
/// </summary>
public sealed class ClipCableTest(IClipCableAudio audio, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    public static readonly TimeSpan Lead = TimeSpan.FromSeconds(0.3);
    public static readonly TimeSpan Slack = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaxRecording = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan OnsetMargin = TimeSpan.FromSeconds(0.1);

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    /// <summary>How long to record for a clip: lead, pre-roll, clip, post-roll and slack, capped at <see cref="MaxRecording"/>.</summary>
    public static TimeSpan RecordingLength(TimeSpan clip, TimeSpan preRoll, TimeSpan postRoll)
    {
        var length = Lead + preRoll + clip + postRoll + Slack;
        return length < MaxRecording ? length : MaxRecording;
    }

    /// <summary>Where in the recording the clip could first appear.</summary>
    public static TimeSpan SearchFrom(TimeSpan preRoll)
    {
        var from = Lead + preRoll - OnsetMargin;
        return from > TimeSpan.Zero ? from : TimeSpan.Zero;
    }

    /// <summary>
    /// Records and analyzes. <paramref name="play"/> starts the clip for real; <paramref name="stop"/> stops it and
    /// releases push-to-talk and the mic, and is always called once play was. Cancelling stops everything and throws.
    /// </summary>
    public async Task<ClipCableTestResult> RunAsync(
        ClipCableTestRequest request, Action play, Action stop, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        try
        {
            return await RunCoreAsync(request, play, stop, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return ClipCableTestResult.Failed($"The test failed: {e.Message}");
        }
    }

    /// <summary>Plays a recording on the monitor device; returns why it couldn't, or null. Cancelling throws.</summary>
    public async Task<string?> PlayBackAsync(string? monitorRenderId, MonoAudio recording, CancellationToken cancellationToken)
    {
        if (monitorRenderId is null)
            return CableTestMessages.NoMonitor;

        try
        {
            return await audio.PlayAsync(monitorRenderId, recording, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return e.Message;
        }
    }

    private async Task<ClipCableTestResult> RunCoreAsync(
        ClipCableTestRequest request, Action play, Action stop, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        progress?.Report("Loading the clip…");
        MonoAudio clip;
        try
        {
            clip = await audio.LoadClipAsync(request.ClipFullPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return ClipCableTestResult.Failed($"Couldn't read {request.ClipName}: {e.Message}");
        }

        var length = RecordingLength(clip.Duration, request.PreRoll, request.PostRoll);
        ICableRecording recording;
        try
        {
            recording = audio.StartRecording(request.CableCaptureId, length + TimeSpan.FromSeconds(1));
        }
        catch (Exception e)
        {
            return ClipCableTestResult.Failed($"Couldn't record from CABLE Output: {e.Message}");
        }

        MonoAudio recorded;
        using (recording)
        {
            var played = false;
            try
            {
                await _delay(Lead, cancellationToken).ConfigureAwait(false);
                progress?.Report($"Playing {request.ClipName} and recording what others hear…");
                play();
                played = true;
                await _delay(length - Lead, cancellationToken).ConfigureAwait(false);
                recorded = await recording.StopAsync().ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                return ClipCableTestResult.Failed($"Recording from CABLE Output failed: {e.Message}");
            }
            finally
            {
                // After the recording stops, so cutting off a capped clip doesn't read as a drop.
                if (played)
                    stop();
            }
        }

        if (recorded.SampleRate <= 0 || recorded.Duration < Lead)
            return ClipCableTestResult.Failed(CableTestMessages.NoAudioFromCable);

        progress?.Report("Checking the recording…");
        var analysis = ClipLevelAnalyzer.Analyze(clip, recorded, SearchFrom(request.PreRoll));
        return new ClipCableTestResult(Recording: recorded, Analysis: analysis);
    }
}
