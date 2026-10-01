using QuickParrot.Core.Editing;

namespace QuickParrot.Core.Tests.Editing;

public class SelectionHistoryTests
{
    [Fact]
    public void PushThenUndo_RestoresThePreviousSelection()
    {
        var history = new SelectionHistory(new ClipSelection(0, 100));

        history.Push(new ClipSelection(10, 90));

        Assert.True(history.CanUndo);
        Assert.Equal(new ClipSelection(0, 100), history.Undo());
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Redo_AfterUndo_ReappliesTheStep()
    {
        var history = new SelectionHistory(new ClipSelection(0, 100));
        history.Push(new ClipSelection(10, 90));
        history.Undo();

        Assert.True(history.CanRedo);
        Assert.Equal(new ClipSelection(10, 90), history.Redo());
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Push_AfterUndo_ClearsTheRedoStack()
    {
        var history = new SelectionHistory(new ClipSelection(0, 100));
        history.Push(new ClipSelection(10, 90));
        history.Undo();

        history.Push(new ClipSelection(20, 80));

        Assert.False(history.CanRedo);
        Assert.Equal(new ClipSelection(20, 80), history.Current);
    }

    [Fact]
    public void Push_WithTheSameSelection_IsANoOp()
    {
        var history = new SelectionHistory(new ClipSelection(0, 100));

        history.Push(new ClipSelection(0, 100));

        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Undo_WithNothingToUndo_ReturnsNull()
    {
        var history = new SelectionHistory(new ClipSelection(0, 100));

        Assert.Null(history.Undo());
        Assert.Equal(new ClipSelection(0, 100), history.Current);
    }

    [Fact]
    public void Redo_WithNothingToRedo_ReturnsNull()
    {
        var history = new SelectionHistory(new ClipSelection(0, 100));

        Assert.Null(history.Redo());
    }

    [Fact]
    public void Capacity_DropsTheOldestEntryOnceExceeded()
    {
        var history = new SelectionHistory(new ClipSelection(0, 1), capacity: 2);

        history.Push(new ClipSelection(0, 2)); // past: [0,1]
        history.Push(new ClipSelection(0, 3)); // past: [0,1] [0,2]
        history.Push(new ClipSelection(0, 4)); // past: [0,2] [0,3] — [0,1] dropped

        Assert.Equal(new ClipSelection(0, 3), history.Undo());
        Assert.Equal(new ClipSelection(0, 2), history.Undo());
        Assert.Null(history.Undo());
    }

    [Fact]
    public void UndoRedo_RoundTrip_ThroughSeveralSteps()
    {
        var history = new SelectionHistory(new ClipSelection(0, 1));
        history.Push(new ClipSelection(0, 2));
        history.Push(new ClipSelection(0, 3));

        history.Undo();
        history.Undo();

        Assert.Equal(new ClipSelection(0, 2), history.Redo());
        Assert.Equal(new ClipSelection(0, 3), history.Redo());
        Assert.False(history.CanRedo);
    }
}
