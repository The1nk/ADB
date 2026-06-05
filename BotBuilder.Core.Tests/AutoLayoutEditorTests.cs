using System.Linq;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class AutoLayoutEditorTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    [Fact]
    public void AutoLayout_LaysOutChain_AndIsSingleUndoStep()
    {
        var editor = NewEditor();
        var a = editor.AddNode("control.start", 500, 500);
        var b = editor.AddNode("data.log", 30, 200);
        Connect(editor, a, "out", b, "in");

        editor.AutoLayout();

        Assert.True(a.X < b.X);                 // chain flows left-to-right
        var (ax, ay, bx, by) = (a.X, a.Y, b.X, b.Y);

        editor.Undo();                          // ONE undo restores both
        Assert.Equal(500, a.X);
        Assert.Equal(500, a.Y);
        Assert.Equal(30, b.X);
        Assert.Equal(200, b.Y);
        Assert.NotEqual(ax, a.X);               // sanity: layout had moved them
    }

    private static void Connect(BotEditorViewModel editor, NodeViewModel s, string sp, NodeViewModel t, string tp)
    {
        var sport = s.OutputPorts.First(p => p.Name == sp);
        var tport = t.InputPorts.First(p => p.Name == tp);
        editor.Connect(s, sport, t, tport);
    }
}
