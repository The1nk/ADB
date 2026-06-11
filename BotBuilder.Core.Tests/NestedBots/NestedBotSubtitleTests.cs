using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using BotBuilder.Core.NestedBots;
using Xunit;

namespace BotBuilder.Core.Tests.NestedBots;

public class NestedBotSubtitleTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    [Fact]
    public void NestedBotNode_HasDistinctAccentColor()
    {
        var editor = NewEditor();
        var node = editor.AddNode(NestedBotAction.NestedBotTypeKey, 0, 0);
        Assert.Equal(CategoryColors.NestedBot, node.CategoryColor);
        Assert.NotEqual(CategoryColors.ColorFor("Control Flow"), node.CategoryColor);
    }

    [Fact]
    public void RefreshNestedBotSubtitles_SetsNameOrPlaceholder()
    {
        var editor = NewEditor();
        var node = editor.AddNode(NestedBotAction.NestedBotTypeKey, 0, 0);
        Assert.Equal(NestedBotCardInfo.Unassigned, node.Subtitle); // refreshed on add

        var bot = editor.NestedBotLibrary.AddNew("Sub");
        node.Config[NestedBotAction.NestedBotIdKey] = bot.Id.ToString();
        editor.RefreshNestedBotSubtitles();
        Assert.Equal("Sub", node.Subtitle);
    }

    [Fact]
    public void NonNestedNode_HasNullSubtitle()
    {
        var editor = NewEditor();
        var node = editor.AddNode("control.start", 0, 0);
        Assert.Null(node.Subtitle);
    }
}
