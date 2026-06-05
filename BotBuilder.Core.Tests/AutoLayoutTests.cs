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
    public void IsolatedNode_AtOriginColumn()
    {
        var a = Guid.NewGuid();
        var pos = AutoLayout.Arrange(new[] { N(a) }, Array.Empty<(Guid, Guid)>());
        Assert.Equal(AutoLayout.OriginX, pos[a].X);
    }

    [Fact]
    public void EmptyGraph_EmptyResult()
        => Assert.Empty(AutoLayout.Arrange(Array.Empty<(Guid, double)>(), Array.Empty<(Guid, Guid)>()));
}
