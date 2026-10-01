using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using ComboBox = System.Windows.Controls.ComboBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;

namespace QuickParrot.App.Editor;

/// <summary>
/// Hosts <see cref="ClipEditorView"/>. Keys: Space plays/stops (except while typing a name), Enter saves, Esc closes,
/// Ctrl+A selects all audio. Disposes the view model on close; read <see cref="Outcome"/> afterwards.
/// </summary>
public partial class ClipEditorWindow : Window
{
    private readonly ClipEditorViewModel _viewModel;

    public ClipEditorWindow(ClipEditorViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    public ClipEditorOutcome Outcome => _viewModel.Outcome;

    private void Editor_CloseRequested(ClipEditorOutcome outcome)
    {
        _viewModel.Outcome = outcome;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var typing = Keyboard.FocusedElement is TextBoxBase;
        switch (e.Key)
        {
            case Key.Space or Key.Enter when e.IsRepeat:
                e.Handled = !typing || e.Key == Key.Enter; // holding a key mustn't toggle play or save the clip again and again
                break;
            case Key.Space when !typing && Keyboard.Modifiers == ModifierKeys.None:
                _ = _viewModel.TogglePlayAsync();
                e.Handled = true;
                break;
            case Key.Enter when Keyboard.FocusedElement is not ComboBox { IsDropDownOpen: true }:
                _ = _viewModel.SaveAsync();
                e.Handled = true;
                break;
            case Key.Escape when Keyboard.FocusedElement is not ComboBox { IsDropDownOpen: true }:
                Close();
                e.Handled = true;
                break;
            case Key.A when !typing && Keyboard.Modifiers == ModifierKeys.Control:
                _viewModel.SelectAll();
                e.Handled = true;
                break;
        }
    }

    private void Window_Closed(object? sender, EventArgs e) => _viewModel.Dispose();
}
