using System;
using System.Collections.Generic;
using System.Linq;
using BotBuilder.Core.Layout;
using Xunit;

namespace BotBuilder.Core.Tests;

public class AutoLayoutTests
{
    private static (Guid Id, double Height) N(Guid id, double h = 70) => (id, h);

    [Fact]
    public void LinearChain_LayersLeftToRight()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        var pos = AutoLayout.Arrange(new[] { N(a), N(b), N(c) },
            new[] { (a, b), (b, c) });
        Assert.True(pos[a].X < pos[b].X && pos[b].X < pos[c].X);
    }

    [Fact]
    public void FanOut_SameColumn_DifferentRows_NoOverlap()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        var pos = AutoLayout.Arrange(new[] { N(a), N(b, 70), N(c, 70) },
            new[] { (a, b), (a, c) });
        Assert.Equal(pos[b].X, pos[c].X);                 // same layer/column
        Assert.True(Math.Abs(pos[b].Y - pos[c].Y) >= 70); // packed, no overlap
    }

    [Fact]
    public void Diamond_TakesLongestPath()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid(); var d = Guid.NewGuid();
        var pos = AutoLayout.Arrange(new[] { N(a), N(b), N(c), N(d) },
            new[] { (a, b), (a, c), (b, d), (c, d) });
        Assert.True(pos[d].X > pos[b].X);                 // d after b/c (layer 2, not 1)
        Assert.Equal(pos[b].X, pos[c].X);
    }

    [Fact]
    public void Cycle_Terminates_AndPlacesBoth()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var pos = AutoLayout.Arrange(new[] { N(a), N(b) }, new[] { (a, b), (b, a) });
        Assert.True(pos.ContainsKey(a) && pos.ContainsKey(b));
        Assert.True(pos[a].X < pos[b].X);                 // back-edge b->a dropped; a=layer0, b=layer1
    }

    [Fact]
    public void HeightAwarePacking()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        var pos = AutoLayout.Arrange(new[] { N(a), N(b, 70), N(c, 110) },
            new[] { (a, b), (a, c) });
        var top = Math.Min(pos[b].Y, pos[c].Y);
        var firstHeight = pos[b].Y < pos[c].Y ? 70 : 110;
        var bottom = Math.Max(pos[b].Y, pos[c].Y);
        Assert.True(bottom >= top + firstHeight + 30);    // gap >= firstHeight + RowGap
    }

    [Fact]
    public void Barycenter_UncrossesParallelEdges()
    {
        // Two roots a(top), x(bottom) feed two layer-1 nodes. Edges a->q and x->p would, under
        // naive input-order placement of layer 1 ([p, q]), cross. Barycenter must reorder layer 1
        // to [q, p] so a(top)->q(top) and x(bottom)->p(bottom) run parallel (no crossing).
        var a = Guid.NewGuid(); var x = Guid.NewGuid();   // order 0,1  -> a above x
        var p = Guid.NewGuid(); var q = Guid.NewGuid();   // order 2,3  -> p above q (naive)
        var pos = AutoLayout.Arrange(
            new[] { N(a), N(x), N(p), N(q) },
            new[] { (a, q), (x, p) });

        Assert.True(pos[a].Y < pos[x].Y);                 // roots keep input order in layer 0
        Assert.Equal(pos[p].X, pos[q].X);                 // p, q share layer 1 (same column)
        Assert.True(pos[q].Y < pos[p].Y);                 // reordered so q is above p -> uncrossed
    }

    private static Guid[] LinearChain(int n, out (Guid, Guid)[] edges)
    {
        var ids = new Guid[n];
        for (var i = 0; i < n; i++) ids[i] = Guid.NewGuid();
        edges = new (Guid, Guid)[n - 1];
        for (var i = 0; i < n - 1; i++) edges[i] = (ids[i], ids[i + 1]);
        return ids;
    }

    [Fact]
    public void LongChain_WrapsIntoStackedRows()
    {
        var ids = LinearChain(12, out var edges);
        var pos = AutoLayout.Arrange(ids.Select(id => N(id)).ToArray(), edges);

        // Used vertical space: the chain is no longer a single row.
        Assert.True(pos.Values.Select(p => p.Y).Distinct().Count() > 1);
        // Row-reset: at least two nodes start a row at the left origin (node 0 and each band start).
        Assert.True(pos.Values.Count(p => p.X == AutoLayout.OriginX) >= 2);
        // Narrower than a full single row would have been (11 * ColGap wide).
        var width = pos.Values.Max(p => p.X) - pos.Values.Min(p => p.X);
        Assert.True(width < 11 * AutoLayout.ColGap);
    }

    [Fact]
    public void ShortChain_DoesNotWrap()
    {
        var ids = LinearChain(4, out var edges);
        var pos = AutoLayout.Arrange(ids.Select(id => N(id)).ToArray(), edges);

        Assert.Single(pos.Values.Select(p => p.Y).Distinct());          // one row
        for (var i = 0; i < ids.Length - 1; i++)
            Assert.True(pos[ids[i]].X < pos[ids[i + 1]].X);             // strictly increasing X
    }

    [Fact]
    public void IsolatedNode_AtOriginColumn()
    {
        var a = Guid.NewGuid();
        var pos = AutoLayout.Arrange(new[] { N(a) }, Array.Empty<(Guid, Guid)>());
        Assert.Equal(AutoLayout.OriginX, pos[a].X);
    }

    [Fact]
    public void EmptyGraph_EmptyResult()
        => Assert.Empty(AutoLayout.Arrange(Array.Empty<(Guid, double)>(), Array.Empty<(Guid, Guid)>()));

    [Fact]
    public void PortAware_OrdersSiblingsByFeedingPort()
    {
        var br = Guid.NewGuid();
        var t0 = Guid.NewGuid();   // created first; fed by the LOWER port
        var t1 = Guid.NewGuid();   // created second; fed by the UPPER port
        // edges: (source, target, sourcePortY, targetPortY)
        var pos = AutoLayout.Arrange(
            new[] { N(br), N(t0), N(t1) },
            new[]
            {
                (br, t0, 60.0, 35.0),   // br's lower output port (Y=60) -> t0
                (br, t1, 20.0, 35.0),   // br's upper output port (Y=20) -> t1
            });

        Assert.Equal(pos[t0].X, pos[t1].X);     // same column
        Assert.True(pos[t1].Y < pos[t0].Y);     // t1 (upper port) placed above t0 -> uncrossed
    }

    [Fact]
    public void LongChain_Serpentine_AlternatesFlip()
    {
        var ids = LinearChain(12, out var edges);
        var pos = AutoLayout.Arrange(ids.Select(id => N(id)).ToArray(), edges);

        Assert.False(pos[ids[0]].Flipped);              // band 0 is left-to-right
        Assert.Contains(pos.Values, p => p.Flipped);     // at least one reversed band
        Assert.Contains(pos.Values, p => !p.Flipped);
    }

    [Fact]
    public void FitToWidth_LimitsColumnsPerBand()
    {
        var ids = LinearChain(10, out var edges);
        var portEdges = edges.Select(e => (e.Item1, e.Item2, 0.0, 0.0)).ToArray();
        // Room for 4 columns: (4-1)*ColGap + card width.
        var target = 3 * AutoLayout.ColGap + 160;
        var pos = AutoLayout.Arrange(ids.Select(id => N(id)).ToArray(), portEdges, targetWidth: target);

        Assert.True(pos.Values.Select(p => p.X).Distinct().Count() <= 4);
    }

    [Fact]
    public void BranchFanOut_UpperPortFeedsUpperChild_NoCrossing()
    {
        // A single 2-output node (e.g. a Branch: upper "true" port, lower "false" port) fanning out to two
        // children. Even when the children are supplied in the "wrong" input order (upperChild created
        // AFTER lowerChild), the port-aware barycenter tie-break must order them by feeding-port Y so the
        // upper port's child sits above the lower port's child -> the two wires never cross.
        var branch = Guid.NewGuid();
        var lowerChild = Guid.NewGuid();   // created first, fed by the LOWER output port
        var upperChild = Guid.NewGuid();   // created second, fed by the UPPER output port
        var pos = AutoLayout.Arrange(
            new[] { N(branch), N(lowerChild), N(upperChild) },
            new[]
            {
                (branch, lowerChild, 50.0, 35.0),   // branch lower port (Y=50) -> lowerChild
                (branch, upperChild, 20.0, 35.0),   // branch upper port (Y=20) -> upperChild
            });

        Assert.Equal(pos[lowerChild].X, pos[upperChild].X);   // both children share the next column
        Assert.True(pos[upperChild].Y < pos[lowerChild].Y);   // upper port's child above -> uncrossed wires
    }

    [Fact]
    public void BackCompatOverload_KeepsInputOrder_WhenNoPortData()
    {
        // Same graph as PortAware_OrdersSiblingsByFeedingPort, but via the (Guid,Guid) overload (no port Y).
        // Without port data the barycenter tie falls back to input order, so the created-first sibling (t0)
        // stays ABOVE t1 — proving the two overloads diverge on this exact graph.
        var br = Guid.NewGuid();
        var t0 = Guid.NewGuid();   // created first
        var t1 = Guid.NewGuid();   // created second
        var pos = AutoLayout.Arrange(
            new[] { N(br), N(t0), N(t1) },
            new[] { (br, t0), (br, t1) });

        Assert.Equal(pos[t0].X, pos[t1].X);     // same column
        Assert.True(pos[t0].Y < pos[t1].Y);     // input order preserved: t0 above t1
    }
}
