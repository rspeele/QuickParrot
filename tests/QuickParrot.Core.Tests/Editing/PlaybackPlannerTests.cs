using QuickParrot.Core.Editing;

namespace QuickParrot.Core.Tests.Editing;

public class FromCursorRangeTests
{
    [Fact]
    public void CursorInsideTheSelection_PlaysToTheSelectionEnd()
    {
        var selection = new ClipSelection(1000, 9000);

        Assert.Equal(new ClipSelection(5000, 9000), PlaybackPlanner.FromCursorRange(selection, 5000, 20000));
    }

    [Fact]
    public void CursorAtTheSelectionStart_IsInside()
    {
        var selection = new ClipSelection(1000, 9000);

        Assert.Equal(new ClipSelection(1000, 9000), PlaybackPlanner.FromCursorRange(selection, 1000, 20000));
    }

    [Theory]
    [InlineData(9000)]  // at the selection end: outside (end is exclusive)
    [InlineData(500)]   // before the selection
    [InlineData(12000)] // after the selection
    public void CursorOutsideTheSelection_IgnoresItAndPlaysToTheCaptureEnd(int cursor)
    {
        var selection = new ClipSelection(1000, 9000);

        Assert.Equal(new ClipSelection(cursor, 20000), PlaybackPlanner.FromCursorRange(selection, cursor, 20000));
    }

    [Fact]
    public void CursorAtTheVeryEnd_WrapsToTheCaptureStart()
    {
        Assert.Equal(new ClipSelection(0, 20000), PlaybackPlanner.FromCursorRange(new ClipSelection(1000, 9000), 20000, 20000));
    }

    [Fact]
    public void NoSelection_AlwaysPlaysFromTheCursorToTheEnd()
    {
        Assert.Equal(new ClipSelection(5000, 20000), PlaybackPlanner.FromCursorRange(new ClipSelection(0, 0), 5000, 20000));
    }
}

public class SampleRangeTests
{
    [Fact]
    public void LastSeconds_TakesTheTailOfTheSelection()
    {
        Assert.Equal(new ClipSelection(8000, 9000), PlaybackPlanner.LastSeconds(new ClipSelection(1000, 9000), 1000));
    }

    [Fact]
    public void LastSeconds_OnAShortSelection_TakesItAll()
    {
        Assert.Equal(new ClipSelection(1000, 1400), PlaybackPlanner.LastSeconds(new ClipSelection(1000, 1400), 1000));
    }

    [Fact]
    public void FirstSeconds_TakesTheHeadOfTheSelection()
    {
        Assert.Equal(new ClipSelection(1000, 2000), PlaybackPlanner.FirstSeconds(new ClipSelection(1000, 9000), 1000));
    }

    [Fact]
    public void FirstSeconds_OnAShortSelection_TakesItAll()
    {
        Assert.Equal(new ClipSelection(1000, 1400), PlaybackPlanner.FirstSeconds(new ClipSelection(1000, 1400), 1000));
    }
}

public class DragReleaseSampleRangeTests
{
    private static readonly ClipSelection Selection = new(1000, 9000);

    [Fact]
    public void StartHandle_PlaysTheFirstSecond()
    {
        var range = PlaybackPlanner.DragReleaseSampleRange(Selection, SelectionDragTarget.StartHandle, anchorFrame: 9000, sampleFrames: 1000);

        Assert.Equal(new ClipSelection(1000, 2000), range);
    }

    [Fact]
    public void EndHandle_PlaysTheLastSecond()
    {
        var range = PlaybackPlanner.DragReleaseSampleRange(Selection, SelectionDragTarget.EndHandle, anchorFrame: 1000, sampleFrames: 1000);

        Assert.Equal(new ClipSelection(8000, 9000), range);
    }

    [Fact]
    public void NewSelection_DraggedLeftToRight_PlaysTheEndSample()
    {
        // Press at the start, release past the end: anchor sits at the selection's start.
        var range = PlaybackPlanner.DragReleaseSampleRange(Selection, SelectionDragTarget.NewSelection, anchorFrame: 1000, sampleFrames: 1000);

        Assert.Equal(new ClipSelection(8000, 9000), range);
    }

