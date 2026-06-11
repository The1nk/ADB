using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests.Properties;

public class NestedBotPropertiesTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    [Fact]
    public void IsNestedBotCard_TrueForNestedNode()
    {
        var editor = NewEditor();
        var node = editor.AddNode(NestedBotAction.NestedBotTypeKey, 0, 0);
        editor.Select(node);
        Assert.True(editor.Properties.IsNestedBotCard);
    }

    [Fact]
    public void IsNestedBotCard_FalseForOtherNode()
    {
        var editor = NewEditor();
        var node = editor.AddNode("control.start", 0, 0);
        editor.Select(node);
        Assert.False(editor.Properties.IsNestedBotCard);
    }

    [Fact]
    public void SelectedNestedBotId_RoundTripsToConfig()
    {
        var editor = NewEditor();
        var node = editor.AddNode(NestedBotAction.NestedBotTypeKey, 0, 0);
        editor.Select(node);
        var bot = editor.NestedBotLibrary.AddNew("Sub");

        editor.Properties.SelectedNestedBotId = bot.Id;

        Assert.Equal(bot.Id.ToString(), node.Config[NestedBotAction.NestedBotIdKey]);
        Assert.Equal(bot.Id, editor.Properties.SelectedNestedBotId);
        Assert.Equal("Sub", editor.Properties.SelectedNestedBotName);
        Assert.Equal("Sub", node.Subtitle); // assignment refreshed the card
    }

    [Fact]
    public void ImportNestedBot_AddsEntryAndAssignsIt()
    {
        var editor = NewEditor();
        var node = editor.AddNode(NestedBotAction.NestedBotTypeKey, 0, 0);
        editor.Select(node);
        var external = new Bot { Id = Guid.NewGuid(), Name = "Imported" };

        var entry = editor.Properties.ImportNestedBot(external);

        Assert.Contains(entry, editor.NestedBotLibrary.Entries);
        Assert.Equal(entry.Id, editor.Properties.SelectedNestedBotId);
        Assert.Equal("Imported", node.Subtitle);
    }

    [Fact]
    public void EditableName_RenamesLibraryEntryLive()
    {
        var editor = NewEditor();
        var node = editor.AddNode(NestedBotAction.NestedBotTypeKey, 0, 0);
        editor.Select(node);
        var bot = editor.NestedBotLibrary.AddNew("Old");
        editor.Properties.SelectedNestedBotId = bot.Id;

        editor.Properties.SelectedNestedBotEditableName = "GoToPlayerMenu";

        Assert.Equal("GoToPlayerMenu", editor.NestedBotLibrary.Get(bot.Id)!.Name);
        Assert.Equal("GoToPlayerMenu", node.Subtitle);
    }

    [Fact]
    public void RemoveSelectedNestedBot_RemovesFromLibraryAndUnassigns()
    {
        var editor = NewEditor();
        var node = editor.AddNode(NestedBotAction.NestedBotTypeKey, 0, 0);
        editor.Select(node);
        var bot = editor.NestedBotLibrary.AddNew("Sub");
        editor.Properties.SelectedNestedBotId = bot.Id;

        editor.Properties.RemoveSelectedNestedBot();

        Assert.Empty(editor.NestedBotLibrary.Entries);
        Assert.Null(editor.Properties.SelectedNestedBotId);
    }
}
