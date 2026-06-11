using AdbCore.Actions;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Execution;

public class TargetBinderPlumbingTests
{
    private sealed class FakeBinder : ITargetBinder
    {
        public Task<ResolvedTarget> BindAsync(BotTarget target, CancellationToken ct)
            => Task.FromResult(new ResolvedTarget { Type = target.Type, Selector = target.Selector });
    }

    // A leaf that records whether the run context carries a TargetBinder.
    private sealed class ProbeLeaf : IActionDefinition, IActionExecutor
    {
        public string TypeKey => "test.probeBinder";
        public string DisplayName => "Probe";
        public string Category => "Test";
        public string Description => "";
        public List<PortDefinition> InputPorts { get; } = new() { new() { Name = "in", Label = "In" } };
        public List<PortDefinition> OutputPorts { get; } = new() { new() { Name = "out", Label = "Out" } };
        public List<ConfigField> ConfigFields { get; } = new();
        public bool SupportsRetry => false;

        public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
        {
            context.Context.Variables["hasBinder"] = context.Context.TargetBinder is not null;
            return Task.FromResult(ActionResult.Ok("out"));
        }
    }

    [Fact]
    public async Task TargetBinder_FlowsFromOptionsToContext()
    {
        var probe = new ProbeLeaf();
        var defs = new ActionRegistry();
        var execs = new ActionExecutorRegistry();
        defs.Register(probe); execs.Register(probe);

        var bot = new Bot { Id = Guid.NewGuid(), Name = "B", Actions = { new BotAction { Id = Guid.NewGuid(), TypeKey = "test.probeBinder" } } };
        var options = new ExecutionOptions { TargetBinder = new FakeBinder() };

        var result = await new BotExecutor(execs).RunAsync(bot, options, null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.True((bool)result.FinalVariables["hasBinder"]);
    }
}
