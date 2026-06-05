using System.Linq;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using BotBuilder.Core.Connections;
using Xunit;

namespace BotBuilder.Core.Tests;

public class BranchCountTests
{
    private static BotEditorViewModel Editor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    private static NodeViewModel AddRunParallel(BotEditorViewModel e) => e.AddNode(RunParallelAction.RunParallelTypeKey, 0, 0);
    private static NodeViewModel AddEnd(BotEditorViewModel e, double y) => e.AddNode("control.end", 200, y);
    private static void SetBranches(NodeViewModel node, int n) => node.Config[RunParallelAction.BranchesKey] = n;

    [Fact]
    public void Grow_AddsBranchPorts()
    {
        var e = Editor();
        var rp = AddRunParallel(e);
        SetBranches(rp, 4);

        e.OnBranchCountChanged(rp);

        Assert.Equal(new[] { "branch1", "branch2", "branch3", "branch4" }, rp.OutputPorts.Select(p => p.Name));
    }

    [Fact]
    public void Grow_ThenUndo_RestoresTwoPorts()
    {
        var e = Editor();
        var rp = AddRunParallel(e);
        SetBranches(rp, 4);
        e.OnBranchCountChanged(rp);

        e.Undo();

        Assert.Equal(2, rp.OutputPorts.Count);
    }

    [Fact]
    public void Shrink_DeletesOrphanedConnections_Undoable()
    {
        var e = Editor();
        var rp = AddRunParallel(e);
        SetBranches(rp, 3);
        e.OnBranchCountChanged(rp);

        var end = AddEnd(e, 100);
        var branch3 = rp.OutputPorts.Single(p => p.Name == "branch3");
        var endIn = end.InputPorts[0];
        Assert.Equal(ConnectionError.None, e.Connect(rp, branch3, end, endIn));
        Assert.Single(e.Connections);

        SetBranches(rp, 2);
        e.OnBranchCountChanged(rp);
        Assert.Equal(2, rp.OutputPorts.Count);
        Assert.Empty(e.Connections);

        e.Undo();
        Assert.Equal(3, rp.OutputPorts.Count);
        Assert.Single(e.Connections);
        Assert.Same(branch3, e.Connections[0].SourcePort);
    }

    [Fact]
    public void ClampsToMinimumTwo()
    {
        var e = Editor();
        var rp = AddRunParallel(e);
        SetBranches(rp, 1);

        e.OnBranchCountChanged(rp);

        Assert.Equal(2, rp.OutputPorts.Count);
    }
}
