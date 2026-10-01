namespace QuickParrot.Core.Editing;

public enum SelectionDragTarget
{
    NewSelection,
    StartHandle,
    EndHandle,
}

/// <summary>
/// Mouse logic for the waveform: dragging a handle moves that edge, dragging elsewhere selects a new range, and a press
/// that barely moves is a click that places the cursor. Positions are in the viewport's pixel units.
/// </summary>
public sealed class SelectionGesture
{
    public const double HandleTolerance = 6;
    public const double ClickTolerance = 3;

    private readonly WaveformViewport _viewport;
    private readonly int _minLength;
    private readonly double _pressX;
    private readonly int _anchor;
    private bool _dragged;

    private SelectionGesture(WaveformViewport viewport, int minLength, double pressX, SelectionDragTarget target, int anchor)
    {
        _viewport = viewport;
        _minLength = minLength;
        _pressX = pressX;
        Target = target;
        _anchor = anchor;
    }

    public SelectionDragTarget Target { get; }

    /// <summary>The fixed point the drag grows from: the opposite handle, or (for a new selection) the press frame.</summary>
    public int Anchor => _anchor;

    public static SelectionDragTarget HitTest(WaveformViewport viewport, ClipSelection? selection, double x)
    {
        if (selection is not { Length: > 0 } s)
            return SelectionDragTarget.NewSelection;

        var startDistance = Math.Abs(viewport.XAt(s.Start) - x);
        var endDistance = Math.Abs(viewport.XAt(s.End) - x);
        if (Math.Min(startDistance, endDistance) > HandleTolerance)
            return SelectionDragTarget.NewSelection;

        return startDistance < endDistance ? SelectionDragTarget.StartHandle : SelectionDragTarget.EndHandle;
    }

    public static SelectionGesture Begin(WaveformViewport viewport, ClipSelection? selection, double x, int minLength)
    {
        var target = HitTest(viewport, selection, x);
        var anchor = target switch
        {
            SelectionDragTarget.StartHandle => selection!.Value.End,
            SelectionDragTarget.EndHandle => selection!.Value.Start,
            _ => FrameAt(viewport, x),
        };
        return new SelectionGesture(viewport, minLength, x, target, anchor);
    }

    /// <summary>Returns the selection for the pointer at <paramref name="x"/>, or null while it's still within click distance.</summary>
    public ClipSelection? Move(double x)
    {
        if (!_dragged && Target == SelectionDragTarget.NewSelection && Math.Abs(x - _pressX) < ClickTolerance)
            return null;

        _dragged = true;
        return ClipSelection.FromPoints(_anchor, FrameAt(_viewport, x), _viewport.TotalFrames, _minLength);
    }

    /// <summary>True if the gesture never became a drag, i.e. it was a click at <see cref="ClickFrame"/>.</summary>
    public bool IsClick => !_dragged;

    public int ClickFrame => FrameAt(_viewport, _pressX);

    private static int FrameAt(WaveformViewport viewport, double x) =>
        (int)Math.Clamp(Math.Round(viewport.FrameAt(x)), 0, viewport.TotalFrames);
}
