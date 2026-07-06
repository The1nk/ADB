using System.Linq;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using BotBuilder.Core.Connections;
using Xunit;

namespace BotBuilder.Core.Tests;

/// <summary>The derived, non-persisted single-connection orientation pass: sole-1-1 links point their
/// ports at their neighbor (vertical drops on band turns), everything else keeps flip-aware defaults.</summary>
public class OrientSingleConnectionPortsTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    private static (NodeViewModel a, NodeViewModel b) Chain(BotEditorViewModel editor, double ax, double ay, double bx, double by)
    {
        var a = editor.AddNode("control.delay", ax, ay);
        var b = editor.AddNode("control.delay", bx, by);
        Assert.Equal(ConnectionError.None, editor.Connect(a, a.OutputPorts[0], b, b.InputPorts[0]));
        return (a, b);
    }

    [Fact]
    public void SoleLink_TargetClearlyBelow_BecomesVerticalDrop()
    {
        var editor = NewEditor();
        var (a, b) = Chain(editor, 0, 0, 0, 300);   // b directly below a
        editor.OrientSingleConnectionPorts();

        Assert.Equal(PortEdge.Bottom, a.OutputPorts[0].Edge);
        Assert.Equal(PortEdge.Top, b.InputPorts[0].Edge);
        // Both centered on the card so the drop is truly vertical.
        Assert.Equal(NodeLayout.CardWidth / 2, a.OutputPorts[0].AnchorOffset.X);
        Assert.Equal(NodeLayout.CardWidth / 2, b.InputPorts[0].AnchorOffset.X);
    }

    [Fact]
    public void SoleLink_TargetClearlyAbove_BecomesUpwardDrop()
    {
        var editor = NewEditor();
        var (a, b) = Chain(editor, 0, 300, 0, 0);   // b directly above a
        editor.OrientSingleConnectionPorts();

        Assert.Equal(PortEdge.Top, a.OutputPorts[0].Edge);
        Assert.Equal(PortEdge.Bottom, b.InputPorts[0].Edge);
    }

    [Fact]
    public void SoleLink_TargetToRight_KeepsHorizontalDefaults()
    {
        var editor = NewEditor();
        var (a, b) = Chain(editor, 0, 0, 400, 0);   // b to the right on the same row
        editor.OrientSingleConnectionPorts();

        Assert.Equal(PortEdge.Right, a.OutputPorts[0].Edge);
        Assert.Equal(PortEdge.Left, b.InputPorts[0].Edge);
    }

    [Fact]
    public void Pass_IsIdempotent()
    {
        var editor = NewEditor();
        var (a, b) = Chain(editor, 0, 0, 0, 300);
        editor.OrientSingleConnectionPorts();
        editor.OrientSingleConnectionPorts();
        editor.OrientSingleConnectionPorts();

        Assert.Equal(PortEdge.Bottom, a.OutputPorts[0].Edge);
        Assert.Equal(PortEdge.Top, b.InputPorts[0].Edge);
    }

    [Fact]
    public void SelfHeals_AfterVerticalTurnMovesToHorizontal()
    {
        var editor = NewEditor();
        var (a, b) = Chain(editor, 0, 0, 0, 300);
        editor.OrientSingleConnectionPorts();
        Assert.Equal(PortEdge.Bottom, a.OutputPorts[0].Edge);

        // Move b beside a on the same row and recompute: the vertical override must reset to horizontal.
        b.Y = 0;
        b.X = 400;
        editor.OrientSingleConnectionPorts();

        Assert.Equal(PortEdge.Right, a.OutputPorts[0].Edge);
        Assert.Equal(PortEdge.Left, b.InputPorts[0].Edge);
    }

    [Fact]
    public void BranchNode_MultiOutput_KeepsDefaults()
    {
        var editor = NewEditor();
        var br = editor.AddNode("control.branch", 0, 0);
        var t0 = editor.AddNode("control.delay", 0, 300);
        var t1 = editor.AddNode("control.delay", 0, 600);
        // Wire both branch outputs to targets below.
        Assert.True(br.OutputPorts.Count >= 2);
        editor.Connect(br, br.OutputPorts[0], t0, t0.InputPorts[0]);
        editor.Connect(br, br.OutputPorts[1], t1, t1.InputPorts[0]);
        editor.OrientSingleConnectionPorts();

        // Multi-output source keeps side (non-vertical) outputs; failure/branch ports are not re-oriented
        // to Top/Bottom by the pass. (onTrue/onFalse sit on the Right edge by default.)
        Assert.All(br.OutputPorts.Where(p => !br.IsFailurePortName(p.Name)),
            p => Assert.Equal(PortEdge.Right, p.Edge));
    }

    [Fact]
    public void JoinTarget_MultiInbound_KeepsDefaultInput()
    {
        var editor = NewEditor();
        var s0 = editor.AddNode("control.delay", 0, 0);
        var s1 = editor.AddNode("control.delay", 0, 200);
        var join = editor.AddNode("control.join", 0, 400);
        editor.Connect(s0, s0.OutputPorts[0], join, join.InputPorts[0]);
        editor.Connect(s1, s1.OutputPorts[0], join, join.InputPorts[0]);
        editor.OrientSingleConnectionPorts();

        // Join is fed by >1 wire -> its input keeps the default Left edge, not re-oriented to Top.
        Assert.Equal(PortEdge.Left, join.InputPorts[0].Edge);
    }

    [Fact]
    public void Serpentine12Chain_AutoLayout_HasVerticalBandTurns()
    {
        var editor = NewEditor();
        var nodes = new NodeViewModel[12];
        for (var i = 0; i < 12; i++) { nodes[i] = editor.AddNode("control.delay", i * 10, 0); }
        for (var i = 0; i < 11; i++)
        {
            Assert.Equal(ConnectionError.None,
                editor.Connect(nodes[i], nodes[i].OutputPorts[0], nodes[i + 1], nodes[i + 1].InputPorts[0]));
        }

        editor.AutoLayout();   // wraps into stacked bands; AfterEdit runs the orientation pass

        // At least one band-turn connector became a clean vertical drop: source Bottom -> target Top,
        // both centered on the card so the drop is truly vertical.
        var turns = editor.Connections
            .Where(c => c.SourcePort.Edge == PortEdge.Bottom && c.TargetPort.Edge == PortEdge.Top)
            .ToList();
        Assert.NotEmpty(turns);
        Assert.All(turns, c =>
        {
            Assert.Equal(NodeLayout.CardWidth / 2, c.SourcePort.AnchorOffset.X);
            Assert.Equal(NodeLayout.CardWidth / 2, c.TargetPort.AnchorOffset.X);
            Assert.True(c.Target.Y > c.Source.Y);   // target genuinely below
        });
    }

    [Fact]
    public void FailurePortNode_SuccessWireDown_KeepsSideAndBottomDefaults()
    {
        var editor = NewEditor();
        // screen.findImage has a Success (Right) output AND an onFailure (Bottom) output => 2 output ports,
        // so it is never a sole-1-1 source even when its success target sits directly below it.
        var src = editor.AddNode("screen.findImage", 0, 0);
        var below = editor.AddNode("control.delay", 0, 300);
        var success = src.OutputPorts.First(p => !src.IsFailurePortName(p.Name));
        var failure = src.OutputPorts.First(p => src.IsFailurePortName(p.Name));
        Assert.Equal(ConnectionError.None, editor.Connect(src, success, below, below.InputPorts[0]));
        editor.OrientSingleConnectionPorts();

        Assert.Equal(PortEdge.Right, success.Edge);    // NOT re-oriented to Bottom despite target below
        Assert.Equal(PortEdge.Bottom, failure.Edge);   // failure port stays Bottom-designated
        // The lone-input target below is also NOT flipped up (its source isn't a sole-1-1 source).
        Assert.Equal(PortEdge.Left, below.InputPorts[0].Edge);
    }
}
