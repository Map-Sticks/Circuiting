using Circuiting.ViewModels;
using Xunit;

namespace Circuiting.Tests;

public class CircuitEditHistoryTests
{
    [Fact]
    public void UndoRedoAndNewEditsMaintainTheCorrectSnapshots()
    {
        var history = new CircuitEditHistory();
        history.Push("first");
        history.Push("second");

        Assert.Equal("second", history.Undo("third"));
        Assert.Equal("third", history.Redo("second"));
        Assert.Equal("second", history.Undo("third"));
        history.Push("second");
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void UnrelatedModelChangesPreserveHistoryButCircuitChangesInvalidateIt()
    {
        var history = new CircuitEditHistory();
        history.Push("previous");
        history.ClearIfChanged("same", "same");
        Assert.True(history.CanUndo);

        history.ClearIfChanged("same", "changed");
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void HistoryKeepsTheFiftyMostRecentSnapshotsAndRestoresTheirOrder()
    {
        var history = new CircuitEditHistory();
        for (int i = 0; i < 55; i++) history.Push(i.ToString());
        Assert.Equal(50, history.UndoCount);

        var state = new DocumentSessionState
        {
            UndoTopFirst = history.UndoTopFirst.ToArray(),
            RedoTopFirst = new[] { "next", "later" }
        };
        var restored = new CircuitEditHistory();
        restored.Restore(state);
        Assert.Equal("54", restored.Undo("current"));
        restored.Restore(state);
        Assert.Equal("next", restored.Redo("current"));
        Assert.Equal("5", restored.UndoTopFirst.Last());
    }
}
