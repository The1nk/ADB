using System.Collections.Generic;
using System.Globalization;
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

    [Fact]
    public void SameRowBackwardEdge_DipsBelowTheRow()
    {
        // Source and target on the same row (Y=100): a loop-back to a node directly to the left.
        // The cross-run must not drag along the row's centerline — it should dip below it.
        var path = ConnectionGeometry.BuildPath(
            new CanvasPoint(400, 100), PortEdge.Right, new CanvasPoint(40, 100), PortEdge.Left);

        Assert.Contains(" L ", path);
        Assert.DoesNotContain(" C ", path);
        Assert.True(MaxY(path) > 100, "back-route should dip below the level row, not run along it");
    }

    [Fact]
    public void BottomSourceBackwardEdge_StaysOrthogonal()
    {
        // Source leaves downward (Bottom port), target is left and below.
        var path = ConnectionGeometry.BuildPath(
            new CanvasPoint(400, 100), PortEdge.Bottom, new CanvasPoint(40, 300), PortEdge.Left);

        Assert.Contains(" L ", path);
        Assert.DoesNotContain(" C ", path);
        Assert.StartsWith("M 400,100 ", path);
        // First stub drops straight down from the source: an x=400 point with y > 100.
        Assert.Contains(Coordinates(path), p => p.X == 400 && p.Y > 100);
    }

    private static double MaxY(string path)
    {
        double max = double.NegativeInfinity;
        foreach (var (_, y) in Coordinates(path))
            if (y > max) max = y;
        return max;
    }

    private static IEnumerable<(double X, double Y)> Coordinates(string path)
    {
        foreach (var token in path.Split(' '))
        {
            var comma = token.IndexOf(',');
            if (comma < 0) continue;
            if (double.TryParse(token[..comma], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                double.TryParse(token[(comma + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                yield return (x, y);
        }
    }
}
