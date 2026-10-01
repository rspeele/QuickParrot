using QuickParrot.Core.Editing;

namespace QuickParrot.Core.Tests.Editing;

public class SelectionHistoryTests
{
    private static readonly ClipSelection Initial = new(0, 100);

    [Fact]
    public void PushThenUndo_RestoresThePreviousSelection()
    {
        var history = SelectionHistory.Start(Initial).Push(new ClipSelection(10, 90));

        Assert.True(history.CanUndo);
        var undone = history.Undo();
        Assert.Equal(Initial, undone.Current);
        Assert.False(undone.CanUndo);
    }

    [Fact]
    public void Redo_AfterUndo_ReappliesTheStep()
    {
        var history = SelectionHistory.Start(Initial).Push(new ClipSelection(10, 90)).Undo();

        Assert.True(history.CanRedo);
        var redone = history.Redo();
        Assert.Equal(new ClipSelection(10, 90), redone.Current);
        Assert.False(redone.CanRedo);
    }

    [Fact]
    public void Push_AfterUndo_ClearsTheRedoStack()
    {
        var history = SelectionHistory.Start(Initial).Push(new ClipSelection(10, 90)).Undo()
            .Push(new ClipSelection(20, 80));

        Assert.False(history.CanRedo);
        Assert.Equal(new ClipSelection(20, 80), history.Current);
    }

    [Fact]
    public void Push_WithTheSameSelection_IsNotAStep()
    {
        Assert.False(SelectionHistory.Start(Initial).Push(Initial).CanUndo);
    }

    [Fact]
    public void UndoAndRedo_WithNothingToDo_ReturnTheSameHistory()
    {
        var history = SelectionHistory.Start(Initial);

        Assert.Same(history, history.Undo());
        Assert.Same(history, history.Redo());
    }

    [Fact]
    public void Capacity_DropsTheOldestEntryOnceExceeded()
    {
        var history = SelectionHistory.Start(new ClipSelection(0, 1), capacity: 2)
            .Push(new ClipSelection(0, 2))  // past: [0,1]
            .Push(new ClipSelection(0, 3))  // past: [0,1] [0,2]
            .Push(new ClipSelection(0, 4)); // past: [0,2] [0,3] — [0,1] dropped

        history = history.Undo();
        Assert.Equal(new ClipSelection(0, 3), history.Current);
        history = history.Undo();
        Assert.Equal(new ClipSelection(0, 2), history.Current);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void UndoRedo_RoundTrip_ThroughSeveralSteps()
    {
        var history = SelectionHistory.Start(new ClipSelection(0, 1))
            .Push(new ClipSelection(0, 2))
            .Push(new ClipSelection(0, 3))
            .Undo()
            .Undo();

        history = history.Redo();
        Assert.Equal(new ClipSelection(0, 2), history.Current);
        history = history.Redo();
        Assert.Equal(new ClipSelection(0, 3), history.Current);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Preview_ShowsASelectionWithoutRecordingAStep()
    {
        var history = SelectionHistory.Start(Initial).Preview(new ClipSelection(5, 50));

        Assert.Equal(new ClipSelection(5, 50), history.Current);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void PushAfterPreviews_RecordsOneStepFromTheSelectionBeforeThem()
    {
        var history = SelectionHistory.Start(Initial)
            .Preview(new ClipSelection(5, 50))
            .Preview(new ClipSelection(5, 60))
            .Push(new ClipSelection(6, 61));

        Assert.Equal(new ClipSelection(6, 61), history.Current);
        Assert.Equal(Initial, history.Undo().Current);
        Assert.False(history.Undo().CanUndo);
    }

    [Fact]
    public void PushingBackToTheRecordedSelection_DropsThePreviewWithoutAStep()
    {
        var history = SelectionHistory.Start(Initial).Preview(new ClipSelection(5, 50)).Push(Initial);

        Assert.Equal(Initial, history.Current);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void UndoDuringAPreview_StepsBackFromTheRecordedSelection()
    {
        var history = SelectionHistory.Start(Initial)
            .Push(new ClipSelection(10, 90))
            .Preview(new ClipSelection(20, 30))
            .Undo();

        Assert.Equal(Initial, history.Current);
        Assert.Equal(new ClipSelection(10, 90), history.Redo().Current);
    }

    [Fact]
    public void Cursor_IsKeptAcrossUndoAndRedo()
    {
        var history = SelectionHistory.Start(Initial, cursor: 7).Push(new ClipSelection(10, 90)).WithCursor(42);

        Assert.Equal(42, history.Undo().Cursor);
        Assert.Equal(42, history.Undo().Redo().Cursor);
    }
}
