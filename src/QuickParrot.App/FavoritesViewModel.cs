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
    private readonly SettingsMirror _settings;
    private readonly Func<string, IFolderSource> _openSource;
    private IReadOnlyList<FavoriteRow> _rows = [];

    public FavoritesViewModel(QuickParrotEngine engine, SettingsMirror settings, Func<string, IFolderSource> openSource)
    {
        _engine = engine;
        _settings = settings;
        _openSource = openSource;
        _settings.Changed += OnSettingsChanged;
        Refresh();
    }

    public IReadOnlyList<FavoriteRow> Rows
    {
        get => _rows;
        private set => SetField(ref _rows, value);
    }

    public bool PlayWithoutChord
    {
        get => _settings.Current.FavoritesWithoutChord;
        set => _settings.Update(s => s with { FavoritesWithoutChord = value });
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
    public void Refresh()
    {
        var settings = _settings.Current;
        var library = string.IsNullOrEmpty(settings.LibraryRoot) ? null : _openSource(settings.LibraryRoot);
        Rows = FavoriteStatus.Describe(
                settings.Favorites, library is null ? null : library.ClipExists, settings.ChordKey, settings.PushToTalkBinding)
            .Select(view => new FavoriteRow(view))
            .ToList();
    }

    // Skips the file checks when an unrelated setting (like a volume slider) changed.
    private void OnSettingsChanged(AppSettings old, AppSettings now)
    {
        if (old.FavoritesWithoutChord != now.FavoritesWithoutChord)
            OnPropertyChanged(nameof(PlayWithoutChord));

        if (!(old.Favorites, old.LibraryRoot, old.ChordKey, old.PushToTalkBinding)
            .Equals((now.Favorites, now.LibraryRoot, now.ChordKey, now.PushToTalkBinding)))
            Refresh();
    }
}
