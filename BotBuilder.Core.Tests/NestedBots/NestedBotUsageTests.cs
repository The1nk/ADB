using System;
using System.Collections.Generic;
using System.Linq;
using AdbCore.Actions.BuiltIn;
using AdbCore.Models;
using BotBuilder.Core.NestedBots;
using Xunit;

namespace BotBuilder.Core.Tests.NestedBots;

public class NestedBotUsageTests
{
    private static Bot Entry(Guid id, string name, params Guid[] referTo)
    {
        var bot = new Bot { Id = id, Name = name };
        foreach (var r in referTo)
        {
            bot.Actions.Add(new BotAction
            {
                Id = Guid.NewGuid(),
                TypeKey = NestedBotAction.NestedBotTypeKey,
                Config = { [NestedBotAction.NestedBotIdKey] = r.ToString() },
            });
        }
        return bot;
    }

    private static NestedBotLibrary LibraryOf(params Bot[] entries)
    {
        var lib = new NestedBotLibrary();
        lib.Load(entries);
        return lib;
    }

    [Fact]
    public void UsageCount_CountsTopLevelAndNestedReferences()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var lib = LibraryOf(Entry(a, "A", b), Entry(b, "B"));
        var topLevel = new List<Guid> { a, a };

        Assert.Equal(2, NestedBotUsage.UsageCount(lib, topLevel, a));
        Assert.Equal(1, NestedBotUsage.UsageCount(lib, topLevel, b));
    }

    [Fact]
    public void ReachableFromTopLevel_FollowsTransitiveReferences()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var lib = LibraryOf(Entry(a, "A", b), Entry(b, "B", c), Entry(c, "C"));

        var reachable = NestedBotUsage.ReachableFromTopLevel(lib, new[] { a });

        Assert.Equal(new HashSet<Guid> { a, b, c }, reachable);
    }

    [Fact]
    public void UnusedEntries_ReturnsThoseNotReachableFromTopLevel()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var lib = LibraryOf(Entry(a, "A", b), Entry(b, "B"));

        var unused = NestedBotUsage.UnusedEntries(lib, Array.Empty<Guid>());

        Assert.Equal(new HashSet<Guid> { a, b }, unused.ToHashSet());
    }

    [Fact]
    public void UsageCount_IgnoresReferencesFromUnreachableEntries()
    {
        // The PokeGo case: orphan A references orphan B; nothing is reachable from the (empty) top level.
        // B's only referrer (A) is itself unused, so B's usage must be 0 — consistent with UnusedEntries
        // removing it, not the misleading "usage 1".
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var lib = LibraryOf(Entry(a, "A", b), Entry(b, "B"));

        Assert.Equal(0, NestedBotUsage.UsageCount(lib, Array.Empty<Guid>(), b));
        Assert.Equal(0, NestedBotUsage.UsageCount(lib, Array.Empty<Guid>(), a));
    }

    [Fact]
    public void UnusedEntries_KeepsReachableChainButDropsIslands()
    {
        var used = Guid.NewGuid();
        var usedChild = Guid.NewGuid();
        var island = Guid.NewGuid();
        var islandChild = Guid.NewGuid();
        var lib = LibraryOf(Entry(used, "Used", usedChild), Entry(usedChild, "UsedChild"),
                            Entry(island, "Island", islandChild), Entry(islandChild, "IslandChild"));

        var unused = NestedBotUsage.UnusedEntries(lib, new[] { used }).ToHashSet();

        Assert.Equal(new HashSet<Guid> { island, islandChild }, unused);
    }
}
