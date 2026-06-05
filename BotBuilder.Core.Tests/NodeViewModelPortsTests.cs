using System.Collections.Specialized;
using System.Linq;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class NodeViewModelPortsTests
{
    private static NodeViewModel RunParallelNode()
    {
        var def = new RunParallelAction();
        return NodeViewModel.FromDefinition(def, System.Guid.NewGuid(), "Run Parallel", 0, 0);
    }

    [Fact]
    public void FromDefinition_RunParallel_StartsWithTwoBranchPorts()
    {
        var node = RunParallelNode();
        Assert.Equal(new[] { "branch1", "branch2" }, node.OutputPorts.Select(p => p.Name));
    }

    [Fact]
    public void SetBranchPortCount_Grows_PreservingExistingInstances()
    {
        var node = RunParallelNode();
        var first = node.OutputPorts[0];

        node.SetBranchPortCount(4);

        Assert.Equal(new[] { "branch1", "branch2", "branch3", "branch4" }, node.OutputPorts.Select(p => p.Name));
        Assert.Same(first, node.OutputPorts[0]);
    }

    [Fact]
    public void SetBranchPortCount_Shrinks_DropsTrailing()
    {
        var node = RunParallelNode();
        node.SetBranchPortCount(5);
        var second = node.OutputPorts[1];

        node.SetBranchPortCount(2);

        Assert.Equal(new[] { "branch1", "branch2" }, node.OutputPorts.Select(p => p.Name));
        Assert.Same(second, node.OutputPorts[1]);
    }

    [Fact]
    public void OutputPorts_IsObservable()
    {
        var node = RunParallelNode();
        var changed = false;
        node.OutputPorts.CollectionChanged += (_, _) => changed = true;

        node.SetBranchPortCount(3);

        Assert.True(changed);
    }

    [Fact]
    public void ReplaceOutputPorts_SwapsContents()
    {
        var node = RunParallelNode();
        var replacement = RunParallelAction.OutputPortsForBranches(3)
            .Select((p, i) => new PortViewModel(p.Name, PortDirection.Out, NodeLayout.RightAnchor(i, 3, NodeLayout.CardHeight(3))))
            .ToList();

        node.ReplaceOutputPorts(replacement);

        Assert.Equal(new[] { "branch1", "branch2", "branch3" }, node.OutputPorts.Select(p => p.Name));
    }
}
