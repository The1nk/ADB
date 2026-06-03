using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;

namespace BotBuilder.Core.Tests;

public class BotEditorViewModelRunStateTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    [Fact]
    public void ResetRunStates_ClearsEveryNode()
    {
        var editor = NewEditor();
        var a = editor.AddNode("control.start", 0, 0);
        var b = editor.AddNode("control.end", 100, 0);
        a.RunState = NodeRunState.Succeeded;
        b.RunState = NodeRunState.Failed;

        editor.ResetRunStates();

        Assert.Equal(NodeRunState.None, a.RunState);
        Assert.Equal(NodeRunState.None, b.RunState);
    }
}
