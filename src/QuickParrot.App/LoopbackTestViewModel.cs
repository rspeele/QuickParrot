using QuickParrot.Core.Devices;
using QuickParrot.Core.Diagnostics;
using QuickParrot.Core.Engine;

namespace QuickParrot.App;

/// <summary>The Diagnostics tab's one-click setup test: resolves the cable devices, runs <see cref="ILoopbackTester"/>,
/// and shows the result. Device-ID resolution itself lives in <see cref="LoopbackTestSetup"/> so it's unit-testable.</summary>
public sealed class LoopbackTestViewModel : ObservableObject
{
    public const string Note =
        "Plays a short chime into CABLE Input while recording what your game hears (~5 s), then plays the recording "
        + "back to you. If you're in voice chat, others will hear the chime and you.";

    private readonly ILoopbackTester _tester;
    private readonly IAudioDeviceCatalog _devices;
    private readonly ICaptureDeviceCatalog _captureDevices;
    private readonly QuickParrotEngine _engine;
    private readonly DiagnosticsViewModel _diagnostics;
    private CancellationTokenSource? _cts;
    private bool _isRunning;
    private string _progress = "";
    private LoopbackTestResult? _result;

    public LoopbackTestViewModel(
        ILoopbackTester tester,
        IAudioDeviceCatalog devices,
        ICaptureDeviceCatalog captureDevices,
        QuickParrotEngine engine,
        DiagnosticsViewModel diagnostics)
    {
        _tester = tester;
        _devices = devices;
        _captureDevices = captureDevices;
        _engine = engine;
        _diagnostics = diagnostics;

        // The setup test's readiness depends on the same devices and settings diagnostics already watches.
        _diagnostics.ReportChanged += _ => OnPropertyChanged(nameof(CanRun));
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

    public bool CanRun => IsRunning || ResolveSetup().IsReady;

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
                OnPropertyChanged(nameof(HasResult));
        }
    }

    public bool HasResult => _result is not null;

    /// <summary>Starts the test, or cancels one already running.</summary>
    public async Task RunOrCancelAsync()
    {
        if (IsRunning)
        {
            _cts?.Cancel();
            return;
        }

        var setup = ResolveSetup();
        if (!setup.IsReady)
            return;

        _engine.Stop(); // don't let a playing clip pollute the recording
        _engine.SuppressPlayback(true); // ...or a chord played mid-test start playing into it
        Result = null;
        Progress = "";
        IsRunning = true;

        var cts = new CancellationTokenSource();
        _cts = cts;
        var progress = new Progress<string>(text => Progress = text); // must be built on the UI thread to post back here
        try
        {
            Result = await _tester.RunAsync(setup.CableRenderId!, setup.CableCaptureId!, setup.MonitorRenderId, progress, cts.Token);
        }
        catch (OperationCanceledException)
        {
            Progress = "Test cancelled";
        }
        finally
        {
            _engine.SuppressPlayback(false);
            if (_cts == cts)
                _cts = null;
            cts.Dispose();
            IsRunning = false;
            _diagnostics.RequestCheck();
        }
    }

    private LoopbackTestSetup ResolveSetup() => LoopbackTestSetup.Resolve(
        _devices.GetRenderDevices(), _captureDevices.GetCaptureDevices(),
        _engine.Settings.CableDeviceId, _engine.Settings.MonitorDeviceId, _devices.GetDefaultRenderDeviceId());
}
