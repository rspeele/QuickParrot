using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using QuickParrot.Core.Editing;

namespace QuickParrot.App.Editor;

/// <summary>Hosts <see cref="ClipEditorView"/> and dispatches its keyboard shortcuts. Disposes the view model on close.</summary>
public partial class ClipEditorWindow : Window, IClipEditorWindow
{
    private readonly ClipEditorViewModel _viewModel;
    private readonly TaskCompletionSource<ClipEditorOutcome> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ClipEditorOutcome _closingAs = ClipEditorOutcome.Done;

    public ClipEditorWindow(ClipEditorViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    Task<ClipEditorOutcome> IClipEditorWindow.Closed => _closed.Task;

    public static IClipEditorWindow Open(ClipEditorViewModel viewModel, Window? owner)
    {
        var window = new ClipEditorWindow(viewModel) { Owner = owner };
        window.Show();
        return window;
    }

    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Activate();
    }

    private void Editor_CloseRequested(ClipEditorOutcome outcome)
    {
        _closingAs = outcome;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var focused = Keyboard.FocusedElement;
        var shortcut = EditorShortcuts.Map(
            ToEditorKey(e.Key), (EditorModifiers)Keyboard.Modifiers, focused is TextBoxBase, e.IsRepeat,
            focused is ComboBox { IsDropDownOpen: true });
        if (shortcut == EditorShortcut.None)
            return;

        e.Handled = true;
        switch (shortcut)
        {
            case EditorShortcut.TogglePlay:
                _ = _viewModel.TogglePlayAsync();
                break;
            case EditorShortcut.Save:
                _ = _viewModel.SaveAsync();
                break;
            case EditorShortcut.Close:
                Close();
                break;
            case EditorShortcut.SelectAll:
                _viewModel.SelectAll();
                break;
            case EditorShortcut.SetSelectionStart:
                _viewModel.SetSelectionEdgeAtPlayheadOrCursor(isStart: true);
                break;
            case EditorShortcut.SetSelectionEnd:
                _viewModel.SetSelectionEdgeAtPlayheadOrCursor(isStart: false);
                break;
            case EditorShortcut.PlayAndFocusName:
                _ = _viewModel.PlaySelectionAsync();
                Editor.FocusName();
                break;
            case EditorShortcut.SuggestName:
                _ = _viewModel.SuggestNameAsync();
                break;
            case EditorShortcut.Undo:
                _viewModel.Undo();
                break;
            case EditorShortcut.Redo:
                _viewModel.Redo();
                break;
        }
    }

    private static EditorKey ToEditorKey(Key key) => key switch
    {
        Key.Space => EditorKey.Space,
        Key.Enter => EditorKey.Enter,
        Key.Escape => EditorKey.Escape,
        Key.OemOpenBrackets => EditorKey.OpenBracket,
        Key.OemCloseBrackets => EditorKey.CloseBracket,
        Key.A => EditorKey.A,
        Key.E => EditorKey.E,
        Key.S => EditorKey.S,
        Key.Y => EditorKey.Y,
        Key.Z => EditorKey.Z,
        _ => EditorKey.Other,
    };

    private void Window_Closed(object? sender, EventArgs e)
    {
        try
        {
            _viewModel.Dispose();
        }
        finally
        {
            _closed.TrySetResult(_closingAs);
        }
    }
}
