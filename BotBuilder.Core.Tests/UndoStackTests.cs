using BotBuilder.Core.Undo;
using Xunit;

namespace BotBuilder.Core.Tests;

public class UndoStackTests
{
    private sealed class Counter : IUndoableCommand
    {
        private readonly Action _inc;
        private readonly Action _dec;
        public Counter(Action inc, Action dec) { _inc = inc; _dec = dec; }
        public void Do() => _inc();
        public void Undo() => _dec();
    }

    [Fact]
    public void Execute_RunsDo_AndEnablesUndo()
    {
        var stack = new UndoStack();
        var n = 0;
        stack.Execute(new Counter(() => n++, () => n--));

        Assert.Equal(1, n);
        Assert.True(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void Undo_ThenRedo_RestoresState()
    {
        var stack = new UndoStack();
        var n = 0;
        stack.Execute(new Counter(() => n++, () => n--));

        stack.Undo();
        Assert.Equal(0, n);
        Assert.True(stack.CanRedo);

        stack.Redo();
        Assert.Equal(1, n);
    }

    [Fact]
    public void PushExecuted_DoesNotRunDo_ButIsUndoable()
    {
        var stack = new UndoStack();
        var n = 5;
        stack.PushExecuted(new Counter(() => n++, () => n--));

        Assert.Equal(5, n);
        Assert.True(stack.CanUndo);

        stack.Undo();
        Assert.Equal(4, n);
    }

    [Fact]
    public void NewExecute_ClearsRedo()
    {
        var stack = new UndoStack();
        var n = 0;
        stack.Execute(new Counter(() => n++, () => n--));
        stack.Undo();
        Assert.True(stack.CanRedo);

        stack.Execute(new Counter(() => n += 10, () => n -= 10));

        Assert.False(stack.CanRedo);
        Assert.Equal(10, n);
    }

    [Fact]
    public void Clear_ResetsBothStacks()
    {
        var stack = new UndoStack();
        stack.Execute(new Counter(() => { }, () => { }));

        stack.Clear();

        Assert.False(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }
}
