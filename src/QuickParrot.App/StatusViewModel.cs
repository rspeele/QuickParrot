using QuickParrot.App.Mvvm;
using QuickParrot.Core.Common;
using QuickParrot.Core.Diagnostics;

namespace QuickParrot.App;

/// <summary>The status line at the bottom of the main window. UI thread only.</summary>
public sealed class StatusViewModel : ObservableObject
{
    private StatusLine _line = StatusLine.Empty;

    public string Display => _line.Display;

    public string Message => _line.Message;

    public void Report(string message) => Set(_line with { Message = message });

    public void ShowDiagnostics(DiagnosticsReport report) => Set(_line with { DiagnosticsSummary = report.StatusLine });

    /// <summary>See <see cref="StatusLine.Restore"/>.</summary>
    public void Restore(string expected, string replacement) => Set(_line.Restore(expected, replacement));

    private void Set(StatusLine line)
    {
        if (line == _line)
            return;

        _line = line;
        OnPropertyChanged(nameof(Display));
    }
}
