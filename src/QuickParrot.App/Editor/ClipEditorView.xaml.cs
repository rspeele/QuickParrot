using System.Windows;
using System.Windows.Controls;
using MessageBox = System.Windows.MessageBox;
using UserControl = System.Windows.Controls.UserControl;

namespace QuickParrot.App.Editor;

/// <summary>The editor's content, separate from its window so it can also be rendered offscreen.</summary>
public partial class ClipEditorView : UserControl
{
    public ClipEditorView()
    {
        InitializeComponent();
    }

    /// <summary>Raised by Done and Discard; the hosting window closes.</summary>
    public event Action<ClipEditorOutcome>? CloseRequested;

    private ClipEditorViewModel Vm => (ClipEditorViewModel)DataContext;

    private void Wave_SelectionCommitted(object sender, RoutedEventArgs e) => Vm.CommitSelection();

    private void ZoomToSelection_Click(object sender, RoutedEventArgs e) => Wave.ZoomToSelection();

    private void ShowAll_Click(object sender, RoutedEventArgs e) => Wave.ShowAll();

    private void PlaySelection_Click(object sender, RoutedEventArgs e) => _ = Vm.PlaySelectionAsync();

    private void PlayFromCursor_Click(object sender, RoutedEventArgs e) => _ = Vm.PlayFromCursorAsync();

    private void Stop_Click(object sender, RoutedEventArgs e) => Vm.Stop();

    private void SelectAll_Click(object sender, RoutedEventArgs e) => Vm.SelectAll();

    private void SuggestName_Click(object sender, RoutedEventArgs e) => _ = Vm.SuggestNameAsync();

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
