using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class BotEditorViewModelTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    [Fact]
    public void AddNode_AddsNodeAndMarksDirty()
    {
        var editor = NewEditor();

        var node = editor.AddNode("control.start", 5, 6);

        Assert.Contains(node, editor.Nodes);
        Assert.Equal("control.start", node.TypeKey);
        Assert.Equal(5, node.X);
        Assert.True(editor.IsDirty);
    }

    [Fact]
    public void MoveNode_UpdatesPositionAndDirty()
    {
        var editor = NewEditor();
        var node = editor.AddNode("control.start", 0, 0);

        editor.MoveNode(node, 40, 50);

        Assert.Equal(40, node.X);
        Assert.Equal(50, node.Y);
        Assert.True(editor.IsDirty);
    }

    [Fact]
    public void Select_SetsIsSelectedExclusively()
    {
        var editor = NewEditor();
        var a = editor.AddNode("control.start", 0, 0);
        var b = editor.AddNode("control.end", 0, 0);

        editor.Select(a);
        Assert.True(a.IsSelected);
        Assert.False(b.IsSelected);
        Assert.Same(a, editor.SelectedNode);

        editor.Select(b);
        Assert.False(a.IsSelected);
        Assert.True(b.IsSelected);
    }

    [Fact]
    public void New_ClearsNodesAndDirty()
    {
        var editor = NewEditor();
        editor.AddNode("control.start", 0, 0);

        editor.New();

        Assert.Empty(editor.Nodes);
        Assert.False(editor.IsDirty);
        Assert.Null(editor.SelectedNode);
    }

    [Fact]
    public void SaveThenOpen_RestoresNodes()
    {
        var editor = NewEditor();
        editor.BotName = "RoundTrip";
        editor.AddNode("control.start", 10, 20);
        editor.AddNode("data.log", 100, 60);
        var path = Path.Combine(Path.GetTempPath(), $"adb-m3a-{Guid.NewGuid():N}.bot");

        try
        {
            editor.Save(path);
            Assert.False(editor.IsDirty);

            var reopened = NewEditor();
            reopened.Open(path);

            Assert.Equal("RoundTrip", reopened.BotName);
            Assert.Equal(2, reopened.Nodes.Count);
            var startAgain = reopened.Nodes.Single(n => n.TypeKey == "control.start");
            Assert.Equal(10, startAgain.X);
            Assert.Equal(20, startAgain.Y);
            Assert.False(reopened.IsDirty);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
