using System.Globalization;

namespace BotBuilder.Core.Connections;

/// <summary>Pure functions for routing a connection as a direction-aware cubic bezier:
/// the curve leaves each endpoint along its port edge's outward normal.</summary>
public static class ConnectionGeometry
{
    private const double MinPull = 40;
    private const double BackRouteMargin = 1;   // target must be clearly left of source to back-route
    private const double BackRoutePull = 24;    // short stub off each port before turning into the gutter
    private const double RowClearance = 48;     // ~half a default node card + margin: drops the gutter clear of level rows

    /// <summary>The two cubic control points for a curve from <paramref name="start"/> (leaving along
    /// <paramref name="startEdge"/>) to <paramref name="end"/> (approaching along <paramref name="endEdge"/>).</summary>
    public static (CanvasPoint C1, CanvasPoint C2) ControlPoints(
        CanvasPoint start, PortEdge startEdge, CanvasPoint end, PortEdge endEdge)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var pull = Math.Max(MinPull, Math.Sqrt(dx * dx + dy * dy) / 2);
        var s = NodeLayout.Outward(startEdge);
        var e = NodeLayout.Outward(endEdge);
        return (new CanvasPoint(start.X + s.X * pull, start.Y + s.Y * pull),
                new CanvasPoint(end.X + e.X * pull, end.Y + e.Y * pull));
    }

    /// <summary>A WPF path mini-language string in invariant culture. Forward edges curve as a cubic
    /// bezier; an edge whose target sits to the left of its source (a wrap "return" wire or loop-back,
    /// which a bezier would drag straight back across the nodes between them) is routed orthogonally
    /// down into the gap between rows, across, and up into the target.</summary>
    public static string BuildPath(CanvasPoint start, PortEdge startEdge, CanvasPoint end, PortEdge endEdge)
    {
        if (end.X < start.X - BackRouteMargin)
            return BuildBackRoute(start, startEdge, end, endEdge);

        var (c1, c2) = ControlPoints(start, startEdge, end, endEdge);
        return string.Create(CultureInfo.InvariantCulture,
            $"M {start.X},{start.Y} C {c1.X},{c1.Y} {c2.X},{c2.Y} {end.X},{end.Y}");
    }

    /// <summary>Orthogonal "down-across-up" route for a backward edge: leave the source along its port
    /// normal, drop to the mid-gutter between the two rows (clear of nodes), run across, then approach
    /// the target along its port normal. Uses only the two endpoints — no obstacle map.</summary>
    private static string BuildBackRoute(CanvasPoint start, PortEdge startEdge, CanvasPoint end, PortEdge endEdge)
    {
        var s = NodeLayout.Outward(startEdge);
        var e = NodeLayout.Outward(endEdge);
        var sx = start.X + s.X * BackRoutePull;
        var sy = start.Y + s.Y * BackRoutePull;
        var ex = end.X + e.X * BackRoutePull;
        var ey = end.Y + e.Y * BackRoutePull;
        var gutterY = (sy + ey) / 2;   // midway between source and target rows -> in the inter-band gap
        if (Math.Abs(ey - sy) < RowClearance * 2)        // rows are level / too close: midpoint would sit on a row
            gutterY = Math.Max(sy, ey) + RowClearance;   // drop the cross-run below both rows instead
        return string.Create(CultureInfo.InvariantCulture,
            $"M {start.X},{start.Y} L {sx},{sy} L {sx},{gutterY} L {ex},{gutterY} L {ex},{ey} L {end.X},{end.Y}");
    }

    /// <summary>Orthogonal back-route through an explicitly assigned lane: out along the source normal to
    /// the right corridor, down/up to the gutter row, across to the left corridor, then up/down into the
    /// target. The corridors and gutter come from <see cref="BackRoutePlanner"/> so parallel return wires
    /// never coincide.</summary>
    public static string BuildLanedBackRoute(
        CanvasPoint start, PortEdge startEdge, CanvasPoint end, PortEdge endEdge,
        double rightCornerX, double leftCornerX, double gutterY)
    {
        var s = NodeLayout.Outward(startEdge);
        var e = NodeLayout.Outward(endEdge);
        var sy = start.Y + s.Y * BackRoutePull;   // a short stub along the source normal (handles Bottom ports)
        var ey = end.Y + e.Y * BackRoutePull;
        return string.Create(CultureInfo.InvariantCulture,
            $"M {start.X},{start.Y} L {start.X},{sy} L {rightCornerX},{sy} L {rightCornerX},{gutterY} " +
            $"L {leftCornerX},{gutterY} L {leftCornerX},{ey} L {end.X},{ey} L {end.X},{end.Y}");
    }
}
