using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Execution;

public class ControlFlowExecutorRegistryTests
{
    private sealed class FakeControlFlow : IControlFlowExecutor
    {
        public required string TypeKey { get; init; }
        public Task<ControlFlowResult> ExecuteAsync(ControlFlowContext context, CancellationToken ct)
            => Task.FromResult(ControlFlowResult.Continue(null));
    }

    [Fact]
    public void Register_ThenTryGet_ReturnsExecutor()
    {
        var registry = new ControlFlowExecutorRegistry();
        var cf = new FakeControlFlow { TypeKey = "control.fake" };
        registry.Register(cf);

        Assert.True(registry.TryGet("control.fake", out var found));
        Assert.Same(cf, found);
    }

    [Fact]
    public void TryGet_UnknownKey_ReturnsFalse()
    {
        var registry = new ControlFlowExecutorRegistry();
        Assert.False(registry.TryGet("nope", out var found));
        Assert.Null(found);
    }

    [Fact]
    public void Register_DuplicateKey_Throws()
    {
        var registry = new ControlFlowExecutorRegistry();
        registry.Register(new FakeControlFlow { TypeKey = "dup" });
        Assert.Throws<InvalidOperationException>(() => registry.Register(new FakeControlFlow { TypeKey = "dup" }));
    }
}
