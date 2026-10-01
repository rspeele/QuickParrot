namespace QuickParrot.Core.Editing;

public enum SelectionDragTarget
{
    NewSelection,
    StartHandle,
    EndHandle,
}

/// <summary>
/// Mouse logic for the waveform: dragging a handle moves an edge, dragging elsewhere selects a new range, and a press that
/// barely moves is a click. Each call takes the current viewport (it may have scrolled since the press); immutable.
/// </summary>
public sealed record SelectionGesture
{
    public const double HandleTolerance = 6;
    public const double ClickTolerance = 3;

    private SelectionGesture(SelectionDragTarget target, int anchor, double pressX, int clickFrame, int minLength)
    {
        Target = target;
        Anchor = anchor;
        PressX = pressX;
        ClickFrame = clickFrame;
        MinLength = minLength;
    }

    public SelectionDragTarget Target { get; }

    /// <summary>The fixed point the drag grows from: the opposite handle, or (for a new selection) the press frame.</summary>
    public int Anchor { get; }

    /// <summary>The frame pressed, where a click places the cursor.</summary>
    public int ClickFrame { get; }

    /// <summary>The selection for the latest move, or null while it's still within click distance.</summary>
    public ClipSelection? Selection { get; private init; }

    /// <summary>True if the gesture never became a drag, i.e. it was a click at <see cref="ClickFrame"/>.</summary>
    public bool IsClick => Selection is null;

    private double PressX { get; }

    private int MinLength { get; }

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
        var pressFrame = FrameAt(viewport, x);
        var anchor = target switch
        {
            SelectionDragTarget.StartHandle => selection!.Value.End,
            SelectionDragTarget.EndHandle => selection!.Value.Start,
            _ => pressFrame,
        };
        return new SelectionGesture(target, anchor, x, pressFrame, minLength);
    }

    /// <summary>The gesture with the pointer at <paramref name="x"/> in <paramref name="viewport"/>; see <see cref="Selection"/>.</summary>
    public SelectionGesture Move(WaveformViewport viewport, double x)
    {
        if (IsClick && Target == SelectionDragTarget.NewSelection && Math.Abs(x - PressX) < ClickTolerance)
            return this;

        return this with { Selection = ClipSelection.FromPoints(Anchor, FrameAt(viewport, x), viewport.TotalFrames, MinLength) };
    }

    private static int FrameAt(WaveformViewport viewport, double x) =>
        (int)Math.Clamp(Math.Round(viewport.FrameAt(x)), 0, viewport.TotalFrames);
}
