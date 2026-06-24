using BotBuilder.Core;
using BotBuilder.Core.Connections;
using Xunit;

namespace BotBuilder.Core.Tests;

public class ConnectionGeometryTests
{
    [Fact]
    public void BuildPath_ProducesInvariantCultureBezierString()
    {
        var path = ConnectionGeometry.BuildPath(
            new CanvasPoint(0, 0), PortEdge.Right, new CanvasPoint(100, 50), PortEdge.Left);

        Assert.StartsWith("M 0,0 C ", path);
        Assert.Contains(",", path);
        Assert.DoesNotContain(";", path);
        Assert.Equal(path, path.Trim());
    }

    [Fact]
    public void RightSource_PullsHorizontally_BackCompat()
    {
        var (c1, _) = ConnectionGeometry.ControlPoints(new(160, 50), PortEdge.Right, new(300, 90), PortEdge.Left);
        Assert.True(c1.X > 160);
        Assert.Equal(50, c1.Y);          // horizontal tangent (Y unchanged) — same as before
    }

    [Fact]
    public void LeftTarget_PullsBackToTheLeft()
    {
        var (_, c2) = ConnectionGeometry.ControlPoints(new(160, 50), PortEdge.Right, new(300, 90), PortEdge.Left);
        Assert.True(c2.X < 300);
        Assert.Equal(90, c2.Y);
    }

    [Fact]
    public void BottomSource_PullsDownward()
    {
        var (c1, _) = ConnectionGeometry.ControlPoints(new(80, 70), PortEdge.Bottom, new(80, 220), PortEdge.Left);
        Assert.Equal(80, c1.X);          // vertical tangent (X unchanged)
        Assert.True(c1.Y > 70);          // pulls down
    }

    [Fact]
    public void BackwardEdge_RoutesOrthogonally()
    {
        // Target is left of (and below) the source: a wrap-return / loop-back wire.
        var path = ConnectionGeometry.BuildPath(
            new CanvasPoint(400, 100), PortEdge.Right, new CanvasPoint(40, 300), PortEdge.Left);

        Assert.Contains(" L ", path);          // orthogonal line segments, not a single curve
        Assert.DoesNotContain(" C ", path);    // no bezier for back-routed wires
        Assert.StartsWith("M 400,100 ", path);
    }

    [Fact]
    public void ForwardEdge_StillUsesBezier()
    {
        var path = ConnectionGeometry.BuildPath(
            new CanvasPoint(40, 100), PortEdge.Right, new CanvasPoint(400, 100), PortEdge.Left);

        Assert.Contains(" C ", path);
        Assert.DoesNotContain(" L ", path);
    }
}
