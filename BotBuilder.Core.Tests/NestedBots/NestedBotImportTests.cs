using AdbCore.Actions.BuiltIn;
using AdbCore.Models;
using BotBuilder.Core.NestedBots;
using Xunit;

namespace BotBuilder.Core.Tests.NestedBots;

public class NestedBotImportTests
{
    [Fact]
    public void Import_AddsEntryWithFreshId_DetachedFromSource()
    {
        var lib = new NestedBotLibrary();
        var external = new Bot
        {
            Id = Guid.NewGuid(),
            Name = "GoToPlayerMenu",
            Actions = { new BotAction { Id = Guid.NewGuid(), TypeKey = "control.start", Config = { ["k"] = "v" } } },
        };

        var imported = lib.Import(external);

        Assert.Contains(imported, lib.Entries);
        Assert.NotEqual(external.Id, imported.Id);            // fresh entry id
        Assert.Equal("GoToPlayerMenu", imported.Name);
        Assert.NotSame(external.Actions[0], imported.Actions[0]); // deep copy
        Assert.NotSame(external.Actions[0].Config, imported.Actions[0].Config);
    }

    [Fact]
    public void Import_FlattensNestedLibrary_AndRemapsReferences()
    {
        // external is itself a root: top graph has a card referencing its own nested entry "Inner".
        var inner = new Bot { Id = Guid.NewGuid(), Name = "Inner" };
        var external = new Bot { Id = Guid.NewGuid(), Name = "Outer", NestedBots = { inner } };
        external.Actions.Add(new BotAction
        {
            Id = Guid.NewGuid(),
            TypeKey = NestedBotAction.NestedBotTypeKey,
            Config = { [NestedBotAction.NestedBotIdKey] = inner.Id.ToString() },
        });

        var lib = new NestedBotLibrary();
        var imported = lib.Import(external);

        // Both bots are now flat entries with fresh ids.
        Assert.Equal(2, lib.Entries.Count);
        var importedInner = lib.Entries.Single(b => b.Name == "Inner");
        Assert.NotEqual(inner.Id, importedInner.Id);

        // The card's reference was remapped to the new Inner id.
        var card = imported.Actions.Single(a => a.TypeKey == NestedBotAction.NestedBotTypeKey);
        Assert.Equal(importedInner.Id.ToString(), card.Config[NestedBotAction.NestedBotIdKey]);

        // The imported entries carry no own NestedBots (flattened into the root library).
        Assert.Empty(imported.NestedBots);
        Assert.Empty(importedInner.NestedBots);
    }
}
