using System;
using System.Linq;
using BotBuilder.Core;
using BotBuilder.Core.Connections;
using Xunit;

namespace BotBuilder.Core.Tests;

public class BackRoutePlannerTests
{
    private static BackRouteInput In(Guid id, double sx, double sy, double ex, double ey)
        => new(id, sx, sy, ex, ey, PortEdge.Right);

    [Fact]
    public void OnlyBackwardEdgesGetLanes()
    {
        var fwd = Guid.NewGuid(); var back = Guid.NewGuid();
        var plans = BackRoutePlanner.Plan(
            new[] { In(fwd, 0, 0, 100, 0), In(back, 100, 0, 0, 50) },
            nodesLeftX: 0, nodesRightX: 260,
            clearBands: new (double, double)[] { (-100, 200) });

        Assert.False(plans.ContainsKey(fwd));   // forward edge: not a back-route
        Assert.True(plans.ContainsKey(back));   // backward edge: laned
    }

    [Fact]
    public void EachBackRouteGetsADistinctCorridorAndGutter()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        var plans = BackRoutePlanner.Plan(
            new[]
            {
                In(a, 500, 100, 40, 300),
                In(b, 500, 110, 40, 310),
                In(c, 500, 120, 40, 320),
            },
            nodesLeftX: 40, nodesRightX: 660,
            // one clear band wide enough to hold all three staggered gutters distinctly
            clearBands: new (double, double)[] { (150, 350) });

        var rightXs = plans.Values.Select(p => p.RightCornerX).ToList();
        var leftXs  = plans.Values.Select(p => p.LeftCornerX).ToList();
        var gutters = plans.Values.Select(p => p.GutterY).ToList();

        Assert.Equal(3, rightXs.Distinct().Count());   // no shared right corridor
        Assert.Equal(3, leftXs.Distinct().Count());    // no shared left corridor
        Assert.Equal(3, gutters.Distinct().Count());   // no shared gutter row
        Assert.All(rightXs, x => Assert.True(x > 660)); // right corridors sit right of all nodes
        Assert.All(leftXs,  x => Assert.True(x < 40));  // left corridors sit left of all nodes
    }

    [Fact]
    public void WiderSpanGetsOuterLane()
    {
        var narrow = Guid.NewGuid(); var wide = Guid.NewGuid();
        var plans = BackRoutePlanner.Plan(
            new[]
            {
                In(narrow, 300, 100, 200, 300),   // span 100
                In(wide,   500, 100, 40, 300),    // span 460
            },
            nodesLeftX: 40, nodesRightX: 660,
            clearBands: new (double, double)[] { (150, 350) });

        // Wider span routes farther out on both sides (outer lane).
        Assert.True(plans[wide].RightCornerX > plans[narrow].RightCornerX);
        Assert.True(plans[wide].LeftCornerX  < plans[narrow].LeftCornerX);
    }

    [Fact]
    public void GutterSnapsIntoAClearBand_NotThroughARow()
    {
        // Raw midpoint (200) falls inside an occupied row (180..260); the clear bands sit on either
        // side of that row, so the gutter must snap into one of them rather than cut through the row.
        var id = Guid.NewGuid();
        var clearBands = new (double, double)[] { (140, 180), (260, 300) };
        var plans = BackRoutePlanner.Plan(
            new[] { In(id, 500, 100, 40, 300) },   // midpoint = 200
            nodesLeftX: 40, nodesRightX: 660,
            clearBands: clearBands);

        var gutterY = plans[id].GutterY;
        var inAClearBand = clearBands.Any(b => gutterY >= b.Item1 && gutterY <= b.Item2);
        Assert.True(inAClearBand, $"gutter {gutterY} should lie within a clear band");
        Assert.False(gutterY > 180 && gutterY < 260, $"gutter {gutterY} must not cut through the occupied row");
    }

    [Fact]
    public void Plan_DoesNotThrow_OnSubFourPixelBand()
    {
        // A clear band thinner than 4px (Top+2 > Bottom-2) must not crash the clamp; it falls back to
        // the band center. Freely drag-positioned rows can land 2-3px apart and produce such gaps.
        var id = Guid.NewGuid();
        var clearBands = new (double, double)[] { (200, 203) };   // 3px band near the route midpoint
        var plans = BackRoutePlanner.Plan(
            new[] { In(id, 500, 180, 40, 220) },   // midpoint = 200, nearest the thin band
            nodesLeftX: 40, nodesRightX: 660,
            clearBands: clearBands);

        var gutterY = plans[id].GutterY;
        Assert.True(double.IsFinite(gutterY), "gutter Y must be finite");
        Assert.InRange(gutterY, 200, 203);   // snapped to the thin band's center
    }
}