    [Fact]
    public void NewSelection_DraggedRightToLeft_PlaysTheStartSample()
    {
        // Press at the end, release past the start: anchor sits at the selection's end.
        var range = PlaybackPlanner.DragReleaseSampleRange(Selection, SelectionDragTarget.NewSelection, anchorFrame: 9000, sampleFrames: 1000);

        Assert.Equal(new ClipSelection(1000, 2000), range);
    }

    [Fact]
    public void NewSelection_DraggedLeftToRight_WhenSnappingMovesTheStartPastTheAnchor_StillPlaysTheEndSample()
    {
        // Anchor (press frame) was 1000, pre-snap, i.e. exactly at the start; quiet-point snapping then nudged the
        // start a few frames to 996, to the far side of the anchor. A rule that only checks "anchor <= start" would
        // misread this as a right-to-left drag; proximity to the (now distant) end still says otherwise.
        var snapped = new ClipSelection(996, 9000);

        var range = PlaybackPlanner.DragReleaseSampleRange(snapped, SelectionDragTarget.NewSelection, anchorFrame: 1000, sampleFrames: 1000);

        Assert.Equal(new ClipSelection(8000, 9000), range);
    }

    [Fact]
    public void NewSelection_DraggedRightToLeft_WhenSnappingNudgesTheEnd_StillPlaysTheStartSample()
    {
        // Anchor (press frame) was 9000, pre-snap, i.e. exactly at the end; quiet-point snapping nudged the end a
        // few frames further away, to 9004. The end stays the nearer edge, so this should keep reading as right-to-left.
        var snapped = new ClipSelection(1000, 9004);

        var range = PlaybackPlanner.DragReleaseSampleRange(snapped, SelectionDragTarget.NewSelection, anchorFrame: 9000, sampleFrames: 1000);

        Assert.Equal(new ClipSelection(1000, 2000), range);
    }
}

public class BracketKeyTests
{
    [Fact]
    public void SetSelectionStart_MovesTheStart()
    {
        Assert.Equal(new ClipSelection(3000, 9000), PlaybackPlanner.SetSelectionStart(new ClipSelection(1000, 9000), 3000, 20000));
    }

    [Fact]
    public void SetSelectionStart_AtOrPastTheEnd_ResetsTheEndToTheCaptureEnd()
    {
        Assert.Equal(new ClipSelection(9000, 20000), PlaybackPlanner.SetSelectionStart(new ClipSelection(1000, 9000), 9000, 20000));
        Assert.Equal(new ClipSelection(15000, 20000), PlaybackPlanner.SetSelectionStart(new ClipSelection(1000, 9000), 15000, 20000));
    }

    [Fact]
    public void SetSelectionStart_ClampsToTheCapture()
    {
        Assert.Equal(new ClipSelection(0, 9000), PlaybackPlanner.SetSelectionStart(new ClipSelection(1000, 9000), -500, 20000));
    }

    [Fact]
    public void SetSelectionEnd_MovesTheEnd()
    {
        Assert.Equal(new ClipSelection(1000, 7000), PlaybackPlanner.SetSelectionEnd(new ClipSelection(1000, 9000), 7000, 20000));
    }

    [Fact]
    public void SetSelectionEnd_AtOrBeforeTheStart_ResetsTheStartToTheCaptureStart()
    {
        Assert.Equal(new ClipSelection(0, 1000), PlaybackPlanner.SetSelectionEnd(new ClipSelection(1000, 9000), 1000, 20000));
        Assert.Equal(new ClipSelection(0, 200), PlaybackPlanner.SetSelectionEnd(new ClipSelection(1000, 9000), 200, 20000));
    }

    [Fact]
    public void SetSelectionEnd_ClampsToTheCapture()
    {
        Assert.Equal(new ClipSelection(1000, 20000), PlaybackPlanner.SetSelectionEnd(new ClipSelection(1000, 9000), 99999, 20000));
    }
}

public class StopMarkerTests
{
    [Fact]
    public void ManualStop_SetsTheMarker()
    {
        Assert.Equal(1234, PlaybackPlanner.StopMarker(double.NaN, manualStop: true, frame: 1234));
    }

    [Fact]
    public void NaturalEnd_LeavesTheMarkerAlone()
    {
        Assert.Equal(1234, PlaybackPlanner.StopMarker(1234, manualStop: false, frame: 9999));
    }

    [Fact]
    public void ANewManualStop_ReplacesTheOldMark()
    {
        Assert.Equal(5555, PlaybackPlanner.StopMarker(1234, manualStop: true, frame: 5555));
    }
}
