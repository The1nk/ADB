using AdbCore.Actions.BuiltIn;
using AdbCore.Models;
using BotBuilder.Core.NestedBots;
using Xunit;

namespace BotBuilder.Core.Tests.NestedBots;

public class NestedBotExportTests
{
    // Builds a library entry that references another entry by a Nested Bot card.
    private static (NestedBotLibrary lib, Bot outer, Bot inner, Bot unrelated) BuildLibrary()
    {
        var lib = new NestedBotLibrary();
        var outer = lib.AddNew("Outer");
        var inner = lib.AddNew("Inner");
        var unrelated = lib.AddNew("Unrelated");

        outer.Actions.Add(new BotAction
        {
            Id = Guid.NewGuid(),
            TypeKey = NestedBotAction.NestedBotTypeKey,
            Config = { [NestedBotAction.NestedBotIdKey] = inner.Id.ToString() },
        });
        return (lib, outer, inner, unrelated);
    }

    [Fact]
    public void Export_BundlesTransitivelyReferencedNestedBots()
    {
        var (lib, outer, inner, _) = BuildLibrary();

        var exported = lib.Export(outer.Id);

        Assert.Equal("Outer", exported.Name);
        Assert.Single(exported.NestedBots);
        Assert.Equal(inner.Id, exported.NestedBots[0].Id);
    }

    [Fact]
    public void Export_ExcludesUnreferencedEntries()
    {
        var (lib, outer, _, unrelated) = BuildLibrary();

        var exported = lib.Export(outer.Id);

        Assert.DoesNotContain(exported.NestedBots, b => b.Id == unrelated.Id);
    }

    [Fact]
    public void Export_BundlesMultiLevelTransitiveReferences()
    {
        var lib = new NestedBotLibrary();
        var outer = lib.AddNew("Outer");
        var inner = lib.AddNew("Inner");
        var grandchild = lib.AddNew("Grandchild");

        outer.Actions.Add(new BotAction
        {
            Id = Guid.NewGuid(),
            TypeKey = NestedBotAction.NestedBotTypeKey,
            Config = { [NestedBotAction.NestedBotIdKey] = inner.Id.ToString() },
        });
        inner.Actions.Add(new BotAction
        {
            Id = Guid.NewGuid(),
            TypeKey = NestedBotAction.NestedBotTypeKey,
            Config = { [NestedBotAction.NestedBotIdKey] = grandchild.Id.ToString() },
        });

        var exported = lib.Export(outer.Id);

        Assert.Contains(exported.NestedBots, b => b.Id == inner.Id);
        Assert.Contains(exported.NestedBots, b => b.Id == grandchild.Id);
        Assert.Equal(2, exported.NestedBots.Count);
    }

    [Fact]
    public void Export_PreservesIdsAndReferences()
    {
        var (lib, outer, inner, _) = BuildLibrary();

        var exported = lib.Export(outer.Id);

        Assert.Equal(outer.Id, exported.Id);
        var card = exported.Actions.Single(a => a.TypeKey == NestedBotAction.NestedBotTypeKey);
        Assert.Equal(inner.Id.ToString(), card.Config[NestedBotAction.NestedBotIdKey]);
    }

    [Fact]
    public void Export_IsDeepCopy_DoesNotMutateLibrary()
    {
        var (lib, outer, inner, _) = BuildLibrary();
        var countBefore = lib.Entries.Count;

        var exported = lib.Export(outer.Id);
        exported.NestedBots[0].Name = "Bundled renamed";
        exported.Name = "Renamed after export";

        Assert.Equal(countBefore, lib.Entries.Count);
        Assert.Equal("Outer", lib.Get(outer.Id)!.Name);   // source untouched
        Assert.Equal("Inner", lib.Get(inner.Id)!.Name);   // bundled source untouched
        Assert.NotSame(outer, exported);
    }

    [Fact]
    public void Export_UnknownId_Throws()
    {
        var lib = new NestedBotLibrary();
        Assert.Throws<InvalidOperationException>(() => lib.Export(Guid.NewGuid()));
    }

    [Fact]
    public void Export_ToleratesDanglingReference()
    {
        var lib = new NestedBotLibrary();
        var outer = lib.AddNew("Outer");
        var danglingId = Guid.NewGuid();
        outer.Actions.Add(new BotAction
        {
            Id = Guid.NewGuid(),
            TypeKey = NestedBotAction.NestedBotTypeKey,
            Config = { [NestedBotAction.NestedBotIdKey] = danglingId.ToString() },
        });

        var exported = lib.Export(outer.Id);

        Assert.Empty(exported.NestedBots);
        var card = exported.Actions.Single(a => a.TypeKey == NestedBotAction.NestedBotTypeKey);
        Assert.Equal(danglingId.ToString(), card.Config[NestedBotAction.NestedBotIdKey]);
    }

    [Fact]
    public void Import_OfExport_RoundTripsToEquivalentGraph()
    {
        var (lib, outer, inner, _) = BuildLibrary();
        var exported = lib.Export(outer.Id);

        var target = new NestedBotLibrary();
        var reimported = target.Import(exported);

        // Two flat entries (Outer + Inner), fresh ids, card reference remapped and still resolvable.
        Assert.Equal(2, target.Entries.Count);
        Assert.Equal("Outer", reimported.Name);
        var card = reimported.Actions.Single(a => a.TypeKey == NestedBotAction.NestedBotTypeKey);
        var newInnerId = Guid.Parse(card.Config[NestedBotAction.NestedBotIdKey].ToString()!);
        Assert.Equal("Inner", target.Get(newInnerId)!.Name);
        Assert.NotEqual(outer.Id, reimported.Id);
        Assert.NotEqual(inner.Id, newInnerId);
    }
}
