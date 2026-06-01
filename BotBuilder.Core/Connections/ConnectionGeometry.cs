using System.Globalization;

namespace BotBuilder.Core.Connections;

/// <summary>Pure functions for routing a connection as a horizontal cubic bezier.</summary>
public static class ConnectionGeometry
{
    private const double MinHorizontalPull = 40;

    /// <summary>The two cubic control points for a curve from <paramref name="start"/> to <paramref name="end"/>.</summary>
    public static (CanvasPoint C1, CanvasPoint C2) ControlPoints(CanvasPoint start, CanvasPoint end)
    {
        var pull = Math.Max(MinHorizontalPull, Math.Abs(end.X - start.X) / 2);
        return (new CanvasPoint(start.X + pull, start.Y), new CanvasPoint(end.X - pull, end.Y));
    }

    /// <summary>A WPF path mini-language string ("M .. C .. .. ..") in invariant culture.</summary>
    public static string BuildPath(CanvasPoint start, CanvasPoint end)
    {
        var (c1, c2) = ControlPoints(start, end);
        return string.Create(CultureInfo.InvariantCulture,
            $"M {start.X},{start.Y} C {c1.X},{c1.Y} {c2.X},{c2.Y} {end.X},{end.Y}");
    }
}
