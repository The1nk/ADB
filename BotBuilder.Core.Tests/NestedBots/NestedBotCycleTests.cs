using AdbCore.Actions.BuiltIn;
using AdbCore.Models;
using BotBuilder.Core.NestedBots;
using Xunit;

namespace BotBuilder.Core.Tests.NestedBots;

public class NestedBotCycleTests
{
    private static BotAction Ref(Guid nestedId) => new()
    {
        Id = Guid.NewGuid(),
        TypeKey = NestedBotAction.NestedBotTypeKey,
        Config = { [NestedBotAction.NestedBotIdKey] = nestedId.ToString() },
    };

    [Fact]
    public void SelfReference_IsCycle()
    {
        var lib = new NestedBotLibrary();
        var a = lib.AddNew("A");
        Assert.True(lib.WouldCreateCycle(a.Id, a.Id));
    }

    [Fact]
    public void DirectBackReference_IsCycle()
    {
        var lib = new NestedBotLibrary();
        var a = lib.AddNew("A");
        var b = lib.AddNew("B");
        b.Actions.Add(Ref(a.Id)); // B already references A
        // Assigning a card in A that references B would close the loop A->B->A.
        Assert.True(lib.WouldCreateCycle(a.Id, b.Id));
    }

    [Fact]
    public void TransitiveBackReference_IsCycle()
    {
        var lib = new NestedBotLibrary();
        var a = lib.AddNew("A");
        var b = lib.AddNew("B");
        var c = lib.AddNew("C");
        b.Actions.Add(Ref(c.Id)); // B->C
        c.Actions.Add(Ref(a.Id)); // C->A
        // A->B would close A->B->C->A.
        Assert.True(lib.WouldCreateCycle(a.Id, b.Id));
    }

    [Fact]
    public void IndependentReference_IsNotCycle()
    {
        var lib = new NestedBotLibrary();
        var a = lib.AddNew("A");
        var b = lib.AddNew("B");
        Assert.False(lib.WouldCreateCycle(a.Id, b.Id)); // B doesn't reference A
    }
}
