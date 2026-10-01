namespace QuickParrot.Core.Editing;

/// <summary>
/// Undo/redo over a clip editor's selection: a bounded stack of past selections plus a redo stack that a new
/// push clears. Pure state; the caller decides which selection changes are worth a step (e.g. not every mouse move).
/// </summary>
public sealed class SelectionHistory
{
    public const int DefaultCapacity = 100;

    private readonly int _capacity;
    private readonly List<ClipSelection> _past = [];
    private readonly List<ClipSelection> _future = [];
    private ClipSelection _current;

    public SelectionHistory(ClipSelection initial, int capacity = DefaultCapacity)
    {
        _capacity = Math.Max(1, capacity);
        _current = initial;
    }

    public ClipSelection Current => _current;

    internal bool CanUndo => _past.Count > 0;

    internal bool CanRedo => _future.Count > 0;

    /// <summary>Records a step away from <see cref="Current"/>; a no-op if <paramref name="selection"/> matches it. Clears redo.</summary>
    public void Push(ClipSelection selection)
    {
        if (selection == _current)
            return;

        _past.Add(_current);
        if (_past.Count > _capacity)
            _past.RemoveAt(0);

        _current = selection;
        _future.Clear();
    }

    /// <summary>Steps back one selection, or null if there's nothing to undo.</summary>
    public ClipSelection? Undo()
    {
        if (_past.Count == 0)
            return null;

        _future.Add(_current);
        _current = _past[^1];
        _past.RemoveAt(_past.Count - 1);
        return _current;
    }

    /// <summary>Steps forward one selection, or null if there's nothing to redo.</summary>
    public ClipSelection? Redo()
    {
        if (_future.Count == 0)
            return null;

        _past.Add(_current);
        if (_past.Count > _capacity)
            _past.RemoveAt(0);

        _current = _future[^1];
        _future.RemoveAt(_future.Count - 1);
        return _current;
    }
}
