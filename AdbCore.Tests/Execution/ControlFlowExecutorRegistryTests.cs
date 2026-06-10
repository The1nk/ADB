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

    [Fact]
    public void CreateDefault_RegistersLoopAndParallel()
    {
        var registry = ControlFlowExecutorRegistry.CreateDefault();

        Assert.Equal(3, registry.Count);
        Assert.True(registry.TryGet(AdbCore.Actions.BuiltIn.LoopAction.LoopTypeKey, out var loop));
        Assert.IsType<AdbCore.Execution.ControlFlow.LoopControlFlowExecutor>(loop);
        Assert.True(registry.TryGet(AdbCore.Actions.BuiltIn.LoopBreakAction.LoopBreakTypeKey, out var loopBreak));
        Assert.IsType<AdbCore.Execution.ControlFlow.LoopBreakControlFlowExecutor>(loopBreak);
        Assert.True(registry.TryGet(AdbCore.Actions.BuiltIn.RunParallelAction.RunParallelTypeKey, out var parallel));
        Assert.IsType<AdbCore.Execution.ControlFlow.ParallelControlFlowExecutor>(parallel);
    }

    [Fact]
    public async Task BotExecutor_DispatchesControlFlowNodeThroughInjectedRegistry()
    {
        // A control-flow node with a custom TypeKey that no ActionExecutor handles. If BotExecutor consulted
        // only the action registry it would fail ("No executor registered"); dispatching through the injected
        // control-flow registry proves the registry-driven path is live and the ctor param is wired.
        var ran = false;
        var customKey = "control.custom-test";

        var cfRegistry = new ControlFlowExecutorRegistry();
        cfRegistry.Register(new RecordingControlFlow
        {
            TypeKey = customKey,
            OnExecute = () => ran = true,
        });

        var node = new BotAction { Id = Guid.NewGuid(), TypeKey = customKey, Label = customKey };
        var bot = new Bot { Name = "cf-dispatch" };
        bot.Actions.Add(node);

        var result = await new BotExecutor(new ActionExecutorRegistry(), cfRegistry)
            .RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(ran);
        Assert.True(result.Success);
    }

    private sealed class RecordingControlFlow : IControlFlowExecutor
    {
        public required string TypeKey { get; init; }
        public required Action OnExecute { get; init; }

        public Task<ControlFlowResult> ExecuteAsync(ControlFlowContext context, CancellationToken ct)
        {
            OnExecute();
            // No wired output → end the walk successfully.
            return Task.FromResult(ControlFlowResult.Continue(null));
        }
    }
}
