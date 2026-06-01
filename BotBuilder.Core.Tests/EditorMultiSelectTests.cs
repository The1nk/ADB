using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class EditorMultiSelectTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    [Fact]
    public void Editor_ExposesAViewport()
    {
        Assert.NotNull(NewEditor().Viewport);
    }

    [Fact]
    public void SelectNodes_MarksOnlyThoseSelected()
    {
        var e = NewEditor();
        var a = e.AddNode("control.start", 0, 0);
        var b = e.AddNode("data.log", 0, 0);
        var c = e.AddNode("control.end", 0, 0);

        e.SelectNodes(new[] { a, c });

        Assert.True(a.IsSelected);
        Assert.False(b.IsSelected);
        Assert.True(c.IsSelected);
    }

    [Fact]
    public void SelectNodes_ClearsConnectionSelection()
    {
        var e = NewEditor();
        var a = e.AddNode("control.start", 0, 0);
        var b = e.AddNode("data.log", 0, 0);
        e.Connect(a, a.OutputPorts[0], b, b.InputPorts[0]);
        e.SelectConnection(e.Connections[0]);

        e.SelectNodes(new[] { a });

        Assert.Null(e.SelectedConnection);
        Assert.False(e.Connections[0].IsSelected);
    }

    [Fact]
    public void DeleteSelection_DeletesAllSelectedNodes_AsOneUndo()
    {
        var e = NewEditor();
        var a = e.AddNode("control.start", 0, 0);
        var b = e.AddNode("data.log", 0, 0);
        var c = e.AddNode("control.end", 0, 0);
        e.Connect(a, a.OutputPorts[0], b, b.InputPorts[0]);

        e.SelectNodes(new[] { a, b });
        e.DeleteSelection();

        Assert.DoesNotContain(a, e.Nodes);
        Assert.DoesNotContain(b, e.Nodes);
        Assert.Contains(c, e.Nodes);
        Assert.Empty(e.Connections);

        e.Undo();
        Assert.Contains(a, e.Nodes);
        Assert.Contains(b, e.Nodes);
        Assert.Single(e.Connections);
    }

    [Fact]
    public void DeleteSelection_NothingSelected_IsNoOp()
    {
        var e = NewEditor();
        e.AddNode("control.start", 0, 0);

        e.DeleteSelection();

        Assert.Single(e.Nodes);
    }
}
