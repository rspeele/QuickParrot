using QuickParrot.App.Mvvm;
using QuickParrot.Core.Engine;
using QuickParrot.Core.Library;
using QuickParrot.Core.Navigation;
using QuickParrot.Core.Settings;

namespace QuickParrot.App;

public sealed record LibraryItem(FolderEntry Entry)
{
    public string Display => Entry.IsFolder ? $"[{Entry.Name}]" : Entry.Name;
}

/// <summary>The Library tab's folder browser over the library root in settings, refreshed live as files change.</summary>
public sealed class LibraryViewModel : ObservableObject, IDisposable
{
    private readonly SettingsMirror _settings;
    private readonly QuickParrotEngine _engine;
    private readonly StatusViewModel _status;
    private readonly Func<string, IFolderSource> _openSource;
    private readonly Func<string, Action, IDisposable> _watch;
    private readonly Action<string> _openFolder;
    private readonly Action<Action> _postToUi;
    private OpenedLibrary? _library;
    private IDisposable? _watcher;
    private IReadOnlyList<LibraryItem> _entries = [];

    /// <param name="watch">Starts watching a root, calling back (on any thread) after its contents change.</param>
    /// <param name="openFolder">Shows a folder in Explorer; may throw <see cref="System.ComponentModel.Win32Exception"/>.</param>
    public LibraryViewModel(
        SettingsMirror settings,
        QuickParrotEngine engine,
        StatusViewModel status,
        Func<string, IFolderSource> openSource,
        Func<string, Action, IDisposable> watch,
        Action<string> openFolder,
        Action<Action> postToUi)
    {
        _settings = settings;
        _engine = engine;
        _status = status;
        _openSource = openSource;
        _watch = watch;
        _openFolder = openFolder;
        _postToUi = postToUi;
        _settings.Changed += (_, now) =>
        {
            if (NullIfEmpty(now.LibraryRoot) != _library?.Root)
                OpenLibrary(now.LibraryRoot);
        };
        OpenLibrary(settings.Current.LibraryRoot);
    }

    /// <summary>Raised on the UI thread after files changed on disk and the listing has been refreshed.</summary>
    public event Action? ContentsChanged;

    /// <summary>Null with no library chosen.</summary>
    public string? Root => _library?.Root;

    /// <summary>The relative path of the folder being shown.</summary>
    public string Folder => _library?.View.Path ?? "";

    public string RootDisplay => _library?.Root ?? "(no sound library chosen)";

    public string CurrentPath => _library is null ? "" : "/" + _library.View.Path;

    public bool CanGoUp => _library?.View.CanGoUp == true;

    public bool CanOpenInExplorer => _library is not null;

    public IReadOnlyList<LibraryItem> Entries
    {
        get => _entries;
        private set => SetField(ref _entries, value);
    }

    public string Warning => OverlayCapacity.TruncationWarning(_library?.View.Entries.Count ?? 0) ?? "";

    public bool HasWarning => Warning.Length > 0;

    public void Choose(string root) => _settings.Update(s => s.WithLibraryRoot(root));

    /// <summary>Enters a folder, or plays a clip.</summary>
    public void Open(LibraryItem? item)
    {
        if (_library is not { } library || item is null)
            return;

        if (item.Entry.IsFolder)
        {
            Show(library with { View = library.View.Open(library.Source, item.Entry) });
            return;
        }

        _engine.Play(item.Entry.RelativePath);
        _status.Report($"Playing {item.Entry.Name}");
    }

    public void GoUp()
    {
        if (_library is { } library)
            Show(library with { View = library.View.GoUp(library.Source) });
    }

    /// <summary>Re-reads the folder shown, falling back to the nearest ancestor that still exists.</summary>
    public void Refresh()
    {
        if (_library is { } library)
            Show(library with { View = library.View.Refresh(library.Source) });
    }

    public void Stop()
    {
        _engine.Stop();
        _status.Report("Stopped");
    }

    public void OpenCurrentFolderInExplorer()
    {
        if (_library is not { } library)
            return;

        if (library.Source.GetEntries(library.View.Path) is null)
        {
            Refresh();
            _status.Report("That folder no longer exists.");
            return;
        }

        try
        {
            _openFolder(LibraryPathResolver.FullPath(library.Root, library.View.Path));
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _status.Report($"Couldn't open the folder: {e.Message}");
        }
    }

    public void Dispose() => _watcher?.Dispose();

    private void OpenLibrary(string? root)
    {
        _watcher?.Dispose();
        _watcher = null;
        if (string.IsNullOrEmpty(root))
        {
            Show(null);
            return;
        }

        var source = _openSource(root);
        Show(new OpenedLibrary(root, source, LibraryView.Root(source)));
        _watcher = _watch(root, () => _postToUi(OnContentsChanged));
    }

    private void OnContentsChanged()
    {
        Refresh();
        ContentsChanged?.Invoke();
    }

    private void Show(OpenedLibrary? library)
    {
        _library = library;
        var entries = library?.View.Entries.Select(e => new LibraryItem(e)).ToList() ?? [];
        if (!entries.SequenceEqual(_entries)) // unchanged after a live refresh: keep the list's focus and scroll position
            Entries = entries;
        OnPropertyChanged(nameof(Root));
        OnPropertyChanged(nameof(RootDisplay));
        OnPropertyChanged(nameof(Folder));
        OnPropertyChanged(nameof(CurrentPath));
        OnPropertyChanged(nameof(CanGoUp));
        OnPropertyChanged(nameof(CanOpenInExplorer));
        OnPropertyChanged(nameof(Warning));
        OnPropertyChanged(nameof(HasWarning));
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private sealed record OpenedLibrary(string Root, IFolderSource Source, LibraryView View);
}
