using System.Collections.Immutable;
using QuickParrot.Core.Favorites;
using QuickParrot.Core.Keyboard;
using QuickParrot.Core.Library;

namespace QuickParrot.Core.Navigation;

/// <summary>Chord-navigation state machine: turns <see cref="ChordEvent"/>s into <see cref="NavigationAction"/>s
/// and exposes <see cref="ViewState"/> for the overlay. Each event moves one immutable session to the next.</summary>
/// <remarks>
/// Chord+Shift+F-key enters assign mode for that slot. Picking a file assigns it and spends the session; the same
/// F-key again (last played) and the clear key apply but stay in assign mode, so several slots can be edited at once.
/// </remarks>
public sealed class ChordNavigator
{
    private static readonly IReadOnlyList<NavigationAction> NoActions = [];
    private static readonly NavigationLayout EmptyLayout = LayoutBuilder.Build([]);

    private readonly IFolderSource _source;
    private Session? _session;
    private SearchSession? _search;
    private FragmentComposer? _fragments;
    private volatile OverlayViewState? _viewState;

    public ChordNavigator(IFolderSource source, string persistentPath = "")
    {
        _source = source;
        PersistentPath = string.Join('/', persistentPath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Where new sessions start; every change is also emitted as <see cref="PersistPath"/>.</summary>
    public string PersistentPath { get; private set; }

    /// <summary>
    /// Immutable snapshot, rebuilt from the source after each event; safe to read from another thread.
    /// Null/hidden when no chord session is active, or the current session is spent.
    /// </summary>
    public OverlayViewState? ViewState => _viewState;

    /// <summary>The favorite slot being assigned, if in assign mode; read on the navigator's own thread.</summary>
    public int? AssigningSlot => _session is { Spent: false } session ? session.AssignSlot : null;

    public IReadOnlyList<NavigationAction> Handle(ChordEvent evt)
    {
        if (evt is FragmentsPressed && _session is not null)
        {
            _session = null;
            _fragments = new FragmentComposer(_source.GetAllClips());
            _viewState = _fragments.ViewState;
            return NoActions;
        }

        if (_fragments is not null)
        {
            var fragmentActions = _fragments.Handle(evt);
            _viewState = _fragments.Closed ? null : _fragments.ViewState;
            if (_fragments.Closed)
                _fragments = null;
            return fragmentActions;
        }

        if (evt is SearchPressed && _session is not null)
        {
            _session = null;
            var clips = _source.GetAllClips().Where(clip => !FragmentLibrary.IsFragmentPath(clip.RelativePath)).ToArray();
            _search = new SearchSession(clips, LibrarySearch.CreateFolderContexts(clips), "", []);
            UpdateSearch("");
            return NoActions;
        }

        if (_search is not null)
            return HandleSearch(evt);

        var (next, actions) = Transition(_session, evt);
        Apply(next, actions, refreshLayout: evt is not SaveNavigationPressed);
        return actions;
    }

    private IReadOnlyList<NavigationAction> HandleSearch(ChordEvent evt)
    {
        var search = _search!;
        switch (evt)
        {
            case SearchTextEntered text:
                UpdateSearch(search.Query + string.Concat(text.Text.Where(c => !char.IsControl(c) && !char.IsDigit(c))));
                break;
            case SearchBackspacePressed when search.Query.Length > 0:
                var elements = System.Globalization.StringInfo.ParseCombiningCharacters(search.Query);
                UpdateSearch(search.Query[..elements[^1]]);
                break;
            case SearchSelectionPressed select:
                _search = null;
                _viewState = null;
                var index = select.Number - 1;
                return index >= 0 && index < search.Matches.Length
                    ? [new PlayClip(search.Matches[index].RelativePath)] : NoActions;
            case ChordCancelled:
                _search = null;
                _viewState = null;
                break;
        }
        return NoActions;
    }

    private void UpdateSearch(string query)
    {
        var search = _search!;
        var matches = LibrarySearch.Match(search.Clips, query);
        _search = search with { Query = query, Matches = matches };
        _viewState = new OverlayViewState("", OverlayLayoutKind.Wheel,
            matches.Select((entry, index) => new NumberedEntry(index + 1, entry.Name, false)
            {
                FolderContext = search.FolderContexts[entry.RelativePath],
            }).ToImmutableArray(),
            [], null, false) { SearchQuery = query };
    }

    internal void ExpireSaveNavigationConfirmation()
    {
        if (_session is { Spent: false, SaveNavigationConfirmed: true } session)
            Apply(session with { SaveNavigationConfirmed = false }, NoActions, refreshLayout: false);
    }

    /// <summary>Supplies the strip asked for by <see cref="NeedFavoritesPanel"/>; ignored unless still assigning
    /// that slot.</summary>
    public void ShowFavorites(FavoritesPanel panel)
    {
        if (_session is { Spent: false } session && session.AssignSlot == panel.TargetSlot)
            Apply(session with { Favorites = panel }, NoActions);
    }

    private (Session? Next, IReadOnlyList<NavigationAction> Actions) Transition(Session? session, ChordEvent evt) =>
        (session, evt) switch
        {
            (null, ChordPressed) => Start(),
            (null, ChordlessFavoritePressed c) when FavoriteSlots.IsValidSlot(c.Slot) =>
                (null, [new PlayFavorite(c.Slot)]),
            ({ AnyKeyPressed: false, Spent: false }, ChordReleased) => (null, [new StopPlayback()]), // bare tap
            (_, ChordReleased or ChordCancelled) => (null, NoActions),
            ({ Spent: false, AssignSlot: null } live, SaveNavigationPressed) =>
                (live with { AnyKeyPressed = true, SaveNavigationConfirmed = true },
                    live.CurrentPath != PersistentPath ? [new PersistPath(live.CurrentPath)] : NoActions),
            ({ Spent: false } live, DigitPressed d) => Digit(live with { AnyKeyPressed = true, SaveNavigationConfirmed = false }, d.Digit),
            ({ Spent: false } live, FavoritePressed f) when FavoriteSlots.IsValidSlot(f.Slot) =>
                Favorite(live with { SaveNavigationConfirmed = false }, f.Slot, f.Shift),
            ({ Spent: false, AssignSlot: int slot }, FavoriteClearPressed) => (session, [new ClearFavorite(slot)]),

            // Like playing a clip, a grab spends the session: the overlay hides and the release won't stop playback.
            ({ Spent: false } live, GrabPressed) => (live with { Spent = true }, [new GrabReplay()]),
            _ => (session, NoActions), // includes chord auto-repeat while already held
        };

    // Falls back to the nearest existing ancestor (ultimately root) if PersistentPath no longer exists,
    // and persists that fallback since PersistentPath is meant to always point somewhere real.
    private (Session?, IReadOnlyList<NavigationAction>) Start()
    {
        var path = PersistentPath;
        while (path.Length > 0 && (FragmentLibrary.IsFragmentPath(path) || _source.GetEntries(path) is null))
            path = GetParentPath(path);

        return (new Session(path), path == PersistentPath ? NoActions : [new PersistPath(path)]);
    }

    private (Session?, IReadOnlyList<NavigationAction>) Digit(Session session, int digit) =>
        digit == 0 ? Up(session) : Number(session, digit);

    // Selects from the layout the user was shown, not a fresh read, so the number always matches the overlay.
    private (Session?, IReadOnlyList<NavigationAction>) Number(Session session, int digit)
    {
        var layout = session.Layout;
        var index = digit - 1;

        if (layout.Kind == OverlayLayoutKind.Wheel)
        {
            return index >= 0 && index < layout.WheelEntries.Length
                ? Select(session, layout.WheelEntries[index])
                : (session, NoActions);
        }

        if (session.ZoomedColumn is int zoomedColumn)
        {
            var columnEntries = layout.GridColumns[zoomedColumn - 1];
            return index >= 0 && index < columnEntries.Length
                ? Select(session, columnEntries[index])
                : (session, NoActions);
        }

        return index >= 0 && index < layout.GridColumns.Length
            ? (session with { ZoomedColumn = digit }, NoActions)
            : (session, NoActions);
    }

    private (Session?, IReadOnlyList<NavigationAction>) Up(Session session)
    {
        if (session.ZoomedColumn is not null)
            return (session with { ZoomedColumn = null }, NoActions);

        if (session.CurrentPath.Length == 0)
            return (session, NoActions); // no-op at root

        var parent = GetParentPath(session.CurrentPath);
        return (session with { CurrentPath = parent }, NoActions);
    }

    private (Session?, IReadOnlyList<NavigationAction>) Select(Session session, FolderEntry entry)
    {
        if (entry.IsFolder)
        {
            var opened = session with { CurrentPath = entry.RelativePath, ZoomedColumn = null };
            return (opened, NoActions);
        }

        NavigationAction pick = session.AssignSlot is int slot
            ? new AssignFavorite(slot, entry.RelativePath)
            : new PlayClip(entry.RelativePath);
        return (session with { Spent = true }, [pick]);
    }

    private static (Session?, IReadOnlyList<NavigationAction>) Favorite(Session session, int slot, bool shift)
    {
        if (session.AssignSlot is not int target)
        {
            return shift
                ? (session with { AnyKeyPressed = true, AssignSlot = slot }, [new NeedFavoritesPanel(slot)])
                : (session with { AnyKeyPressed = true, Spent = true }, [new PlayFavorite(slot)]);
        }

        if (slot == target)
            return (session, [new AssignLastPlayedFavorite(slot)]);

        return session.Favorites is { } panel
            ? (session with { AssignSlot = slot, Favorites = panel with { TargetSlot = slot } }, NoActions)
            : (session with { AssignSlot = slot }, [new NeedFavoritesPanel(slot)]);
    }

    private static string GetParentPath(string path)
    {
        var separatorIndex = path.LastIndexOf('/');
        return separatorIndex < 0 ? "" : path[..separatorIndex];
    }

    // An unchanged session keeps its view state, so the engine only republishes real changes.
    private void Apply(Session? next, IReadOnlyList<NavigationAction> actions, bool refreshLayout = true)
    {
        foreach (var action in actions)
        {
            if (action is PersistPath persist)
                PersistentPath = persist.Path;
        }

        if (ReferenceEquals(next, _session))
            return;

        _session = refreshLayout && next is { Spent: false } live ? Relayout(live) : next;
        _viewState = _session is { Spent: false } shown ? BuildViewState(shown) : null;
    }

    private Session Relayout(Session session)
    {
        var entries = (_source.GetEntries(session.CurrentPath) ?? [])
            .Where(entry => !FragmentLibrary.IsFragmentPath(entry.RelativePath)).ToArray();
        var layout = LayoutBuilder.Build(entries);
        var zoomedColumn = session.ZoomedColumn > layout.GridColumns.Length
            ? null // column vanished on disk (or folder shrank to a wheel)
            : session.ZoomedColumn;
        return session with { Layout = layout, ZoomedColumn = zoomedColumn };
    }

    private OverlayViewState BuildViewState(Session session)
    {
        var layout = session.Layout;
        if (layout.Kind == OverlayLayoutKind.Wheel)
        {
            return new OverlayViewState(
                session.CurrentPath, OverlayLayoutKind.Wheel, NumberEntries(layout.WheelEntries), [], null, false,
                session.Favorites, ShowSaveNavigationHint: ShouldShowSaveHint(session), SaveNavigationConfirmed: session.SaveNavigationConfirmed);
        }

        var columns = layout.GridColumns
            .Select((column, i) => new GridColumn(i + 1, NumberEntries(column)))
            .ToImmutableArray();

        return new OverlayViewState(
            session.CurrentPath, OverlayLayoutKind.Grid, [], columns, session.ZoomedColumn, layout.Truncated,
            session.Favorites, ShowSaveNavigationHint: ShouldShowSaveHint(session), SaveNavigationConfirmed: session.SaveNavigationConfirmed);
    }

    private bool ShouldShowSaveHint(Session session) =>
        session.AssignSlot is null && (session.CurrentPath != PersistentPath || session.SaveNavigationConfirmed);

    private static ImmutableArray<NumberedEntry> NumberEntries(ImmutableArray<FolderEntry> entries) =>
        entries.Select((e, i) => new NumberedEntry(i + 1, e.Name, e.IsFolder)).ToImmutableArray();

    private sealed record Session(string CurrentPath)
    {
        public bool SaveNavigationConfirmed { get; init; }
        public NavigationLayout Layout { get; init; } = EmptyLayout;
        public int? ZoomedColumn { get; init; }
        public bool AnyKeyPressed { get; init; }
        public bool Spent { get; init; }
        public int? AssignSlot { get; init; }

        /// <summary>Set only in assign mode, once the engine supplies it.</summary>
        public FavoritesPanel? Favorites { get; init; }
    }

    private sealed record SearchSession(IReadOnlyList<FolderEntry> Clips,
        IReadOnlyDictionary<string, LibrarySearchFolderContext?> FolderContexts, string Query, FolderEntry[] Matches);
}
