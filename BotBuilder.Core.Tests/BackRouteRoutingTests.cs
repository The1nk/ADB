using System.Linq;
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
        // source so the connection becomes a back-route; reroute; assert it switched to an orthogonal path.
        var editor = NewEditor();
        var a = editor.AddNode("control.start", 500, 500);
        var b = editor.AddNode("data.log", 30, 200);
        Connect(editor, a, "out", b, "in");

        // place the source (a) to the right of the target (b) -> the a->b connection is now backward
        a.X = 600; a.Y = 100;
        b.X = 40; b.Y = 300;

        editor.RerouteBackEdges();

        var path = editor.Connections[0].PathData;
        Assert.Contains(" L ", path);          // laned orthogonal route
        Assert.DoesNotContain(" C ", path);
    }
}
