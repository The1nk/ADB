using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class BackRouteRoutingTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    private static void Connect(BotEditorViewModel editor, NodeViewModel s, string sp, NodeViewModel t, string tp)
    {
        var sport = s.OutputPorts.First(p => p.Name == sp);
        var tport = t.InputPorts.First(p => p.Name == tp);
        editor.Connect(s, sport, t, tport);
    }

    [Fact]
    public void RerouteBackEdges_LanesBackwardConnectionsOnly()
    {
        // A branch output is a multi-output (non-sole-1-1) port, so the derived orientation pass never
        // straightens it toward its target — it stays a genuine geometric back-edge when its target is laid
        // out to the LEFT. (A plain sole-1-1 link would instead be oriented to point at its neighbor, so it
        // could never remain backward.) Position the branch's target to its left, run the full derived
        // display pipeline (orient + reroute) as the app does, and assert the wire switched to a LANED
        // orthogonal route (distinct from a plain back-route) routed out to its right corridor.
        var editor = NewEditor();
        var a = editor.AddNode("control.branch", 500, 500);
        var b = editor.AddNode("data.log", 30, 200);
        Connect(editor, a, "true", b, "in");

        // place the source branch (a) to the right of the target (b) -> the a.true->b connection is backward
        a.X = 600; a.Y = 100;
        b.X = 40; b.Y = 300;

        editor.OrientSingleConnectionPorts();
        editor.RerouteBackEdges();

        var path = editor.Connections[0].PathData;
        Assert.Contains(" L ", path);          // orthogonal route
        Assert.DoesNotContain(" C ", path);

        // A LANED back-route (BuildLanedBackRoute) emits 7 `L` segments; a plain unlaned back-route
        // (BuildBackRoute) emits 5. Asserting 7 proves SetLane actually applied a lane (not a no-op).
        Assert.Equal(7, path.Split(" L ").Length - 1);

        // And the lane's right corridor must route out PAST the rightmost node edge — only a laned
        // route does this. Parse every "x,y" point and assert the max X exceeds every node's right edge.
        var rightmostNodeEdge = editor.Nodes.Max(n => n.X + NodeLayout.CardWidth);
        var maxX = Regex.Matches(path, @"(-?\d+(?:\.\d+)?),(-?\d+(?:\.\d+)?)")
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .Max();
        Assert.True(maxX > rightmostNodeEdge,
            $"laned route should route past the node block (maxX {maxX} > rightmost edge {rightmostNodeEdge})");

        // The horizontal run rides the gutter Y. With band-aware planning that Y must sit in a clear gap
        // between rows, never inside a node's vertical extent. The gutter is the Y of the longest
        // horizontal segment (the across-run between the two corridors): take consecutive point pairs and
        // pick the Y of the pair with the largest horizontal span, then assert it clears every node row.
        var pts = Regex.Matches(path, @"(-?\d+(?:\.\d+)?),(-?\d+(?:\.\d+)?)")
            .Select(m => (X: double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                          Y: double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)))
            .ToList();
        var gutterY = Enumerable.Range(0, pts.Count - 1)
            .Where(i => pts[i].Y == pts[i + 1].Y)                 // horizontal segments only
            .OrderByDescending(i => Math.Abs(pts[i + 1].X - pts[i].X))
            .Select(i => pts[i].Y)
            .First();
        Assert.All(editor.Nodes, n =>
            Assert.False(gutterY > n.Y && gutterY < n.Y + n.Height,
                $"gutter Y {gutterY} cuts through node at [{n.Y}, {n.Y + n.Height}]"));
    }
}
