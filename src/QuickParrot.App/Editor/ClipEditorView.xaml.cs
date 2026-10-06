using System.Windows;
using System.Windows.Controls;

namespace QuickParrot.App.Editor;

/// <summary>The editor's content, separate from its window so it can also be rendered offscreen.</summary>
public partial class ClipEditorView : UserControl
{
    public ClipEditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>Raised by Done and Discard; the hosting window closes.</summary>
    public event Action<ClipEditorOutcome>? CloseRequested;

    private ClipEditorViewModel Vm => (ClipEditorViewModel)DataContext;

    /// <summary>Ctrl+S: focuses the name box and selects any text in it, so typing a name replaces it.</summary>
    public void FocusName()
    {
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ClipEditorViewModel old)
            old.FolderBrowseRequested -= OnFolderBrowseRequested;
        if (e.NewValue is ClipEditorViewModel vm)
            vm.FolderBrowseRequested += OnFolderBrowseRequested;
    }

    private async void OnFolderBrowseRequested(string initialDirectory)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = initialDirectory };
        await Vm.ApplyBrowsedFolderAsync(dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FolderName : null);
    }

    private void Wave_SelectionCommitted(object sender, RoutedEventArgs e)
    {
        var args = (SelectionCommittedEventArgs)e;
        Vm.CommitSelection(args.Target, args.AnchorFrame);
    }

    private void ZoomToSelection_Click(object sender, RoutedEventArgs e) => Wave.ZoomToSelection();

    private void ShowAll_Click(object sender, RoutedEventArgs e) => Wave.ShowAll();

    private void PlaySelection_Click(object sender, RoutedEventArgs e) => _ = Vm.PlaySelectionAsync();

    private void PlayFromCursor_Click(object sender, RoutedEventArgs e) => _ = Vm.PlayFromCursorAsync();

    private void PlayEnd_Click(object sender, RoutedEventArgs e) => _ = Vm.PlayEndAsync();

    private void Stop_Click(object sender, RoutedEventArgs e) => Vm.Stop();

    private void SelectAll_Click(object sender, RoutedEventArgs e) => Vm.SelectAll();

    private void Save_Click(object sender, RoutedEventArgs e) => _ = Vm.SaveAsync();

    private void Done_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(ClipEditorOutcome.Done);

    private void Discard_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(Window.GetWindow(this)!, "Throw this capture away? Clips you've already saved are kept.",
            "Discard capture", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        if (answer == MessageBoxResult.OK)
            CloseRequested?.Invoke(ClipEditorOutcome.Discarded);
    }
}
