using QuickParrot.App.Mvvm;
using QuickParrot.Core.Devices;
using QuickParrot.Core.Diagnostics;

namespace QuickParrot.App;

/// <summary>The Diagnostics tab's one-click setup test: resolves the cable devices, runs <see cref="ILoopbackTester"/>,
/// and shows the result. Device-ID resolution itself lives in <see cref="LoopbackTestSetup"/> so it's unit-testable.</summary>
public sealed class LoopbackTestViewModel : ObservableObject
{
    public const string Note =
        "Plays a short chime into CABLE Input while recording what your game hears (~5 s), then plays the recording "
        + "back to you. If you're in voice chat, others will hear the chime and you.";

    private readonly ILoopbackTester _tester;
    private readonly CableTestRunner _runner;
    private readonly AudioDiagnostics _diagnostics;
    private bool _isRunning;
    private string _progress = "";
    private LoopbackTestResult? _result;
    private LoopbackTestReport? _report;

    public LoopbackTestViewModel(ILoopbackTester tester, CableTestRunner runner, AudioDiagnostics diagnostics)
    {
        _tester = tester;
        _runner = runner;
        _diagnostics = diagnostics;
        _runner.Changed += () => OnPropertyChanged(nameof(CanRun));
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetField(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(CanRun));
                OnPropertyChanged(nameof(ButtonLabel));
            }
        }
    }

    public bool CanRun => IsRunning || (_runner.IsSetupReady && !_runner.IsBusy);

    public string ButtonLabel => IsRunning ? "Cancel" : "Run test";

    public string Progress
    {
        get => _progress;
        private set => SetField(ref _progress, value);
    }

    public LoopbackTestResult? Result
    {
        get => _result;
        private set
        {
            if (SetField(ref _result, value))
            {
                OnPropertyChanged(nameof(HasResult));
                Report = value is null ? null : LoopbackTestAdvice.Describe(value);
            }
        }
    }

    public bool HasResult => _result is not null;

    /// <summary>Plain-language wording of <see cref="Result"/>, for binding; null until a test has run.</summary>
    public LoopbackTestReport? Report
    {
        get => _report;
        private set => SetField(ref _report, value);
    }

    /// <summary>Starts the test, or cancels one already running.</summary>
    public async Task RunOrCancelAsync()
    {
        if (IsRunning)
        {
            _runner.Cancel();
            return;
        }

        // Stops a playing clip polluting the recording, and a chord played mid-test starting one.
        if (_runner.TryStart(recordsCable: true) is not { } run)
            return;

        using (run)
        {
            Result = null;
            Progress = "";
            IsRunning = true;
            var progress = new Progress<string>(text => Progress = text); // must be built on the UI thread to post back here
            try
            {
                var setup = run.Setup;
                Result = await _tester.RunAsync(
                    setup.CableRenderId!, setup.CableCaptureId!, setup.MonitorRenderId, progress, run.Token);
            }
            catch (OperationCanceledException)
            {
                Progress = "Test cancelled";
            }
            catch (Exception e)
            {
                Result = LoopbackTestResult.Failed($"The test failed: {e.Message}");
            }
            finally
            {
                IsRunning = false;
                _diagnostics.RequestCheck();
            }
        }
    }
}
