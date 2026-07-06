using System.Linq;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using BotBuilder.Core.Connections;
using Xunit;

namespace BotBuilder.Core.Tests;

/// <summary>The derived <see cref="ConnectionViewModel.IsBackEdge"/> flag that drives the dashed/muted
/// back-edge rendering: true only for a laned, backward-facing (loop/return) wire.</summary>
public class IsBackEdgeTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    [Fact]
    public void ForwardWire_IsNotBackEdge()
    {
        var editor = NewEditor();
        var a = editor.AddNode("control.delay", 0, 0);
        var b = editor.AddNode("control.delay", 400, 0);
        editor.Connect(a, a.OutputPorts[0], b, b.InputPorts[0]);

        Assert.False(editor.Connections.Single().IsBackEdge);
    }

    [Fact]
    public void LanedBackwardWire_IsBackEdge()
    {
        // A branch output (multi-output, never straightened by the orientation pass) whose target is laid
        // out to the LEFT stays a genuine back-edge and gets a lane -> IsBackEdge true.
        var editor = NewEditor();
        var br = editor.AddNode("control.branch", 600, 100);
        var target = editor.AddNode("data.log", 40, 300);
        editor.Connect(br, br.OutputPorts.First(p => p.Name == "true"), target, target.InputPorts[0]);

        editor.OrientSingleConnectionPorts();
        editor.RerouteBackEdges();

        Assert.True(editor.Connections.Single().IsBackEdge);
    }

    [Fact]
    public void VerticalBandTurn_IsNotBackEdge()
    {
        // A sole-1-1 link straightened into a vertical drop is forward, not a back-edge.
        var editor = NewEditor();
        var a = editor.AddNode("control.delay", 0, 0);
        var b = editor.AddNode("control.delay", 0, 300);
        editor.Connect(a, a.OutputPorts[0], b, b.InputPorts[0]);

        Assert.Equal(PortEdge.Bottom, a.OutputPorts[0].Edge);   // it did become a vertical drop
        Assert.False(editor.Connections.Single().IsBackEdge);
    }
}
