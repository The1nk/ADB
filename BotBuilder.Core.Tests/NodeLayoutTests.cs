using Xunit;

namespace BotBuilder.Core.Tests;

public class NodeLayoutTests
{
    [Theory]
    [InlineData(1, 70)]
    [InlineData(2, 70)]
    [InlineData(3, 90)]   // 28 + (3-1)*20 + 2*11 = 90
    [InlineData(4, 110)]
    public void CardHeight_GrowsForThreeOrMoreRightPorts(int rightCount, double expected)
        => Assert.Equal(expected, NodeLayout.CardHeight(rightCount));

    [Fact]
    public void RightBlock_IsVerticallyCentered_AndSymmetric()
    {
        var h = NodeLayout.CardHeight(2);
        var a0 = NodeLayout.RightAnchor(0, 2, h);
        var a1 = NodeLayout.RightAnchor(1, 2, h);
        Assert.Equal(NodeLayout.CardWidth, a0.X);
        Assert.Equal(NodeLayout.PortSpacing, a1.Y - a0.Y);
        var centerY = NodeLayout.HeaderHeight + (h - NodeLayout.HeaderHeight) / 2;
        Assert.Equal(centerY, (a0.Y + a1.Y) / 2);
    }

    [Fact]
    public void SingleInput_LandsAtBodyCenter()
    {
        var h = NodeLayout.CardHeight(1);
        var inp = NodeLayout.LeftAnchor(0, 1, h);
        var centerY = NodeLayout.HeaderHeight + (h - NodeLayout.HeaderHeight) / 2;
        Assert.Equal(0, inp.X);
        Assert.Equal(centerY, inp.Y);
    }

    [Fact]
    public void BottomAnchor_SingleIsBottomCenter()
    {
        var h = NodeLayout.CardHeight(1);
        var b = NodeLayout.BottomAnchor(0, 1, h);
        Assert.Equal(NodeLayout.CardWidth / 2, b.X);
        Assert.Equal(h, b.Y);
    }

    [Fact]
    public void BottomAnchor_DistributesHorizontally()
    {
        var h = NodeLayout.CardHeight(1);
        var b0 = NodeLayout.BottomAnchor(0, 2, h);
        var b1 = NodeLayout.BottomAnchor(1, 2, h);
        Assert.True(b0.X < NodeLayout.CardWidth / 2 && b1.X > NodeLayout.CardWidth / 2);
        Assert.Equal(h, b0.Y);
    }

    [Theory]
    [InlineData(PortEdge.Left, -1, 0)]
    [InlineData(PortEdge.Right, 1, 0)]
    [InlineData(PortEdge.Bottom, 0, 1)]
    public void Outward_Normals(PortEdge edge, double nx, double ny)
    {
        var n = NodeLayout.Outward(edge);
        Assert.Equal(nx, n.X);
        Assert.Equal(ny, n.Y);
    }
}
