using System.Linq;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using Xunit;

namespace BotBuilder.Core.Tests;

public class NodeCopyPasteTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    [Fact]
    public void CopySelection_NothingSelected_IsNoOp_AndPasteDoesNothing()
    {
        var editor = NewEditor();
        editor.CopySelection();          // nothing selected
        var before = editor.Nodes.Count;
        editor.Paste();
        Assert.Equal(before, editor.Nodes.Count);
    }

    [Fact]
    public void Paste_SingleNode_ClonesWithNewIdAndOffset()
    {
        var editor = NewEditor();
        var src = editor.AddNode("data.setVariable", 100, 100);
        src.Config["name"] = "counter";
        editor.Select(src);
        editor.CopySelection();
        editor.Paste();

        Assert.Equal(2, editor.Nodes.Count);
        var pasted = editor.Nodes.Last();
        Assert.NotEqual(src.Id, pasted.Id);
        Assert.Equal("data.setVariable", pasted.TypeKey);
        Assert.Equal("counter", pasted.Config["name"]);
        Assert.Equal(124, pasted.X);
        Assert.Equal(124, pasted.Y);
        Assert.True(pasted.IsSelected);
        Assert.False(src.IsSelected);
    }

    [Fact]
    public void Paste_DeepCopiesConfig()
    {
        var editor = NewEditor();
        var src = editor.AddNode("data.setVariable", 100, 100);
        src.Config["name"] = "counter";
        editor.Select(src);
        editor.CopySelection();
        editor.Paste();

        var pasted = editor.Nodes.Last();
        pasted.Config["name"] = "changed";
        Assert.Equal("counter", src.Config["name"]);
    }

    [Fact]
    public void Paste_TwoConnectedNodes_ClonesNodesAndInternalConnection()
    {
        var editor = NewEditor();
        var a = editor.AddNode("control.start", 0, 0);
        var b = editor.AddNode("data.log", 200, 0);
        Connect(editor, a, "out", b, "in");
        editor.SelectNodes(new[] { a, b });
        editor.CopySelection();
        var connsBefore = editor.Connections.Count;
        editor.Paste();

        Assert.Equal(4, editor.Nodes.Count);
        Assert.Equal(connsBefore + 1, editor.Connections.Count);
        var pastedConn = editor.Connections.Last();
        // pasted connection joins the two pasted nodes, not the originals
        Assert.DoesNotContain(pastedConn.Source, new[] { a, b });
        Assert.DoesNotContain(pastedConn.Target, new[] { a, b });
    }

    [Fact]
    public void Paste_ConnectionToUnselectedNode_IsNotCopied()
    {
        var editor = NewEditor();
        var a = editor.AddNode("control.start", 0, 0);
        var b = editor.AddNode("data.log", 200, 0);
        Connect(editor, a, "out", b, "in");
        editor.Select(a);                  // only A selected
        editor.CopySelection();
        editor.Paste();
        // one new node (A'), no new connection (the A->B edge had a non-selected endpoint)
        Assert.Equal(3, editor.Nodes.Count);
        Assert.Single(editor.Connections);
    }

    [Fact]
    public void Paste_RunParallel_PreservesBranchPorts()
    {
        var editor = NewEditor();
        var rp = editor.AddNode(RunParallelAction.RunParallelTypeKey, 0, 0);
        rp.Config[RunParallelAction.BranchesKey] = 3;
        rp.SetBranchPortCount(3);
        editor.Select(rp);
        editor.CopySelection();
        editor.Paste();
        var pasted = editor.Nodes.Last();
        Assert.Equal(3, pasted.OutputPorts.Count);
    }

    [Fact]
    public void Paste_IsSingleUndoStep()
    {
        var editor = NewEditor();
        var a = editor.AddNode("control.start", 0, 0);
        var b = editor.AddNode("data.log", 200, 0);
        Connect(editor, a, "out", b, "in");
        editor.SelectNodes(new[] { a, b });
        editor.CopySelection();
        editor.Paste();
        Assert.Equal(4, editor.Nodes.Count);
        editor.Undo();
        Assert.Equal(2, editor.Nodes.Count);
        Assert.Single(editor.Connections);
    }

    // Local helper — uses the editor's public Connect path (port lookup by name).
    private static void Connect(BotEditorViewModel editor, NodeViewModel src, string outPort, NodeViewModel tgt, string inPort)
    {
        var sp = src.OutputPorts.First(p => p.Name == outPort);
        var tp = tgt.InputPorts.First(p => p.Name == inPort);
        editor.Connect(src, sp, tgt, tp);
    }
}
