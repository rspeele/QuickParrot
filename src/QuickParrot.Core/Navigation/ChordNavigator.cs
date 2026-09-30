using QuickParrot.Core.Library;

namespace QuickParrot.Core.Navigation;

/// <summary>Pure chord-navigation state machine: turns <see cref="ChordEvent"/>s into
/// <see cref="NavigationAction"/>s and exposes <see cref="ViewState"/> for the overlay.</summary>
public sealed class ChordNavigator
{
    private static readonly IReadOnlyList<NavigationAction> NoActions = [];

    private readonly IFolderSource _source;
    private Session? _session;
    private volatile OverlayViewState? _viewState;

    public ChordNavigator(IFolderSource source, string persistentPath = "")
    {
        _source = source;
        PersistentPath = persistentPath;
    }

    /// <summary>Raised with the new value whenever <see cref="PersistentPath"/> changes, so it can be saved.</summary>
    public event Action<string>? PersistentPathChanged;

    public string PersistentPath
    {
        get;
        set
        {
            var normalized = string.Join('/', value.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries));
            if (normalized == field)
                return;

            field = normalized;
            PersistentPathChanged?.Invoke(normalized);
        }
    }

    /// <summary>
    /// Immutable snapshot, rebuilt from the source after each event; safe to read from another thread.
    /// Null/hidden when no chord session is active, or the current session is spent.
    /// </summary>
    public OverlayViewState? ViewState => _viewState;

    public IReadOnlyList<NavigationAction> Handle(ChordEvent evt) => evt switch
    {
        ChordPressed => HandleChordPressed(),
        ChordReleased => HandleChordReleased(),
        ChordCancelled => HandleChordCancelled(),
        DigitPressed d => HandleDigit(d.Digit, d.Shift),
        _ => NoActions,
    };

    private IReadOnlyList<NavigationAction> HandleChordPressed()
    {
        if (_session is not null)
            return NoActions; // auto-repeat while already held

        _session = new Session { CurrentPath = ResolveStartingPath() };
        Refresh(_session);
        return NoActions;
    }

    private IReadOnlyList<NavigationAction> HandleChordReleased()
    {
        if (_session is null)
            return NoActions;

        var session = _session;
        _session = null;
        _viewState = null;

        var bareTap = !session.AnyDigitPressed && !session.Spent;
        return bareTap ? [new StopPlayback()] : NoActions;
    }

    private IReadOnlyList<NavigationAction> HandleChordCancelled()
    {
        _session = null;
        _viewState = null;
        return NoActions;
    }

    private IReadOnlyList<NavigationAction> HandleDigit(int digit, bool shift)
    {
        if (_session is not { Spent: false } session)
            return NoActions;

        session.AnyDigitPressed = true;

        var actions = digit == 0 ? HandleZero(session, shift) : HandleNumber(session, digit, shift);
        Refresh(session);
        return actions;
    }

    // Selects from the layout the user was shown, not a fresh read, so the number always matches the overlay.
    private IReadOnlyList<NavigationAction> HandleNumber(Session session, int digit, bool shift)
    {
        var layout = session.Layout;
        var index = digit - 1;

        if (layout.Kind == OverlayLayoutKind.Wheel)
        {
            return index >= 0 && index < layout.WheelEntries.Count
                ? Select(session, layout.WheelEntries[index], shift)
                : NoActions;
        }

        if (session.ZoomedColumn is int zoomedColumn)
        {
            var columnEntries = layout.GridColumns[zoomedColumn - 1];
            return index >= 0 && index < columnEntries.Count
                ? Select(session, columnEntries[index], shift)
                : NoActions;
        }

        if (index >= 0 && index < layout.GridColumns.Count)
            session.ZoomedColumn = digit;

        return NoActions;
    }

    private IReadOnlyList<NavigationAction> HandleZero(Session session, bool shift)
    {
        if (session.ZoomedColumn is not null)
        {
            session.ZoomedColumn = null;
            return NoActions;
        }

        if (session.CurrentPath.Length == 0)
            return NoActions; // no-op at root

        var parent = GetParentPath(session.CurrentPath);
        session.CurrentPath = parent;

        if (shift)
            PersistentPath = parent;

        return NoActions;
    }

    private IReadOnlyList<NavigationAction> Select(Session session, FolderEntry entry, bool shift)
    {
        if (entry.IsFolder)
        {
            session.CurrentPath = entry.RelativePath;
            session.ZoomedColumn = null;

            if (shift)
                PersistentPath = entry.RelativePath;

            return NoActions;
        }

        if (shift)
            PersistentPath = session.CurrentPath;

        session.Spent = true;
        return [new PlayClip(entry.RelativePath)];
    }

    // Falls back to the nearest existing ancestor (ultimately root) if PersistentPath no longer exists,
    // and persists that fallback since PersistentPath is meant to always point somewhere real.
    private string ResolveStartingPath()
    {
        var path = PersistentPath;
        while (path.Length > 0 && _source.GetEntries(path) is null)
            path = GetParentPath(path);

        if (path != PersistentPath)
            PersistentPath = path;

        return path;
    }

    private static string GetParentPath(string path)
    {
        var separatorIndex = path.LastIndexOf('/');
        return separatorIndex < 0 ? "" : path[..separatorIndex];
    }

    private void Refresh(Session session)
    {
        if (session.Spent)
        {
            _viewState = null;
            return;
        }

        var layout = LayoutBuilder.Build(_source.GetEntries(session.CurrentPath) ?? []);
        if (session.ZoomedColumn > layout.GridColumns.Count)
            session.ZoomedColumn = null; // column vanished on disk (or folder shrank to a wheel)

        session.Layout = layout;
        _viewState = BuildViewState(session.CurrentPath, layout, session.ZoomedColumn);
    }

    private static OverlayViewState BuildViewState(string path, NavigationLayout layout, int? zoomedColumn)
    {
        if (layout.Kind == OverlayLayoutKind.Wheel)
        {
            return new OverlayViewState(
                path, OverlayLayoutKind.Wheel, Number(layout.WheelEntries), [], null, false);
        }

        var columns = layout.GridColumns
            .Select((column, i) => new GridColumn(i + 1, Number(column)))
            .ToList();

        return new OverlayViewState(
            path, OverlayLayoutKind.Grid, [], columns, zoomedColumn, layout.Truncated);
    }

    private static IReadOnlyList<NumberedEntry> Number(IReadOnlyList<FolderEntry> entries) =>
        entries.Select((e, i) => new NumberedEntry(i + 1, e.Name, e.IsFolder, e.RelativePath)).ToList();

    private sealed class Session
    {
        public required string CurrentPath { get; set; }
        public NavigationLayout Layout { get; set; } = LayoutBuilder.Build([]);
        public int? ZoomedColumn { get; set; }
        public bool AnyDigitPressed { get; set; }
        public bool Spent { get; set; }
    }
}
