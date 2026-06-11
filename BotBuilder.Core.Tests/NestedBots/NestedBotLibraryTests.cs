using AdbCore.Models;
using BotBuilder.Core.NestedBots;
using Xunit;

namespace BotBuilder.Core.Tests.NestedBots;

public class NestedBotLibraryTests
{
    [Fact]
    public void AddNew_CreatesNamedEntryWithFreshId()
    {
        var lib = new NestedBotLibrary();
        var bot = lib.AddNew("GoToPlayerMenu");

        Assert.Equal("GoToPlayerMenu", bot.Name);
        Assert.NotEqual(Guid.Empty, bot.Id);
        Assert.Single(lib.Entries);
        Assert.Same(bot, lib.Get(bot.Id));
    }

    [Fact]
    public void Rename_UpdatesEntryName()
    {
        var lib = new NestedBotLibrary();
        var bot = lib.AddNew("Old");
        lib.Rename(bot.Id, "New");
        Assert.Equal("New", lib.Get(bot.Id)!.Name);
    }

    [Fact]
    public void Remove_DropsEntry()
    {
        var lib = new NestedBotLibrary();
        var bot = lib.AddNew("X");
        Assert.True(lib.Remove(bot.Id));
        Assert.Empty(lib.Entries);
        Assert.Null(lib.Get(bot.Id));
    }

    [Fact]
    public void Load_ReplacesEntries()
    {
        var lib = new NestedBotLibrary();
        lib.AddNew("Stale");
        var fresh = new Bot { Id = Guid.NewGuid(), Name = "Loaded" };
        lib.Load(new[] { fresh });
        Assert.Single(lib.Entries);
        Assert.Equal("Loaded", lib.Entries[0].Name);
    }
}
