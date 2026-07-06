using System.Linq;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class NodeViewModelFlipTests
{
    private static NodeViewModel DelayNode()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        defs.TryGet("control.delay", out var def);
        return NodeViewModel.FromDefinition(def!, System.Guid.NewGuid(), "Delay", 0, 0);
    }

    [Fact]
    public void Default_InputsLeft_OutputsRight()
    {
        var node = DelayNode();
        Assert.All(node.InputPorts, p => Assert.Equal(PortEdge.Left, p.Edge));
        Assert.All(node.OutputPorts, p => Assert.Equal(PortEdge.Right, p.Edge));
        Assert.False(node.PortsFlipped);
    }

    [Fact]
    public void Flipped_InputsRight_OutputsLeft()
    {
        var node = DelayNode();
        node.SetPortsFlipped(true);

        Assert.True(node.PortsFlipped);
        Assert.All(node.InputPorts, p => Assert.Equal(PortEdge.Right, p.Edge));
        Assert.All(node.OutputPorts, p => Assert.Equal(PortEdge.Left, p.Edge));
        // Right-edge input anchor sits at the card's right edge (x == CardWidth).
        Assert.Equal(NodeLayout.CardWidth, node.InputPorts[0].AnchorOffset.X);
        Assert.Equal(0, node.OutputPorts[0].AnchorOffset.X);
    }

    [Fact]
    public void Flip_Then_Unflip_RestoresEdges()
    {
        var node = DelayNode();
        node.SetPortsFlipped(true);
        node.SetPortsFlipped(false);
        Assert.False(node.PortsFlipped);
        Assert.Equal(PortEdge.Left, node.InputPorts[0].Edge);
        Assert.Equal(PortEdge.Right, node.OutputPorts[0].Edge);
    }
}
