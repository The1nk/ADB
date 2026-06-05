using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using BotBuilder.Core;
using BotBuilder.Core.Targets;
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

    private static TargetViewModel SeedTarget(BotEditorViewModel editor, BotTargetType type)
    {
        var target = editor.TargetBar.AddTarget();
        target.Type = type;
        return target;
    }

    [Fact]
    public void AddNode_OneMatchingTypeTarget_AutoAssigns()
    {
        var editor = NewEditor();
        var android = SeedTarget(editor, BotTargetType.AndroidDevice);

        var node = editor.AddNode("android.findImage", 10, 10);

        Assert.Equal(android.Id, node.TargetId);
    }

    [Fact]
    public void AddNode_TwoMatchingTargets_LeavesUnassigned()
    {
        var editor = NewEditor();
        SeedTarget(editor, BotTargetType.AndroidDevice);
        SeedTarget(editor, BotTargetType.AndroidDevice);

        var node = editor.AddNode("android.findImage", 10, 10);

        Assert.Null(node.TargetId);
    }

    [Fact]
    public void AddNode_TargetAgnosticNode_NeverAssigns()
    {
        var editor = NewEditor();
        SeedTarget(editor, BotTargetType.AndroidDevice);

        var node = editor.AddNode("control.branch", 10, 10);

        Assert.Null(node.TargetId);
    }

    [Fact]
    public void AddNode_WindowNodeWithLoneWindowTarget_AutoAssigns()
    {
        var editor = NewEditor();
        var win = SeedTarget(editor, BotTargetType.Window);

        var node = editor.AddNode("input.click", 10, 10);

        Assert.Equal(win.Id, node.TargetId);
    }

    [Fact]
    public void AddNode_TypeMismatch_LeavesUnassigned()
    {
        var editor = NewEditor();
        SeedTarget(editor, BotTargetType.AndroidDevice);          // only an Android target
        var node = editor.AddNode("input.click", 10, 10);          // a Window-category node
        Assert.Null(node.TargetId);
    }

    [Fact]
    public void AddNode_ScreenNodeWithLoneWindowTarget_AutoAssigns()
    {
        var editor = NewEditor();
        var win = SeedTarget(editor, BotTargetType.Window);
        var node = editor.AddNode("screen.findImage", 10, 10);
        Assert.Equal(win.Id, node.TargetId);
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
