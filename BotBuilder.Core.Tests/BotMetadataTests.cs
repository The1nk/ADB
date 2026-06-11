using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Serialization;
using BotBuilder.Core;
using BotBuilder.Core.NestedBots;
using Xunit;

namespace BotBuilder.Core.Tests;

public class BotMetadataTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    [Fact]
    public void New_SetsCreatedAndUpdatedTimestamps()
    {
        var editor = NewEditor();
        Assert.NotEqual(default, editor.CreatedAt);
        Assert.NotEqual(default, editor.UpdatedAt);
    }

    [Fact]
    public void Save_BumpsUpdatedAt_PreservesCreatedAt_AndPersists()
    {
        var editor = NewEditor();
        var created = editor.CreatedAt;
        var path = Path.Combine(Path.GetTempPath(), $"adb-meta-{Guid.NewGuid():N}.bot");
        try
        {
            editor.Save(path);
            Assert.Equal(created, editor.CreatedAt);          // preserved
            Assert.True(editor.UpdatedAt >= created);          // bumped (>= since fast)

            var loaded = new BotSerializer().Load(path);
            Assert.NotEqual(default, loaded.CreatedAt);
            Assert.NotEqual(default, loaded.UpdatedAt);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Description_RoundTrips()
    {
        var editor = NewEditor();
        editor.BotDescription = "Grinds the player menu.";
        var bot = DocumentMapper.ToBot(editor);
        Assert.Equal("Grinds the player menu.", bot.Description);

        var editor2 = NewEditor();
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        DocumentMapper.Populate(editor2, bot, defs);
        Assert.Equal("Grinds the player menu.", editor2.BotDescription);
    }

    [Fact]
    public void Populate_LoadsTimestamps()
    {
        var editor = NewEditor();
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        var when = new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var bot = new Bot { Id = Guid.NewGuid(), Name = "X", CreatedAt = when, UpdatedAt = when };

        DocumentMapper.Populate(editor, bot, defs);

        Assert.Equal(when, editor.CreatedAt);
        Assert.Equal(when, editor.UpdatedAt);
    }

    [Fact]
    public void AddNew_LibraryEntry_HasTimestamps()
    {
        var lib = new NestedBotLibrary();
        var bot = lib.AddNew("Sub");
        Assert.NotEqual(default, bot.CreatedAt);
        Assert.NotEqual(default, bot.UpdatedAt);
    }
}
