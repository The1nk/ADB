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
        var dx = Math.Abs(end.X - start.X);
        var dy = Math.Abs(end.Y - start.Y);
        // Scale each endpoint's pull by the delta ALONG THAT ENDPOINT'S OWN AXIS — horizontal ports
        // (Left/Right) by |dx|, vertical ports (Top/Bottom) by |dy| — so a near-vertical connector on
        // horizontal ports (or a near-horizontal one on vertical ports) stays tight instead of ballooning
        // out along the port normal. The MinPull floor keeps short connectors from collapsing.
        var startPull = Math.Max(MinPull, AxisDelta(startEdge, dx, dy) / 2);
        var endPull = Math.Max(MinPull, AxisDelta(endEdge, dx, dy) / 2);
        var s = NodeLayout.Outward(startEdge);
        var e = NodeLayout.Outward(endEdge);
        return (new CanvasPoint(start.X + s.X * startPull, start.Y + s.Y * startPull),
                new CanvasPoint(end.X + e.X * endPull, end.Y + e.Y * endPull));
    }

    /// <summary>The endpoint-distance along a port edge's own axis: |dx| for horizontal (Left/Right) ports,
    /// |dy| for vertical (Top/Bottom) ports.</summary>
    private static double AxisDelta(PortEdge edge, double dx, double dy) => edge switch
    {
        PortEdge.Top or PortEdge.Bottom => dy,
        _ => dx,
    };

    /// <summary>Whether a connection runs "backwards" relative to its source port's facing direction — i.e.
    /// a loop-back / return wire that must be routed through a gutter rather than drawn as a forward curve.
    /// A Right output faces right, so a target to its left is backward; a Left output (a flipped serpentine
    /// band) faces left, so a target to its right is backward. A Bottom (failure) port stays on the bottom
    /// edge regardless of flip, but the band's forward direction still reverses when flipped, so
    /// <paramref name="sourceFlipped"/> (the source node's <c>PortsFlipped</c>) picks which side is forward.</summary>
    public static bool IsBackward(CanvasPoint start, PortEdge startEdge, CanvasPoint end, bool sourceFlipped = false) => startEdge switch
    {
        PortEdge.Left => end.X > start.X + BackRouteMargin,
        PortEdge.Right => end.X < start.X - BackRouteMargin,
        PortEdge.Bottom => sourceFlipped ? end.X > start.X + BackRouteMargin : end.X < start.X - BackRouteMargin,
        // A Top output routes vertically (up into a Bottom input) — it is never a horizontal back-edge, so
        // a clean vertical band-turn drop renders as a forward bezier rather than an orthogonal gutter route.
        PortEdge.Top => false,
        _ => end.X < start.X - BackRouteMargin,
    };

    /// <summary>A WPF path mini-language string in invariant culture. Forward edges curve as a cubic
    /// bezier; an edge whose target sits to the left of its source (a wrap "return" wire or loop-back,
    /// which a bezier would drag straight back across the nodes between them) is routed orthogonally
    /// down into the gap between rows, across, and up into the target.</summary>
    public static string BuildPath(CanvasPoint start, PortEdge startEdge, CanvasPoint end, PortEdge endEdge, bool sourceFlipped = false)
    {
        if (IsBackward(start, startEdge, end, sourceFlipped))
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
        // Stub along each port's outward normal before turning into the corridor. For Left/Right ports the
        // normal is horizontal, so sy==start.Y / ey==end.Y and that first/last L segment is a harmless
        // zero-length point; for a Bottom failure port the stub steps down/up first.
        var sy = start.Y + s.Y * BackRoutePull;
        var ey = end.Y + e.Y * BackRoutePull;
        return string.Create(CultureInfo.InvariantCulture,
            $"M {start.X},{start.Y} L {start.X},{sy} L {rightCornerX},{sy} L {rightCornerX},{gutterY} " +
            $"L {leftCornerX},{gutterY} L {leftCornerX},{ey} L {end.X},{ey} L {end.X},{end.Y}");
    }
}
