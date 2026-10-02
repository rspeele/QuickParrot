using QuickParrot.App.Mvvm;
using QuickParrot.Core.Diagnostics;

namespace QuickParrot.App;

public sealed record FindingItem(DiagnosticFinding Finding)
{
    public DiagnosticSeverity Severity => Finding.Severity;

    public string Title => Finding.Title;

    public string Explanation => Finding.Explanation;

    public bool HasFix => Finding.Fix is not null;

    public string FixLabel => Finding.Fix?.Label ?? "";
}

/// <summary>The Diagnostics tab. Checks and repairs run in the background; results arrive on the UI thread.</summary>
public sealed class DiagnosticsViewModel : ObservableObject
{
    private readonly AudioDiagnostics _diagnostics;
    private IReadOnlyList<FindingItem> _findings = [];
    private DiagnosticsReport? _report;
    private bool _isBusy;
    private string _repairMessage = "";

    public DiagnosticsViewModel(
        AudioDiagnostics diagnostics, LoopbackTestViewModel test, ClipCableTestViewModel clipTest, Action<Action> postToUi)
    {
        _diagnostics = diagnostics;
        _diagnostics.Updated += report => postToUi(() => Apply(report));
        Test = test;
        ClipTest = clipTest;
    }

    public LoopbackTestViewModel Test { get; }

    public ClipCableTestViewModel ClipTest { get; }

    /// <summary>Raised on the UI thread with every new report.</summary>
    public event Action<DiagnosticsReport>? ReportChanged;

    public IReadOnlyList<FindingItem> Findings
    {
        get => _findings;
        private set => SetField(ref _findings, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
                OnPropertyChanged(nameof(IsIdle));
        }
    }

    public bool IsIdle => !IsBusy;

    public bool IsAllGood => _report is { Findings.Count: 0 };

    public string Summary => _report switch
    {
        null => "Checking your audio setup…",
        { Findings.Count: 0 } => "",
        { ProblemCount: 0 } => "No problems found, just some notes.",
        { ProblemCount: 1 } => "1 problem found.",
        var report => $"{report.ProblemCount} problems found.",
    };

    public string RepairMessage
    {
        get => _repairMessage;
        private set
        {
            if (SetField(ref _repairMessage, value))
                OnPropertyChanged(nameof(HasRepairMessage));
        }
    }

    public bool HasRepairMessage => _repairMessage.Length > 0;

    public void RequestCheck() => _diagnostics.RequestCheck();

    public async Task RecheckAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        RepairMessage = "";
        try
        {
            await _diagnostics.CheckNowAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>The outcome is shown in <see cref="RepairMessage"/>.</summary>
    public async Task FixAsync(FindingItem? item)
    {
        if (IsBusy || item?.Finding.Fix is not { } fix)
            return;

        IsBusy = true;
        RepairMessage = "Fixing…";
        try
        {
            var result = await _diagnostics.RepairAsync(fix);
            RepairMessage = result.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Apply(DiagnosticsReport report)
    {
        _report = report;
        Findings = report.Findings.Select(f => new FindingItem(f)).ToList();
        OnPropertyChanged(nameof(IsAllGood));
        OnPropertyChanged(nameof(Summary));
        ReportChanged?.Invoke(report);
    }
}
