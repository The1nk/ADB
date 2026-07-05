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

    [Fact]
    public void BottomOutput_NotFlipped_TargetToLeft_IsBackward()
        => Assert.True(ConnectionGeometry.IsBackward(new CanvasPoint(200, 0), PortEdge.Bottom, new CanvasPoint(0, 0), sourceFlipped: false));

    [Fact]
    public void BottomOutput_NotFlipped_TargetToRight_IsForward()
        => Assert.False(ConnectionGeometry.IsBackward(new CanvasPoint(0, 0), PortEdge.Bottom, new CanvasPoint(200, 0), sourceFlipped: false));

    [Fact]
    public void BottomOutput_Flipped_TargetToLeft_IsForward()  // reversed band: forward flow runs leftward
        => Assert.False(ConnectionGeometry.IsBackward(new CanvasPoint(200, 0), PortEdge.Bottom, new CanvasPoint(0, 0), sourceFlipped: true));

    [Fact]
    public void BottomOutput_Flipped_TargetToRight_IsBackward()
        => Assert.True(ConnectionGeometry.IsBackward(new CanvasPoint(0, 0), PortEdge.Bottom, new CanvasPoint(200, 0), sourceFlipped: true));
}
