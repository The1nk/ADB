using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests.NestedBots;

public class NestedBotLibraryRoundTripTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    [Fact]
    public void ToBot_WritesLibrary()
    {
        var editor = NewEditor();
        editor.NestedBotLibrary.AddNew("Sub");

        var bot = DocumentMapper.ToBot(editor);

        Assert.Single(bot.NestedBots);
        Assert.Equal("Sub", bot.NestedBots[0].Name);
    }

    [Fact]
    public void Populate_LoadsLibrary()
    {
        var editor = NewEditor();
        var registry = new ActionRegistry();
        BuiltInActions.Register(registry, new ActionExecutorRegistry());
        var bot = new Bot { Id = Guid.NewGuid(), Name = "Root", NestedBots = { new Bot { Id = Guid.NewGuid(), Name = "Sub" } } };

        DocumentMapper.Populate(editor, bot, registry);

        Assert.Single(editor.NestedBotLibrary.Entries);
        Assert.Equal("Sub", editor.NestedBotLibrary.Entries[0].Name);
    }

    [Fact]
    public void New_ClearsLibrary()
    {
        var editor = NewEditor();
        editor.NestedBotLibrary.AddNew("Sub");
        editor.New();
        Assert.Empty(editor.NestedBotLibrary.Entries);
    }
}
