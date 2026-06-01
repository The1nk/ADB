using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using BotBuilder.Core.Connections;
using Xunit;

namespace BotBuilder.Core.Tests;

public class EditorConnectionsTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    private static (NodeViewModel a, NodeViewModel b) TwoNodes(BotEditorViewModel e)
        => (e.AddNode("control.start", 0, 0), e.AddNode("data.log", 200, 0));

    [Fact]
    public void Connect_Valid_AddsConnection_AndIsUndoable()
    {
        var e = NewEditor();
        var (a, b) = TwoNodes(e);

        var result = e.Connect(a, a.OutputPorts[0], b, b.InputPorts[0]);

        Assert.Equal(ConnectionError.None, result);
        Assert.Single(e.Connections);

        e.Undo();
        Assert.Empty(e.Connections);
        e.Redo();
        Assert.Single(e.Connections);
    }

    [Fact]
    public void Connect_Invalid_DoesNotAdd_AndReturnsReason()
    {
        var e = NewEditor();
        var (a, b) = TwoNodes(e);

        var result = e.Connect(a, a.OutputPorts[0], b, b.OutputPorts[0]);

        Assert.Equal(ConnectionError.NotOutputToInput, result);
        Assert.Empty(e.Connections);
    }

    [Fact]
    public void DeleteNode_CascadesConnections_AndUndoRestoresThem()
    {
        var e = NewEditor();
        var (a, b) = TwoNodes(e);
        e.Connect(a, a.OutputPorts[0], b, b.InputPorts[0]);

        e.DeleteNode(a);
        Assert.DoesNotContain(a, e.Nodes);
        Assert.Empty(e.Connections);

        e.Undo();
        Assert.Contains(a, e.Nodes);
        Assert.Single(e.Connections);
    }

    [Fact]
    public void Disconnect_RemovesConnection_AndUndoRestores()
    {
        var e = NewEditor();
        var (a, b) = TwoNodes(e);
        e.Connect(a, a.OutputPorts[0], b, b.InputPorts[0]);
        var conn = e.Connections[0];

        e.Disconnect(conn);
        Assert.Empty(e.Connections);

        e.Undo();
        Assert.Single(e.Connections);
    }

    [Fact]
    public void MoveThenUndo_RestoresOldPosition()
    {
        var e = NewEditor();
        var node = e.AddNode("control.start", 10, 10);

        e.MoveNode(node, 99, 99);
        e.CommitMove(node, 10, 10);

        e.Undo();
        Assert.Equal(10, node.X);
        Assert.Equal(10, node.Y);
        e.Redo();
        Assert.Equal(99, node.X);
    }

    [Fact]
    public void DeleteSelection_RemovesSelectedConnectionElseSelectedNode()
    {
        var e = NewEditor();
        var (a, b) = TwoNodes(e);
        e.Connect(a, a.OutputPorts[0], b, b.InputPorts[0]);

        e.SelectConnection(e.Connections[0]);
        e.DeleteSelection();
        Assert.Empty(e.Connections);
        Assert.Equal(2, e.Nodes.Count);

        e.Select(a);
        e.DeleteSelection();
        Assert.DoesNotContain(a, e.Nodes);
    }

    [Fact]
    public void Selecting_Node_And_Connection_AreMutuallyExclusive()
    {
        var e = NewEditor();
        var (a, b) = TwoNodes(e);
        e.Connect(a, a.OutputPorts[0], b, b.InputPorts[0]);
        var conn = e.Connections[0];

        e.Select(a);
        Assert.Null(e.SelectedConnection);

        e.SelectConnection(conn);
        Assert.Null(e.SelectedNode);
        Assert.False(a.IsSelected);
        Assert.True(conn.IsSelected);
    }

    [Fact]
    public void SaveOpen_RoundTripsConnections()
    {
        var e = NewEditor();
        var (a, b) = TwoNodes(e);
        e.Connect(a, a.OutputPorts[0], b, b.InputPorts[0]);
        var path = Path.Combine(Path.GetTempPath(), $"adb-m3b-{Guid.NewGuid():N}.bot");

        try
        {
            e.Save(path);
            var reopened = NewEditor();
            reopened.Open(path);

            Assert.Equal(2, reopened.Nodes.Count);
            Assert.Single(reopened.Connections);
            var c = reopened.Connections[0];
            Assert.Equal("control.start", c.Source.TypeKey);
            Assert.Equal("data.log", c.Target.TypeKey);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Disconnect_ThenUndo_LeavesConnectionLiveToNodeMoves()
    {
        var e = NewEditor();
        var (a, b) = TwoNodes(e);
        e.Connect(a, a.OutputPorts[0], b, b.InputPorts[0]);
        e.Disconnect(e.Connections[0]);
        e.Undo(); // restores the connection

        var conn = e.Connections[0];
        var raised = new List<string?>();
        ((System.ComponentModel.INotifyPropertyChanged)conn).PropertyChanged += (_, ev) => raised.Add(ev.PropertyName);

        a.X += 40; // move a node the restored connection is attached to

        Assert.Contains(nameof(BotBuilder.Core.Connections.ConnectionViewModel.PathData), raised);
    }

    [Fact]
    public void Undo_WithNothingToUndo_DoesNotMarkDirty()
    {
        var e = NewEditor(); // New() leaves IsDirty == false
        Assert.False(e.IsDirty);

        e.Undo(); // nothing on the stack

        Assert.False(e.IsDirty);
    }
}
