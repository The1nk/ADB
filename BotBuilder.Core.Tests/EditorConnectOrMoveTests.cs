using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using BotBuilder.Core.Connections;
using Xunit;

namespace BotBuilder.Core.Tests;

public class EditorConnectOrMoveTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    private static NodeViewModel Log(BotEditorViewModel e, double x = 0) => e.AddNode("data.log", x, 0);

    private static ConnectionViewModel? Edge(BotEditorViewModel e, NodeViewModel from, NodeViewModel to)
        => e.Connections.FirstOrDefault(c => ReferenceEquals(c.Source, from) && ReferenceEquals(c.Target, to));

    [Fact]
    public void Occupied_DropOnSingleUnsetOutNode_InsertsIntoTheWire()
    {
        var e = NewEditor();
        var a = Log(e, 0); var b = Log(e, 200); var c = Log(e, 400);
        e.Connect(a, a.OutputPorts[0], c, c.InputPorts[0]); // A -> C

        var r = e.ConnectOrMove(a, a.OutputPorts[0], b, b.InputPorts[0]); // drag A's out onto B

        Assert.Equal(ConnectionError.None, r);
        Assert.Equal(2, e.Connections.Count);
        Assert.NotNull(Edge(e, a, b));
        Assert.NotNull(Edge(e, b, c));
        Assert.Null(Edge(e, a, c));
    }

    [Fact]
    public void Insert_IsOneUndoableStep()
    {
        var e = NewEditor();
        var a = Log(e, 0); var b = Log(e, 200); var c = Log(e, 400);
        e.Connect(a, a.OutputPorts[0], c, c.InputPorts[0]);

        e.ConnectOrMove(a, a.OutputPorts[0], b, b.InputPorts[0]);
        e.Undo();

        Assert.Single(e.Connections);
        Assert.NotNull(Edge(e, a, c));

        e.Redo();
        Assert.Equal(2, e.Connections.Count);
        Assert.NotNull(Edge(e, a, b));
        Assert.NotNull(Edge(e, b, c));
    }

    [Fact]
    public void Occupied_DropOnNodeWhoseOutIsWired_MovesOnly_OrphansOldTarget()
    {
        var e = NewEditor();
        var a = Log(e, 0); var b = Log(e, 200); var c = Log(e, 400); var d = Log(e, 600);
        e.Connect(a, a.OutputPorts[0], c, c.InputPorts[0]);
        e.Connect(b, b.OutputPorts[0], d, d.InputPorts[0]);

        var r = e.ConnectOrMove(a, a.OutputPorts[0], b, b.InputPorts[0]);

        Assert.Equal(ConnectionError.None, r);
        Assert.NotNull(Edge(e, a, b));
        Assert.NotNull(Edge(e, b, d));
        Assert.Null(Edge(e, a, c));
        Assert.Null(Edge(e, b, c));
        Assert.Equal(2, e.Connections.Count);
    }

    [Fact]
    public void Occupied_DropOnMultiOutNode_MovesOnly_NoForward()
    {
        var e = NewEditor();
        var a = Log(e, 0); var c = Log(e, 400);
        var branch = e.AddNode("control.branch", 200, 0);
        e.Connect(a, a.OutputPorts[0], c, c.InputPorts[0]);
        Assert.True(branch.OutputPorts.Count >= 2);

        var r = e.ConnectOrMove(a, a.OutputPorts[0], branch, branch.InputPorts[0]);

        Assert.Equal(ConnectionError.None, r);
        Assert.NotNull(Edge(e, a, branch));
        Assert.Null(Edge(e, a, c));
        Assert.Single(e.Connections);
    }

    [Fact]
    public void Occupied_DropOnOldTarget_IsNoOp_Duplicate()
    {
        var e = NewEditor();
        var a = Log(e, 0); var c = Log(e, 400);
        e.Connect(a, a.OutputPorts[0], c, c.InputPorts[0]);

        var r = e.ConnectOrMove(a, a.OutputPorts[0], c, c.InputPorts[0]);

        Assert.Equal(ConnectionError.Duplicate, r);
        Assert.Single(e.Connections);
        Assert.NotNull(Edge(e, a, c));
    }

    [Fact]
    public void Occupied_DropOnSelf_IsNoOp()
    {
        var e = NewEditor();
        var a = Log(e, 0); var c = Log(e, 400);
        e.Connect(a, a.OutputPorts[0], c, c.InputPorts[0]);

        var r = e.ConnectOrMove(a, a.OutputPorts[0], a, a.InputPorts[0]);

        Assert.Equal(ConnectionError.SelfConnection, r);
        Assert.Single(e.Connections);
        Assert.NotNull(Edge(e, a, c));
    }

    [Fact]
    public void Occupied_MoveThatWouldCycle_IsNoOp()
    {
        var e = NewEditor();
        var a = Log(e, 0); var b = Log(e, 200); var c = Log(e, 400);
        e.Connect(b, b.OutputPorts[0], a, a.InputPorts[0]);
        e.Connect(a, a.OutputPorts[0], c, c.InputPorts[0]);

        var r = e.ConnectOrMove(a, a.OutputPorts[0], b, b.InputPorts[0]);

        Assert.Equal(ConnectionError.WouldCreateCycle, r);
        Assert.NotNull(Edge(e, a, c));
        Assert.NotNull(Edge(e, b, a));
        Assert.Equal(2, e.Connections.Count);
    }

    [Fact]
    public void Occupied_AutoForwardSkipped_WhenItWouldCycle()
    {
        var e = NewEditor();
        var a = Log(e, 0); var b = Log(e, 200); var c = Log(e, 400);
        e.Connect(a, a.OutputPorts[0], c, c.InputPorts[0]);
        e.Connect(c, c.OutputPorts[0], b, b.InputPorts[0]);

        var r = e.ConnectOrMove(a, a.OutputPorts[0], b, b.InputPorts[0]);

        Assert.Equal(ConnectionError.None, r);
        Assert.NotNull(Edge(e, a, b));
        Assert.NotNull(Edge(e, c, b));
        Assert.Null(Edge(e, b, c));
        Assert.Equal(2, e.Connections.Count);
    }

    [Fact]
    public void Unoccupied_DelegatesToConnect()
    {
        var e = NewEditor();
        var a = Log(e, 0); var b = Log(e, 200);

        var r = e.ConnectOrMove(a, a.OutputPorts[0], b, b.InputPorts[0]);

        Assert.Equal(ConnectionError.None, r);
        Assert.Single(e.Connections);
        Assert.NotNull(Edge(e, a, b));
    }
}
