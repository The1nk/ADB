using System.Globalization;

namespace BotBuilder.Core.Connections;

/// <summary>Pure functions for routing a connection as a direction-aware cubic bezier:
/// the curve leaves each endpoint along its port edge's outward normal.</summary>
public static class ConnectionGeometry
{
    private const double MinPull = 40;

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

    /// <summary>A WPF path mini-language string ("M .. C .. .. ..") in invariant culture.</summary>
    public static string BuildPath(CanvasPoint start, PortEdge startEdge, CanvasPoint end, PortEdge endEdge)
    {
        var (c1, c2) = ControlPoints(start, startEdge, end, endEdge);
        return string.Create(CultureInfo.InvariantCulture,
            $"M {start.X},{start.Y} C {c1.X},{c1.Y} {c2.X},{c2.Y} {end.X},{end.Y}");
    }
}
