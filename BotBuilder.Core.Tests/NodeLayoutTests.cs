using AdbCore.Actions.BuiltIn;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class NodeLayoutTests
{
    [Fact]
    public void InputAnchor_IsOnLeftEdge_OutputAnchor_OnRightEdge()
    {
        var input = NodeLayout.InputAnchor(0);
        var output = NodeLayout.OutputAnchor(0);

        Assert.Equal(0, input.X);
        Assert.Equal(NodeLayout.CardWidth, output.X);
        Assert.Equal(input.Y, output.Y);
    }

    [Fact]
    public void Anchors_StackVerticallyByIndex()
    {
        var a0 = NodeLayout.InputAnchor(0);
        var a1 = NodeLayout.InputAnchor(1);

        Assert.Equal(NodeLayout.PortSpacing, a1.Y - a0.Y);
    }

    [Fact]
    public void FromDefinition_AssignsPortAnchorOffsets()
    {
        var node = NodeViewModel.FromDefinition(new LogAction(), Guid.NewGuid(), "", 0, 0);

        Assert.Equal(NodeLayout.InputAnchor(0), node.InputPorts[0].AnchorOffset);
        Assert.Equal(NodeLayout.OutputAnchor(0), node.OutputPorts[0].AnchorOffset);
    }
}
