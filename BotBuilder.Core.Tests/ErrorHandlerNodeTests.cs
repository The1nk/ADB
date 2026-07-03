using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using BotBuilder.Core.Palette;
using Xunit;

namespace BotBuilder.Core.Tests;

public class ErrorHandlerNodeTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    [Fact]
    public void AddNode_FirstErrorHandler_IsAdded()
    {
        var editor = NewEditor();

        var node = editor.AddNode(ErrorHandlerAction.Key, 10, 20);

        Assert.Contains(node, editor.Nodes);
        Assert.Equal(ErrorHandlerAction.Key, node.TypeKey);
    }

    [Fact]
    public void AddNode_SecondErrorHandler_DoesNotDuplicate_AndSelectsExisting()
    {
        var editor = NewEditor();
        var first = editor.AddNode(ErrorHandlerAction.Key, 0, 0);

        var second = editor.AddNode(ErrorHandlerAction.Key, 100, 100);

        Assert.Same(first, second);
        Assert.Single(editor.Nodes, n => n.TypeKey == ErrorHandlerAction.Key);
        Assert.True(first.IsSelected);
    }

    [Fact]
    public void Palette_IncludesErrorHandler_UnderControlFlow()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        var palette = new PaletteViewModel(defs);

        var controlFlow = Assert.Single(palette.Categories, c => c.Name == "Control Flow");
        Assert.Contains(controlFlow.Items, i => i.TypeKey == ErrorHandlerAction.Key);
    }
}
