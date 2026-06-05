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
            .Select((p, i) => new PortViewModel(p.Name, PortDirection.Out, PortEdge.Right, NodeLayout.RightAnchor(i, 3, NodeLayout.CardHeight(3))))
            .ToList();

        node.ReplaceOutputPorts(replacement);

        Assert.Equal(new[] { "branch1", "branch2", "branch3" }, node.OutputPorts.Select(p => p.Name));
        Assert.Equal(NodeLayout.CardHeight(3), node.Height);
    }

    [Fact]
    public void FromDefinition_FailureOutput_GoesBottom_OtherOutputsRight_InputsLeft()
    {
        var def = new MathAction();
        var node = NodeViewModel.FromDefinition(def, System.Guid.NewGuid(), "", 0, 0);
        Assert.All(node.InputPorts, p => Assert.Equal(PortEdge.Left, p.Edge));
        var success = System.Linq.Enumerable.Single(node.OutputPorts, p => p.Name == "onSuccess");
        var failure = System.Linq.Enumerable.Single(node.OutputPorts, p => p.Name == "onFailure");
        Assert.Equal(PortEdge.Right, success.Edge);
        Assert.Equal(PortEdge.Bottom, failure.Edge);
    }

    [Fact]
    public void Join_SomeFailed_Bottom_AllSucceeded_Right()
    {
        var node = NodeViewModel.FromDefinition(new JoinAction(), System.Guid.NewGuid(), "", 0, 0);
        Assert.Equal(PortEdge.Bottom, System.Linq.Enumerable.Single(node.OutputPorts, p => p.Name == "someFailed").Edge);
        Assert.Equal(PortEdge.Right, System.Linq.Enumerable.Single(node.OutputPorts, p => p.Name == "allSucceeded").Edge);
    }

    [Fact]
    public void RunParallel_BranchesRight_CardGrows_AndReCenters()
    {
        var node = NodeViewModel.FromDefinition(new RunParallelAction(), System.Guid.NewGuid(), "", 0, 0);
        node.SetBranchPortCount(4);
        Assert.Equal(4, node.OutputPorts.Count);
        Assert.All(node.OutputPorts, p => Assert.Equal(PortEdge.Right, p.Edge));
        Assert.Equal(NodeLayout.CardHeight(4), node.Height);
        var ys = System.Linq.Enumerable.Select(node.OutputPorts, p => p.AnchorOffset.Y).ToList();
        var centerY = NodeLayout.HeaderHeight + (node.Height - NodeLayout.HeaderHeight) / 2;
        Assert.Equal(centerY, (ys[0] + ys[3]) / 2, 3);
        // input re-centers too
        Assert.Equal(centerY, node.InputPorts[0].AnchorOffset.Y, 3);
    }
}
