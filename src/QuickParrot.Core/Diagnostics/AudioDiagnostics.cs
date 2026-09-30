namespace QuickParrot.Core.Diagnostics;

public interface IAudioSetupReader
{
    /// <summary>Should not throw; unreadable parts are left unknown.</summary>
    AudioSetupSnapshot Read();
}

public sealed record DiagnosticsReport(IReadOnlyList<DiagnosticFinding> Findings)
{
    /// <summary>Errors and warnings; advisories aren't counted.</summary>
    public int ProblemCount => Findings.Count(f => f.Severity != DiagnosticSeverity.Advisory);

    /// <summary>A short status-line summary, or null when there's nothing worth interrupting for.</summary>
    public string? StatusLine => ProblemCount switch
    {
        0 => null,
        1 => "Audio setup: 1 problem — see Diagnostics",
        var n => $"Audio setup: {n} problems — see Diagnostics",
    };
}

/// <summary>
/// Runs checks and repairs off the calling thread, one at a time, re-checking after every repair. Device-change
/// requests are debounced so a burst of notifications runs one check.
/// </summary>
public sealed class AudioDiagnostics : IDisposable
{
    public static readonly TimeSpan Debounce = TimeSpan.FromSeconds(1);

    private readonly IAudioSetupReader _reader;
    private readonly IAudioSetupRepairer _repairer;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _timerLock = new();
    private ITimer? _timer;
    private bool _disposed;

    public AudioDiagnostics(IAudioSetupReader reader, IAudioSetupRepairer repairer, TimeProvider time)
    {
        _reader = reader;
        _repairer = repairer;
        _time = time;
    }

    /// <summary>Raised on a background thread after every check.</summary>
    public event Action<DiagnosticsReport>? Updated;

    /// <summary>Checks once things have been quiet for <see cref="Debounce"/>.</summary>
    public void RequestCheck()
    {
        lock (_timerLock)
        {
            if (_disposed)
                return;

            _timer ??= _time.CreateTimer(_ => _ = CheckNowAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _timer.Change(Debounce, Timeout.InfiniteTimeSpan);
        }
    }

    public Task<DiagnosticsReport> CheckNowAsync() => Task.Run(async () =>
    {
        await _gate.WaitAsync();
        try
        {
            return Check();
        }
        finally
        {
            _gate.Release();
        }
    });

    /// <summary>Applies the fix, then re-checks (raising <see cref="Updated"/>) before returning.</summary>
    public Task<RepairResult> RepairAsync(DiagnosticFix fix) => Task.Run(async () =>
    {
        await _gate.WaitAsync();
        try
        {
            var result = await _repairer.RepairAsync(fix);
            Check();
            return result;
        }
        finally
        {
            _gate.Release();
        }
    });

    public void Dispose()
    {
        lock (_timerLock)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }

    private DiagnosticsReport Check()
    {
        AudioSetupSnapshot snapshot;
        try
        {
            snapshot = _reader.Read();
        }
        catch (Exception)
        {
            snapshot = new AudioSetupSnapshot { DevicesReadable = false };
        }

        var report = new DiagnosticsReport(AudioSetupDiagnoser.Diagnose(snapshot));
        try
        {
            Updated?.Invoke(report);
        }
        catch (Exception)
        {
            // A broken handler must not fail the check or the repair that triggered it.
        }

        return report;
    }
}
