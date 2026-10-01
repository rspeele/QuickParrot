using Microsoft.Extensions.Time.Testing;
using QuickParrot.Core.Diagnostics;
using static QuickParrot.Core.Tests.Diagnostics.AudioSetups;

namespace QuickParrot.Core.Tests.Diagnostics;

public class AudioDiagnosticsTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly FakeReader _reader = new();
    private readonly FakeAudioSystemWriter _writer = new();

    private AudioDiagnostics Create() => new(_reader, new AudioSetupRepairer(_writer), _time);

    [Fact]
    public async Task CheckNow_ReportsFindings()
    {
        using var diagnostics = Create();
        DiagnosticsReport? raised = null;
        diagnostics.Updated += report => raised = report;
        _reader.Snapshot = Healthy with { CommunicationsDucking = CommunicationsDucking.MuteOthers };

        var report = await diagnostics.CheckNowAsync();

        Assert.Equal([AudioSetupDiagnoser.Ids.CommunicationsDucking], report.Findings.Ids());
        Assert.Same(report, raised);
    }

    [Fact]
    public async Task ReaderThatThrows_GivesTheUnreadableAdvisory()
    {
        using var diagnostics = Create();
        _reader.Throw = true;

        var report = await diagnostics.CheckNowAsync();

        Assert.Equal([AudioSetupDiagnoser.Ids.DevicesUnreadable], report.Findings.Ids());
    }

    [Fact]
    public async Task RequestCheck_IsDebounced()
    {
        using var diagnostics = Create();
        var checkedOnce = new TaskCompletionSource<DiagnosticsReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        diagnostics.Updated += report => checkedOnce.TrySetResult(report);

        diagnostics.RequestCheck();
        _time.Advance(AudioDiagnostics.Debounce - TimeSpan.FromMilliseconds(100));
        diagnostics.RequestCheck();
        _time.Advance(AudioDiagnostics.Debounce - TimeSpan.FromMilliseconds(100));
        Assert.Equal(0, _reader.Reads);

        _time.Advance(TimeSpan.FromMilliseconds(100));
        await checkedOnce.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, _reader.Reads);
    }

    [Fact]
    public void RequestCheck_AfterDispose_DoesNothing()
    {
        var diagnostics = Create();
        diagnostics.Dispose();

        diagnostics.RequestCheck();
        _time.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(0, _reader.Reads);
    }

    [Fact]
    public async Task CheckInFlight_WhenDisposed_DoesNotRaiseUpdated()
    {
        var diagnostics = Create();
        var raised = false;
        diagnostics.Updated += _ => raised = true;
        using var release = new ManualResetEventSlim();
        _reader.Gate = release;

        var check = diagnostics.CheckNowAsync();
        diagnostics.Dispose();
        release.Set();
        await check.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(raised);
    }

    [Fact]
    public async Task Repair_RechecksBeforeReturning()
    {
        using var diagnostics = Create();
        var reports = new List<DiagnosticsReport>();
        diagnostics.Updated += reports.Add;

        var result = await diagnostics.RepairAsync(new DiagnosticFix(FixKind.DisableCommunicationsDucking, "Off"));

        Assert.True(result.Succeeded);
        Assert.Equal(["ducking:DoNothing"], _writer.Calls);
        Assert.Single(reports);
    }

    [Fact]
    public async Task BrokenHandler_DoesNotFailTheCheck()
    {
        using var diagnostics = Create();
        diagnostics.Updated += _ => throw new InvalidOperationException();

        Assert.Empty((await diagnostics.CheckNowAsync()).Findings);
    }

    private sealed class FakeReader : IAudioSetupReader
    {
        private int _reads;

        public AudioSetupSnapshot Snapshot { get; set; } = Healthy;

        public bool Throw { get; set; }

        public ManualResetEventSlim? Gate { get; set; }

        public int Reads => Volatile.Read(ref _reads);

        public AudioSetupSnapshot Read()
        {
            Interlocked.Increment(ref _reads);
            Gate?.Wait(TimeSpan.FromSeconds(5));
            return Throw ? throw new InvalidOperationException("boom") : Snapshot;
        }
    }
}

public class DiagnosticsReportStatusLineTests
{
    private static readonly DiagnosticFinding Error = new("e", DiagnosticSeverity.Error, "E", "");
    private static readonly DiagnosticFinding Warning = new("w", DiagnosticSeverity.Warning, "W", "");
    private static readonly DiagnosticFinding Advisory = new("a", DiagnosticSeverity.Advisory, "A", "");

    [Fact]
    public void Report_CountsErrorsAndWarningsOnly()
    {
        Assert.Null(new DiagnosticsReport([Advisory]).StatusLine);
        Assert.Equal("Audio setup: 1 problem — see Diagnostics", new DiagnosticsReport([Warning, Advisory]).StatusLine);
        Assert.Equal("Audio setup: 2 problems — see Diagnostics", new DiagnosticsReport([Error, Warning]).StatusLine);
    }
}
