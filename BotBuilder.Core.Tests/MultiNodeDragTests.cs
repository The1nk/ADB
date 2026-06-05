using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class MultiNodeDragTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    [Fact]
    public void CommitMoves_MovesAllSelected_AsSingleUndoStep()
    {
        var editor = NewEditor();
        var a = editor.AddNode("control.start", 0, 0);
        var b = editor.AddNode("data.log", 100, 0);

        // Simulate a multi-node drag: both already moved live to their new positions, then committed together.
        var starts = new (NodeViewModel, double, double)[] { (a, a.X, a.Y), (b, b.X, b.Y) };
        editor.MoveNode(a, 50, 30);
        editor.MoveNode(b, 150, 30);
        editor.CommitMoves(starts);

        Assert.Equal(50, a.X);
        Assert.Equal(30, a.Y);
        Assert.Equal(150, b.X);
        Assert.Equal(30, b.Y);

        editor.Undo();   // ONE undo restores BOTH

        Assert.Equal(0, a.X);
        Assert.Equal(0, a.Y);
        Assert.Equal(100, b.X);
        Assert.Equal(0, b.Y);
    }

    [Fact]
    public void CommitMoves_NoActualMovement_IsNoOp()
    {
        var editor = NewEditor();
        var a = editor.AddNode("control.start", 5, 7);
        editor.CommitMoves(new (NodeViewModel, double, double)[] { (a, a.X, a.Y) });
        Assert.Equal(5, a.X);
        Assert.Equal(7, a.Y);
    }
}
