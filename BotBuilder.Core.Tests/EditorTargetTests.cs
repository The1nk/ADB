using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class EditorTargetTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    [Fact]
    public void Editor_ExposesATargetBar()
    {
        Assert.NotNull(NewEditor().TargetBar);
    }

    [Fact]
    public void SingleTarget_NoBadges()
    {
        var e = NewEditor();
        var node = e.AddNode("control.start", 0, 0);
        e.TargetBar.AddTarget();

        Assert.Null(node.TargetBadge);
    }

    [Fact]
    public void MultipleTargets_UnassignedNode_BadgesFirstTarget()
    {
        var e = NewEditor();
        var node = e.AddNode("control.start", 0, 0);
        var first = e.TargetBar.AddTarget();
        first.Name = "Client 1";
        e.TargetBar.AddTarget();

        Assert.Equal("Client 1", node.TargetBadge);
    }

    [Fact]
    public void AssignTarget_BadgesAssignedTargetName()
    {
        var e = NewEditor();
        var node = e.AddNode("control.start", 0, 0);
        e.TargetBar.AddTarget();
        var second = e.TargetBar.AddTarget();
        second.Name = "My Phone";

        e.AssignTarget(node, second.Id);

        Assert.Equal(second.Id, node.TargetId);
        Assert.Equal("My Phone", node.TargetBadge);
    }

    [Fact]
    public void RenamingTarget_UpdatesBadges()
    {
        var e = NewEditor();
        var node = e.AddNode("control.start", 0, 0);
        var first = e.TargetBar.AddTarget();
        e.TargetBar.AddTarget();

        first.Name = "Renamed First";

        Assert.Equal("Renamed First", node.TargetBadge);
    }

    [Fact]
    public void RemovingDownToOneTarget_ClearsBadges()
    {
        var e = NewEditor();
        var node = e.AddNode("control.start", 0, 0);
        var a = e.TargetBar.AddTarget();
        var b = e.TargetBar.AddTarget();
        Assert.NotNull(node.TargetBadge);

        e.TargetBar.RemoveTarget(b);

        Assert.Null(node.TargetBadge);
    }

    [Fact]
    public void AddingNode_WithMultipleTargets_ImmediatelyShowsBadge()
    {
        var e = NewEditor();
        var first = e.TargetBar.AddTarget();
        first.Name = "Client 1";
        e.TargetBar.AddTarget();

        var node = e.AddNode("control.start", 0, 0); // added AFTER targets exist

        Assert.Equal("Client 1", node.TargetBadge);
    }

    [Fact]
    public void New_ClearsTargets()
    {
        var e = NewEditor();
        e.TargetBar.AddTarget();
        e.TargetBar.AddTarget();

        e.New();

        Assert.Empty(e.TargetBar.Targets);
    }
}
