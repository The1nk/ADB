namespace BotBuilder.Core.Connections;

/// <summary>A candidate connection for back-route planning: absolute source/target anchor positions.</summary>
public readonly record struct BackRouteInput(Guid Id, double StartX, double StartY, double EndX, double EndY);

/// <summary>The lane a back-route was assigned: the vertical corridor X on each side and the gutter Y for
/// its horizontal run. The corridors are distinct per route (monotonic in lane index) and carry the
/// separation guarantee — the gutter Y is a routing convenience, not relied on for non-overlap.</summary>
public readonly record struct BackRoutePlan(double RightCornerX, double LeftCornerX, double GutterY);

/// <summary>Assigns each backward connection (target left of source) its own nested lane: a right-side
/// corridor, a left-side corridor, and a gutter row, so return/loop wires never lie on top of each other.
/// Pure and deterministic — narrower spans nest inside wider ones, and each gutter rides the clear gap
/// between node rows nearest its midpoint rather than cutting across a row.</summary>
public static class BackRoutePlanner
{
    public const double Margin = 40;       // gap from the node block to the first corridor
    public const double LaneGap = 18;      // horizontal spacing between corridors
    public const double GutterStep = 16;   // vertical spacing between gutter rows

    public static IReadOnlyDictionary<Guid, BackRoutePlan> Plan(
        IReadOnlyList<BackRouteInput> routes, double nodesLeftX, double nodesRightX,
        IReadOnlyList<(double Top, double Bottom)> clearBands)
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

        var perBand = new Dictionary<int, int>();   // how many routes already placed in each clear band
        for (var i = 0; i < back.Count; i++)
        {
            var r = back[i];
            var rightX = nodesRightX + Margin + i * LaneGap;
            var leftX = nodesLeftX - Margin - i * LaneGap;

            // Route the horizontal run through the clear gap between node rows nearest the route's
            // midpoint, so it never cuts through a row; stagger routes sharing a gap.
            var desiredY = (r.StartY + r.EndY) / 2;
            double gutterY;
            var bandIdx = NearestBand(clearBands, desiredY);
            if (bandIdx < 0)
            {
                gutterY = desiredY;   // no band info -> fall back to the midpoint
            }
            else
            {
                var band = clearBands[bandIdx];
                var local = perBand.TryGetValue(bandIdx, out var c) ? c : 0;
                perBand[bandIdx] = local + 1;
                var center = (band.Top + band.Bottom) / 2;
                var lo = band.Top + 2;
                var hi = band.Bottom - 2;
                gutterY = hi <= lo
                    ? (band.Top + band.Bottom) / 2          // band too thin to stagger -> use its center
                    : Math.Clamp(center + local * GutterStep, lo, hi);
            }
            result[r.Id] = new BackRoutePlan(rightX, leftX, gutterY);
        }

        return result;
    }

    /// <summary>The index of the band containing <paramref name="y"/> if any, else the nearest by edge
    /// distance; -1 if none supplied. Bands are disjoint, so at most one contains y.</summary>
    private static int NearestBand(IReadOnlyList<(double Top, double Bottom)> bands, double y)
    {
        var best = -1;
        var bestDist = double.MaxValue;
        for (var i = 0; i < bands.Count; i++)
        {
            var dist = y < bands[i].Top ? bands[i].Top - y
                     : y > bands[i].Bottom ? y - bands[i].Bottom
                     : 0;
            if (dist < bestDist) { bestDist = dist; best = i; }
        }
        return best;
    }
}
