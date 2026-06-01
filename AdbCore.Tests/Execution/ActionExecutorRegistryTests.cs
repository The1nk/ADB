using AdbCore.Execution;
using Xunit;

namespace AdbCore.Tests.Execution;

public class ActionExecutorRegistryTests
{
    [Fact]
    public void Register_ThenGet_ReturnsSameInstance()
    {
        var registry = new ActionExecutorRegistry();
        var exec = new FakeExecutor { TypeKey = "test.alpha" };

        registry.Register(exec);

        Assert.Same(exec, registry.Get("test.alpha"));
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void TryGet_UnknownKey_ReturnsFalseAndNull()
    {
        var registry = new ActionExecutorRegistry();

        var found = registry.TryGet("nope", out var exec);

        Assert.False(found);
        Assert.Null(exec);
    }

    [Fact]
    public void Get_UnknownKey_Throws()
    {
        var registry = new ActionExecutorRegistry();

        Assert.Throws<KeyNotFoundException>(() => registry.Get("nope"));
    }

    [Fact]
    public void Register_DuplicateKey_Throws()
    {
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "dup" });

        var ex = Assert.Throws<InvalidOperationException>(
            () => registry.Register(new FakeExecutor { TypeKey = "dup" }));
        Assert.Contains("dup", ex.Message);
    }

    [Fact]
    public void Register_Null_Throws()
    {
        var registry = new ActionExecutorRegistry();

        Assert.Throws<ArgumentNullException>(() => registry.Register(null!));
    }
}
