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

    [Fact]
    public void UnknownHost_IsNotCycle()
    {
        // A host id that is not a library entry (e.g. the root bot) is never reachable
        // from any candidate, so no cycle can be formed.
        var lib = new NestedBotLibrary();
        var someEntry = lib.AddNew("SomeEntry");
        var unknownHostId = Guid.NewGuid();
        Assert.False(lib.WouldCreateCycle(unknownHostId, someEntry.Id));
    }

    [Fact]
    public void PreExistingDataCycle_Terminates()
    {
        // Simulate data that already contains an A<->B loop (should not happen in practice,
        // but the visited-set guard must prevent an infinite traversal).
        var lib = new NestedBotLibrary();
        var a = lib.AddNew("A");
        var b = lib.AddNew("B");
        a.Actions.Add(Ref(b.Id)); // A -> B
        b.Actions.Add(Ref(a.Id)); // B -> A  (pre-existing cycle in data)

        // WouldCreateCycle must return true (A is reachable from B) AND simply complete
        // (the visited set prevents an infinite loop through the A<->B data cycle).
        Assert.True(lib.WouldCreateCycle(a.Id, b.Id));
    }
}
