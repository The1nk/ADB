using AdbCore.Actions;
using Xunit;

namespace AdbCore.Tests.Actions;

public class ActionRegistryTests
{
    [Fact]
    public void Register_ThenGet_ReturnsSameInstance()
    {
        var registry = new ActionRegistry();
        var def = new FakeActionDefinition { TypeKey = "test.alpha" };

        registry.Register(def);

        Assert.Same(def, registry.Get("test.alpha"));
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void TryGet_UnknownKey_ReturnsFalseAndNull()
    {
        var registry = new ActionRegistry();

        var found = registry.TryGet("does.not.exist", out var def);

        Assert.False(found);
        Assert.Null(def);
    }

    [Fact]
    public void Get_UnknownKey_Throws()
    {
        var registry = new ActionRegistry();

        Assert.Throws<KeyNotFoundException>(() => registry.Get("does.not.exist"));
    }

    [Fact]
    public void Register_DuplicateKey_Throws()
    {
        var registry = new ActionRegistry();
        registry.Register(new FakeActionDefinition { TypeKey = "test.dup" });

        var ex = Assert.Throws<InvalidOperationException>(
            () => registry.Register(new FakeActionDefinition { TypeKey = "test.dup" }));
        Assert.Contains("test.dup", ex.Message);
    }

    [Fact]
    public void Register_Null_Throws()
    {
        var registry = new ActionRegistry();

        Assert.Throws<ArgumentNullException>(() => registry.Register(null!));
    }

    [Fact]
    public void GetByCategory_ReturnsOnlyMatching()
    {
        var registry = new ActionRegistry();
        registry.Register(new FakeActionDefinition { TypeKey = "a", Category = "Screen" });
        registry.Register(new FakeActionDefinition { TypeKey = "b", Category = "Screen" });
        registry.Register(new FakeActionDefinition { TypeKey = "c", Category = "Android" });

        var screen = registry.GetByCategory("Screen").ToList();

        Assert.Equal(2, screen.Count);
        Assert.All(screen, d => Assert.Equal("Screen", d.Category));
    }

    [Fact]
    public void All_ReturnsEveryRegisteredDefinition()
    {
        var registry = new ActionRegistry();
        registry.Register(new FakeActionDefinition { TypeKey = "a" });
        registry.Register(new FakeActionDefinition { TypeKey = "b" });

        Assert.Equal(2, registry.All.Count);
    }
}
