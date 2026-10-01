using QuickParrot.App.Mvvm;
using QuickParrot.Core.Engine;
using QuickParrot.Core.Favorites;
using QuickParrot.Core.Library;
using QuickParrot.Core.Settings;

namespace QuickParrot.App;

public sealed record FavoriteRow(FavoriteSlotView View)
{
    public int Slot => View.Slot;

    public string KeyName => View.KeyName;

    public string Text => View.DisplayText;

    public bool IsMissing => View.Missing;

    public bool HasClip => !View.IsEmpty;

    public string MenuHeader => $"{KeyName}: {Text}";
}

/// <summary>The Library tab's F1-F12 list, the "Assign to" menu, and the plain-F-key setting.</summary>
public sealed class FavoritesViewModel : ObservableObject
{
    private readonly QuickParrotEngine _engine;
    private readonly Action<string> _setStatus;
    private readonly SynchronizationContext _ui;
    private IReadOnlyList<FavoriteRow> _rows = [];
    private object? _shownFor;
    private bool _playWithoutChord;

    public FavoritesViewModel(QuickParrotEngine engine, Action<string> setStatus)
    {
        _engine = engine;
        _setStatus = setStatus;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _playWithoutChord = engine.Settings.FavoritesWithoutChord;
        _engine.SettingsChanged += settings => _ui.Post(_ => Refresh(settings), null);
        _engine.FavoritesNotice += notice => _ui.Post(_ => _setStatus(notice.Message), null);
        Refresh(engine.Settings, force: true);
    }

    public IReadOnlyList<FavoriteRow> Rows
    {
        get => _rows;
        private set => SetField(ref _rows, value);
    }

    public bool PlayWithoutChord
    {
        get => _playWithoutChord;
        set
        {
            if (SetField(ref _playWithoutChord, value))
                _engine.UpdateSettings(s => s with { FavoritesWithoutChord = value });
        }
    }

    public void Play(FavoriteRow? row)
    {
        if (row is not null)
            _engine.PlayFavorite(row.Slot);
    }

    public void Clear(FavoriteRow? row)
    {
        if (row is not null)
            _engine.ClearFavorite(row.Slot);
    }

    /// <summary>Folders can't be favorites, so they're ignored.</summary>
    public void Assign(FavoriteRow? row, FolderEntry? entry)
    {
        if (row is not null && entry is { IsFolder: false })
            _engine.AssignFavorite(row.Slot, entry.RelativePath);
    }

    /// <summary>Re-checks which favorites are missing, e.g. after files changed in the library.</summary>
    public void Refresh() => Refresh(_engine.Settings, force: true);

    // Skips the file checks when an unrelated setting (like a volume slider) changed.
    private void Refresh(AppSettings settings, bool force = false)
    {
        var relevant = (settings.Favorites, settings.LibraryRoot, settings.ChordKey, settings.PushToTalkBinding);
        if (!force && Equals(relevant, _shownFor))
            return;

        _shownFor = relevant;
        var library = string.IsNullOrEmpty(settings.LibraryRoot) ? null : new FileSystemFolderSource(settings.LibraryRoot);
        Rows = FavoriteStatus.Describe(
                settings.Favorites, library is null ? null : library.ClipExists, settings.ChordKey, settings.PushToTalkBinding)
            .Select(view => new FavoriteRow(view))
            .ToList();
    }
}
