using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace QuickParrot.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _apiKeyDirty;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void ChooseLibrary_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose your sound library folder" };
        if (dialog.ShowDialog(this) == true)
            _viewModel.Library.Choose(dialog.FolderName);
    }

    private void Up_Click(object sender, RoutedEventArgs e) => _viewModel.Library.GoUp();

    private void OpenInExplorer_Click(object sender, RoutedEventArgs e) => _viewModel.Library.OpenCurrentFolderInExplorer();

    private void Stop_Click(object sender, RoutedEventArgs e) => _viewModel.Library.Stop();

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Devices.Refresh();
        _viewModel.Library.Refresh();
    }

    private void Entries_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        _viewModel.Library.Open(EntryList.SelectedItem as LibraryItem);

    private void Entries_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            _viewModel.Library.Open(EntryList.SelectedItem as LibraryItem);
        else if (e.Key == Key.Back)
            _viewModel.Library.GoUp();
    }

    // Only files can be favorites, so the menu doesn't open on folders or empty space.
    private void Entries_ContextMenuOpening(object sender, ContextMenuEventArgs e) =>
        e.Handled = ItemsControl.ContainerFromElement(EntryList, e.OriginalSource as DependencyObject)
            is not ListBoxItem { IsSelected: true, DataContext: LibraryItem { Entry.IsFolder: false } };

    private void AssignFavorite_Click(object sender, RoutedEventArgs e) =>
        _viewModel.Favorites.Assign((sender as FrameworkElement)?.DataContext as FavoriteRow, (EntryList.SelectedItem as LibraryItem)?.Entry);

    private void PlayFavorite_Click(object sender, RoutedEventArgs e) =>
        _viewModel.Favorites.Play((sender as FrameworkElement)?.DataContext as FavoriteRow);

    private void ClearFavorite_Click(object sender, RoutedEventArgs e) =>
        _viewModel.Favorites.Clear((sender as FrameworkElement)?.DataContext as FavoriteRow);

    private void GrabNow_Click(object sender, RoutedEventArgs e) => _viewModel.PendingGrabs.GrabNow();

    private async void OpenGrab_Click(object sender, RoutedEventArgs e) => await OpenSelectedGrabAsync();

    private void DeleteGrab_Click(object sender, RoutedEventArgs e) =>
        _viewModel.PendingGrabs.Delete(PendingGrabsList.SelectedItem as PendingGrabItem);

    private async void PendingGrabs_MouseDoubleClick(object sender, MouseButtonEventArgs e) => await OpenSelectedGrabAsync();

    private Task OpenSelectedGrabAsync() =>
        PendingGrabsList.SelectedItem is PendingGrabItem item ? _viewModel.Editors.EditAsync(item.Grab) : Task.CompletedTask;

    // Committing on every keystroke would DPAPI-encrypt and save a partial key after each one; commit once instead,
    // on LostFocus or Enter.
    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e) => _apiKeyDirty = true;

    private void ApiKeyBox_LostFocus(object sender, RoutedEventArgs e) => CommitApiKeyIfDirty();

    private void ApiKeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        CommitApiKeyIfDirty();
        e.Handled = true;
    }

    private void ClearApiKey_Click(object sender, RoutedEventArgs e)
    {
        _apiKeyDirty = false;
        ApiKeyBox.Clear();
        _viewModel.LiteLlm.SetApiKey("");
    }

    private void CommitApiKeyIfDirty()
    {
        if (!_apiKeyDirty)
            return;

        _apiKeyDirty = false;
        _viewModel.LiteLlm.SetApiKey(ApiKeyBox.Password);
    }

    private async void TestLiteLlmConnection_Click(object sender, RoutedEventArgs e) => await _viewModel.LiteLlm.TestConnectionAsync();

    private async void ChangeChordKey_Click(object sender, RoutedEventArgs e) => await _viewModel.Hotkeys.ChangeChordKeyAsync();

    private async void ChangeSaveNavigationKey_Click(object sender, RoutedEventArgs e) => await _viewModel.Hotkeys.ChangeSaveNavigationKeyAsync();

    private async void ChangeSearchKey_Click(object sender, RoutedEventArgs e) => await _viewModel.Hotkeys.ChangeSearchKeyAsync();

    private async void ChangeFragmentsKey_Click(object sender, RoutedEventArgs e) => await _viewModel.Hotkeys.ChangeFragmentsKeyAsync();

    private async void ChangePushToTalkKey_Click(object sender, RoutedEventArgs e) => await _viewModel.Hotkeys.ChangePushToTalkKeyAsync();

    private async void Recheck_Click(object sender, RoutedEventArgs e) => await _viewModel.Diagnostics.RecheckAsync();

    private async void Fix_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.Diagnostics.FixAsync((sender as FrameworkElement)?.DataContext as FindingItem);

    private async void RunLoopbackTest_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.Diagnostics.Test.RunOrCancelAsync();

    private async void ClipPicker_DropDownOpened(object? sender, EventArgs e) =>
        await _viewModel.Diagnostics.ClipTest.LoadClipsIfStaleAsync();

    private async void RunClipTest_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.Diagnostics.ClipTest.RunOrCancelAsync();

    private async void ReplayClipTest_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.Diagnostics.ClipTest.ReplayOrStopAsync();

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e) =>
        _viewModel.Hotkeys.CancelKeyCapture();

    // Otherwise a capture left armed would eat the next key pressed in the game and rebind the chord to it.
    private void MainWindow_Deactivated(object? sender, EventArgs e) => _viewModel.Hotkeys.CancelKeyCapture();

    // Mute/volume changes don't raise SetupChanged, so a fix made in Windows itself only clears promptly here.
    private void MainWindow_Activated(object? sender, EventArgs e) => _viewModel.Diagnostics.RequestCheck();

    private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // SelectionChanged bubbles up from combo boxes elsewhere in the window, so only react to the tab itself.
        if (ReferenceEquals(e.OriginalSource, MainTabControl) && ReferenceEquals(MainTabControl.SelectedItem, DiagnosticsTab))
            _viewModel.Diagnostics.RequestCheck();
    }
}
