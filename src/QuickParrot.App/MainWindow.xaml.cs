using System.Windows;
using System.Windows.Input;

namespace QuickParrot.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

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
            _viewModel.ChooseLibrary(dialog.FolderName);
    }

    private void Up_Click(object sender, RoutedEventArgs e) => _viewModel.GoUp();

    private void Stop_Click(object sender, RoutedEventArgs e) => _viewModel.Stop();

    private void Refresh_Click(object sender, RoutedEventArgs e) => _viewModel.RefreshDevices();

    private void Entries_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        _viewModel.Open(EntryList.SelectedItem as LibraryItem);

    private void Entries_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            _viewModel.Open(EntryList.SelectedItem as LibraryItem);
        else if (e.Key == Key.Back)
            _viewModel.GoUp();
    }
}
