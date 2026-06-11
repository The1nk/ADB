using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using BotBuilder.Core;
using BotBuilder.Core.NestedBots;
using Xunit;

namespace BotBuilder.Core.Tests.NestedBots;

public class NestedBotEntryMappingTests
{
    private static (BotEditorViewModel editor, ActionRegistry reg) NewEditor(NestedBotLibrary? lib = null)
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return (new BotEditorViewModel(defs, lib), defs);
    }

    [Fact]
    public void ToBot_WithoutLibrary_LeavesNestedBotsEmpty()
    {
        var lib = new NestedBotLibrary();
        var (editor, _) = NewEditor(lib);
        lib.AddNew("ShouldNotLeak"); // editor shares this library

        var bot = DocumentMapper.ToBot(editor, includeLibrary: false);

        Assert.Empty(bot.NestedBots); // the shared library was NOT written into this (nested) bot
    }

    [Fact]
    public void Populate_WithoutLibrary_DoesNotClearSharedLibrary()
    {
        var lib = new NestedBotLibrary();
        lib.AddNew("KeepMe");
        var (editor, reg) = NewEditor(lib);
        var entry = new Bot { Id = Guid.NewGuid(), Name = "Entry" }; // an entry with empty NestedBots

        DocumentMapper.Populate(editor, entry, reg, includeLibrary: false);

        Assert.Single(lib.Entries);           // shared library untouched
        Assert.Equal("Entry", editor.BotName); // graph/name still populated
        Assert.Equal(entry.Id, editor.BotId);
    }

    [Fact]
    public void Replace_SwapsEntryByIdPreservingPosition()
    {
        var lib = new NestedBotLibrary();
        var a = lib.AddNew("A");
        var b = lib.AddNew("B");
        var updated = new Bot { Id = b.Id, Name = "B-edited" };

        lib.Replace(updated);

        Assert.Equal(2, lib.Entries.Count);
        Assert.Same(updated, lib.Get(b.Id));
        Assert.Equal("B-edited", lib.Entries[1].Name); // position preserved (index 1)
        Assert.Same(a, lib.Entries[0]);
    }
}
