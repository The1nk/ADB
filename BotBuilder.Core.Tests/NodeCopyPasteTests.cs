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
}
