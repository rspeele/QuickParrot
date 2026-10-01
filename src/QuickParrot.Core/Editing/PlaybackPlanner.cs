namespace QuickParrot.Core.Editing;

/// <summary>Pure range math behind "play from cursor", "play end", drag-release samples, bracket-key edits and the stop marker.</summary>
public static class PlaybackPlanner
{
    /// <summary>"From cursor": stays within the selection if the cursor starts inside it, otherwise ignores the selection and runs to the capture's end.</summary>
    public static ClipSelection FromCursorRange(ClipSelection selection, int cursor, int totalFrames)
    {
        if (selection.Length > 0 && selection.Contains(cursor))
            return new ClipSelection(cursor, selection.End);

        var start = cursor >= totalFrames ? 0 : cursor;
        return new ClipSelection(start, totalFrames);
    }

    /// <summary>The last <paramref name="sampleFrames"/> of <paramref name="selection"/>, or all of it if shorter.</summary>
    public static ClipSelection LastSeconds(ClipSelection selection, int sampleFrames) =>
        new(Math.Max(selection.Start, selection.End - Math.Max(0, sampleFrames)), selection.End);

    /// <summary>The first <paramref name="sampleFrames"/> of <paramref name="selection"/>, or all of it if shorter.</summary>
    public static ClipSelection FirstSeconds(ClipSelection selection, int sampleFrames) =>
        new(selection.Start, Math.Min(selection.End, selection.Start + Math.Max(0, sampleFrames)));

    /// <summary>
    /// What to play as a sample after a selection drag: the edge that moved (start or end handle) — or, for a brand-new
    /// selection, whichever side the drag was released on.
    /// </summary>
    public static ClipSelection DragReleaseSampleRange(ClipSelection selection, SelectionDragTarget target, int anchorFrame, int sampleFrames)
    {
        // selection is post-snap, so the anchor (a pre-snap edge) may no longer sit exactly on either edge;
        // the edge it's still nearest to is the one it anchored.
        var playEnd = target switch
        {
            SelectionDragTarget.EndHandle => true,
            SelectionDragTarget.StartHandle => false,
            _ => Math.Abs(anchorFrame - selection.Start) <= Math.Abs(anchorFrame - selection.End),
        };
        return playEnd ? LastSeconds(selection, sampleFrames) : FirstSeconds(selection, sampleFrames);
    }

    /// <summary>"[": moves the start to <paramref name="target"/>; if that would land at or past the end, the end resets to the capture's end.</summary>
    public static ClipSelection SetSelectionStart(ClipSelection selection, int target, int totalFrames)
    {
        target = Math.Clamp(target, 0, totalFrames);
        return target >= selection.End ? new ClipSelection(target, totalFrames) : new ClipSelection(target, selection.End);
    }

    /// <summary>"]": moves the end to <paramref name="target"/>; if that would land at or before the start, the start resets to the capture's start.</summary>
    public static ClipSelection SetSelectionEnd(ClipSelection selection, int target, int totalFrames)
    {
        target = Math.Clamp(target, 0, totalFrames);
        return target <= selection.Start ? new ClipSelection(0, target) : new ClipSelection(selection.Start, target);
    }

    /// <summary>A manual stop marks <paramref name="frame"/>, replacing any earlier mark; a natural end leaves the mark alone.</summary>
    public static double StopMarker(double current, bool manualStop, double frame) => manualStop ? frame : current;
}
