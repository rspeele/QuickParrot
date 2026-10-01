using System.Collections.Immutable;

namespace QuickParrot.Core.Editing;

/// <summary>
/// The clip editor's selection and cursor, with undo/redo over the selection. <see cref="Preview"/> shows a selection
/// without recording a step (e.g. mid-drag); <see cref="Push"/> records one. Immutable: each change returns a new value.
/// </summary>
public sealed record SelectionHistory
{
    public const int DefaultCapacity = 100;

    private SelectionHistory(ClipSelection committed, int cursor, int capacity)
    {
        Committed = committed;
        Cursor = cursor;
        Capacity = capacity;
    }

    /// <summary>What the editor shows: the last recorded step, or a previewed selection on top of it.</summary>
    public ClipSelection Current => Draft ?? Committed;

    /// <summary>The cursor frame; not part of undo.</summary>
    public int Cursor { get; private init; }

    public bool CanUndo => !Past.IsEmpty;

    public bool CanRedo => !Future.IsEmpty;

    // The selection the next step is recorded from; Current differs from it only while a preview is showing.
    private ClipSelection Committed { get; init; }

    private ClipSelection? Draft { get; init; }

    private ImmutableList<ClipSelection> Past { get; init; } = [];

    private ImmutableList<ClipSelection> Future { get; init; } = [];

    private int Capacity { get; }

    public static SelectionHistory Start(ClipSelection initial, int cursor = 0, int capacity = DefaultCapacity) =>
        new(initial, cursor, Math.Max(1, capacity));

    /// <summary>Shows <paramref name="selection"/> without recording a step.</summary>
    public SelectionHistory Preview(ClipSelection selection) =>
        this with { Draft = selection == Committed ? null : selection };

    /// <summary>Records a step to <paramref name="selection"/> and clears redo; no step if it matches the last one recorded.</summary>
    public SelectionHistory Push(ClipSelection selection) => selection == Committed
        ? this with { Draft = null }
        : this with { Past = Capped(Past.Add(Committed)), Committed = selection, Draft = null, Future = [] };

    /// <summary>Steps back one selection; unchanged if there's nothing to undo.</summary>
    public SelectionHistory Undo() => Past.IsEmpty
        ? this
        : this with { Future = Future.Add(Committed), Committed = Past[^1], Past = Past.RemoveAt(Past.Count - 1), Draft = null };

    /// <summary>Steps forward one selection; unchanged if there's nothing to redo.</summary>
    public SelectionHistory Redo() => Future.IsEmpty
        ? this
        : this with { Past = Capped(Past.Add(Committed)), Committed = Future[^1], Future = Future.RemoveAt(Future.Count - 1), Draft = null };

    public SelectionHistory WithCursor(int cursor) => this with { Cursor = cursor };

    private ImmutableList<ClipSelection> Capped(ImmutableList<ClipSelection> past) =>
        past.Count > Capacity ? past.RemoveAt(0) : past;
}
