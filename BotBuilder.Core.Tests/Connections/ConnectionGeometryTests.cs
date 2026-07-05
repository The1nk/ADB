using BotBuilder.Core;
using BotBuilder.Core.Connections;
using Xunit;

namespace BotBuilder.Core.Tests.Connections;

public class ConnectionGeometryTests
{
    [Fact]
    public void RightOutput_TargetToRight_IsForward()
        => Assert.False(ConnectionGeometry.IsBackward(new CanvasPoint(0, 0), PortEdge.Right, new CanvasPoint(200, 0)));

    [Fact]
    public void RightOutput_TargetToLeft_IsBackward()
        => Assert.True(ConnectionGeometry.IsBackward(new CanvasPoint(200, 0), PortEdge.Right, new CanvasPoint(0, 0)));

    [Fact]
    public void LeftOutput_TargetToLeft_IsForward()  // flipped (serpentine) band
        => Assert.False(ConnectionGeometry.IsBackward(new CanvasPoint(200, 0), PortEdge.Left, new CanvasPoint(0, 0)));

    [Fact]
    public void LeftOutput_TargetToRight_IsBackward()
        => Assert.True(ConnectionGeometry.IsBackward(new CanvasPoint(0, 0), PortEdge.Left, new CanvasPoint(200, 0)));
}
