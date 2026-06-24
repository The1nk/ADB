namespace BotBuilder.Core.Connections;

/// <summary>A candidate connection for back-route planning: absolute source/target anchor positions.</summary>
public readonly record struct BackRouteInput(Guid Id, double StartX, double StartY, double EndX, double EndY);

/// <summary>The lane a back-route was assigned: the vertical corridor X on each side and the gutter Y for
/// its horizontal run. The corridors are distinct per route (monotonic in lane index) and carry the
/// separation guarantee — the gutter Y is a routing convenience, not relied on for non-overlap.</summary>
public readonly record struct BackRoutePlan(double RightCornerX, double LeftCornerX, double GutterY);

/// <summary>Assigns each backward connection (target left of source) its own nested lane: a right-side
/// corridor, a left-side corridor, and a gutter row, so return/loop wires never lie on top of each other.
/// Pure and deterministic — narrower spans nest inside wider ones.</summary>
public static class BackRoutePlanner
{
    public const double Margin = 40;       // gap from the node block to the first corridor
    public const double LaneGap = 18;      // horizontal spacing between corridors
    public const double GutterStep = 16;   // vertical spacing between gutter rows

    public static IReadOnlyDictionary<Guid, BackRoutePlan> Plan(
        IReadOnlyList<BackRouteInput> routes, double nodesLeftX, double nodesRightX)
    {
        var result = new Dictionary<Guid, BackRoutePlan>();

        // Backward edges only (target strictly left of source), ordered narrowest-span first so the
        // narrowest nests in the innermost lane and wider spans wrap around the outside.
        var back = routes
            .Where(r => r.EndX < r.StartX)
            .OrderBy(r => r.StartX - r.EndX)
            .ThenBy(r => r.StartY)
            .ThenBy(r => r.Id)
            .ToList();

        for (var i = 0; i < back.Count; i++)
        {
            var r = back[i];
            var rightX = nodesRightX + Margin + i * LaneGap;
            var leftX = nodesLeftX - Margin - i * LaneGap;
            // base gutter midway between the two rows, then a per-lane step so equal-row pairs separate.
            var gutterY = (r.StartY + r.EndY) / 2 + i * GutterStep;
            result[r.Id] = new BackRoutePlan(rightX, leftX, gutterY);
        }

        return result;
    }
}
