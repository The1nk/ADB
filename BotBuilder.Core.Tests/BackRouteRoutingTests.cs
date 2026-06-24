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
        // Build an editor with two nodes wired forward, then position the target to the LEFT of the
        // source so the connection becomes a back-route; reroute; assert it switched to a LANED
        // orthogonal route (distinct from a plain back-route) routed out to its right corridor.
        var editor = NewEditor();
        var a = editor.AddNode("control.start", 500, 500);
        var b = editor.AddNode("data.log", 30, 200);
        Connect(editor, a, "out", b, "in");

        // place the source (a) to the right of the target (b) -> the a->b connection is now backward
        a.X = 600; a.Y = 100;
        b.X = 40; b.Y = 300;

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
    }
}
