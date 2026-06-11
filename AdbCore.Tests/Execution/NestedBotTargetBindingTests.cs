using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Execution;

public class NestedBotTargetBindingTests
{
    // Records bind calls and hands back a disposable handle so we can prove disposal.
    private sealed class RecordingBinder : ITargetBinder
    {
        public List<string> BoundSelectors { get; } = new();
        public List<DisposableHandle> Created { get; } = new();

        public Task<ResolvedTarget> BindAsync(BotTarget target, CancellationToken ct)
        {
            BoundSelectors.Add(target.Selector);
            var handle = new DisposableHandle();
            Created.Add(handle);
            return Task.FromResult(new ResolvedTarget { Type = target.Type, Selector = target.Selector, Handle = handle });
        }
    }

    private sealed class DisposableHandle : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    // Leaf that records, into a variable, whether a resolved target with the given id is present in the run.
    private sealed class TargetProbeLeaf : IActionDefinition, IActionExecutor
    {
        public string TypeKey => "test.targetProbe";
        public string DisplayName => "Probe";
        public string Category => "Test";
        public string Description => "";
        public List<PortDefinition> InputPorts { get; } = new() { new() { Name = "in", Label = "In" } };
        public List<PortDefinition> OutputPorts { get; } = new() { new() { Name = "out", Label = "Out" } };
        public List<ConfigField> ConfigFields { get; } = new();
        public bool SupportsRetry => false;

        public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
        {
            context.Context.Variables["targetCount"] = context.Context.Targets.Count;
            return Task.FromResult(ActionResult.Ok("out"));
        }
    }

    private static (ActionExecutorRegistry execs, TargetProbeLeaf probe) Registry()
    {
        var defs = new ActionRegistry();
        var execs = new ActionExecutorRegistry();
        var probe = new TargetProbeLeaf();
        defs.Register(probe); execs.Register(probe);
        defs.Register(new StartAction()); execs.Register(new StartAction());
        defs.Register(new NestedBotAction()); execs.Register(new NestedBotExecutor(execs));
        return (execs, probe);
    }

    private static Bot NestedBotWithOwnTarget(out Guid targetId)
    {
        targetId = Guid.NewGuid();
        var start = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.start" };
        var probe = new BotAction { Id = Guid.NewGuid(), TypeKey = "test.targetProbe" };
        var bot = new Bot
        {
            Id = Guid.NewGuid(),
            Name = "Child",
            Targets = { new BotTarget { Id = targetId, Name = "Own", Type = BotTargetType.Window, Selector = "title:Game" } },
            Actions = { start, probe },
        };
        bot.Connections.Add(new ActionConnection { SourceActionId = start.Id, SourcePort = "out", TargetActionId = probe.Id, TargetPort = "in" });
        return bot;
    }

    private static BotAction Card(Guid nestedId, bool receiveVars = true, bool sendTargets = false)
        => new()
        {
            Id = Guid.NewGuid(),
            TypeKey = "control.nestedBot",
            Config =
            {
                ["nestedBotId"] = nestedId.ToString(),
                ["receiveVars"] = receiveVars,
                ["sendTargets"] = sendTargets,
            },
        };

    private static async Task<ActionResult> RunCard(ActionExecutorRegistry execs, BotExecutionContext ctx, BotAction card)
    {
        var exec = new NestedBotExecutor(execs);
        return await exec.ExecuteAsync(new ActionExecutionContext(card, ctx, _ => { }), CancellationToken.None);
    }

    [Fact]
    public async Task OwnTarget_IsLazilyBound_AndDisposedAfterRun()
    {
        var (execs, _) = Registry();
        var nested = NestedBotWithOwnTarget(out _);
        var binder = new RecordingBinder();
        var ctx = new BotExecutionContext
        {
            NestedBots = new Dictionary<Guid, Bot> { [nested.Id] = nested },
            TargetBinder = binder,
        };

        var result = await RunCard(execs, ctx, Card(nested.Id));

        Assert.True(result.Success);
        Assert.Equal(new[] { "title:Game" }, binder.BoundSelectors.ToArray()); // bound its own target
        Assert.Equal(1, Convert.ToInt32(ctx.Variables["targetCount"]));        // child saw the resolved target
        Assert.True(binder.Created.Single().Disposed);                         // child-created handle disposed
    }

    [Fact]
    public async Task NoBinder_OwnTargetStaysUnresolved()
    {
        var (execs, _) = Registry();
        var nested = NestedBotWithOwnTarget(out _);
        var ctx = new BotExecutionContext { NestedBots = new Dictionary<Guid, Bot> { [nested.Id] = nested } };

        var result = await RunCard(execs, ctx, Card(nested.Id));

        Assert.True(result.Success);
        Assert.Equal(0, Convert.ToInt32(ctx.Variables["targetCount"])); // nothing bound, no binder
    }

    [Fact]
    public async Task SharedParentTarget_IsReused_AndNotDisposed()
    {
        var (execs, _) = Registry();
        // Nested target NAME "Own" — make a parent target of the same name so it overlays instead of binding.
        var nested = NestedBotWithOwnTarget(out _);
        nested.Targets[0].Name = "Shared";

        var parentId = Guid.NewGuid();
        var parentHandle = new DisposableHandle();
        var binder = new RecordingBinder();
        var ctx = new BotExecutionContext
        {
            NestedBots = new Dictionary<Guid, Bot> { [nested.Id] = nested },
            TargetNames = new Dictionary<Guid, string> { [parentId] = "Shared" },
            TargetBinder = binder,
        };
        ctx.Targets[parentId] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "title:Parent", Handle = parentHandle };

        var result = await RunCard(execs, ctx, Card(nested.Id, sendTargets: true));

        Assert.True(result.Success);
        Assert.Empty(binder.BoundSelectors);          // overlaid from parent — binder NOT called
        Assert.False(parentHandle.Disposed);          // parent handle must NOT be disposed by the child
        Assert.Equal(1, Convert.ToInt32(ctx.Variables["targetCount"]));
    }

    [Fact]
    public async Task BinderThrows_CardFails_AndPartialHandlesDisposed()
    {
        var (execs, _) = Registry();
        var nested = NestedBotWithOwnTarget(out _);
        var throwingBinder = new ThrowingBinder();
        var ctx = new BotExecutionContext
        {
            NestedBots = new Dictionary<Guid, Bot> { [nested.Id] = nested },
            TargetBinder = throwingBinder,
        };

        var result = await RunCard(execs, ctx, Card(nested.Id));

        Assert.False(result.Success);
        Assert.Contains("target", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ThrowingBinder : ITargetBinder
    {
        public Task<ResolvedTarget> BindAsync(BotTarget target, CancellationToken ct)
            => throw new InvalidOperationException("could not resolve window 'title:Game'");
    }
}
