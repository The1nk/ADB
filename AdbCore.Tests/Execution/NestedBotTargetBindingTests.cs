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
    public async Task BinderThrows_CardFails()
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

    [Fact]
    public async Task BinderThrowsOnSecondTarget_FirstHandleStillDisposed()
    {
        // Arrange: two own targets; binder succeeds on the first bind and throws on the second.
        var (execs, _) = Registry();
        var nested = NestedBotWithTwoOwnTargets();
        var binder = new PartialThrowingBinder(succeedCount: 1);
        var ctx = new BotExecutionContext
        {
            NestedBots = new Dictionary<Guid, Bot> { [nested.Id] = nested },
            TargetBinder = binder,
        };

        var result = await RunCard(execs, ctx, Card(nested.Id));

        // Card must fail because the second bind threw.
        Assert.False(result.Success);
        // The first handle (created before the throw) must have been disposed by the finally block.
        Assert.True(binder.FirstHandle!.Disposed, "partial handle created before the throw must be disposed");
    }

    [Fact]
    public async Task BinderCancellation_Propagates()
    {
        // Arrange: two own targets; binder succeeds on the first bind and throws OCE on the second.
        var (execs, _) = Registry();
        var nested = NestedBotWithTwoOwnTargets();
        var binder = new PartialThrowingBinder(succeedCount: 1, cancelOnOverflow: true);
        var ctx = new BotExecutionContext
        {
            NestedBots = new Dictionary<Guid, Bot> { [nested.Id] = nested },
            TargetBinder = binder,
        };

        // OCE must propagate out of ExecuteAsync (not be swallowed into a failed ActionResult).
        await Assert.ThrowsAsync<OperationCanceledException>(() => RunCard(execs, ctx, Card(nested.Id)));

        // The finally block must still have disposed the handle that was created before the cancellation.
        Assert.True(binder.FirstHandle!.Disposed, "partial handle created before cancellation must be disposed");
    }

    // Builds a nested bot with TWO own targets ("OwnA" and "OwnB"), neither matching any parent target.
    private static Bot NestedBotWithTwoOwnTargets()
    {
        var start = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.start" };
        var probe = new BotAction { Id = Guid.NewGuid(), TypeKey = "test.targetProbe" };
        var bot = new Bot
        {
            Id = Guid.NewGuid(),
            Name = "Child",
            Targets =
            {
                new BotTarget { Id = Guid.NewGuid(), Name = "OwnA", Type = BotTargetType.Window, Selector = "title:A" },
                new BotTarget { Id = Guid.NewGuid(), Name = "OwnB", Type = BotTargetType.Window, Selector = "title:B" },
            },
            Actions = { start, probe },
        };
        bot.Connections.Add(new ActionConnection { SourceActionId = start.Id, SourcePort = "out", TargetActionId = probe.Id, TargetPort = "in" });
        return bot;
    }

    // Binder that succeeds for the first <succeedCount> calls (returning a DisposableHandle each time),
    // then either throws InvalidOperationException or OperationCanceledException on the next call.
    private sealed class PartialThrowingBinder : ITargetBinder
    {
        private readonly int _succeedCount;
        private readonly bool _cancelOnOverflow;
        private int _calls;

        public DisposableHandle? FirstHandle { get; private set; }

        public PartialThrowingBinder(int succeedCount, bool cancelOnOverflow = false)
        {
            _succeedCount = succeedCount;
            _cancelOnOverflow = cancelOnOverflow;
        }

        public Task<ResolvedTarget> BindAsync(BotTarget target, CancellationToken ct)
        {
            _calls++;
            if (_calls <= _succeedCount)
            {
                var handle = new DisposableHandle();
                if (_calls == 1)
                    FirstHandle = handle;
                return Task.FromResult(new ResolvedTarget { Type = target.Type, Selector = target.Selector, Handle = handle });
            }

            if (_cancelOnOverflow)
                throw new OperationCanceledException("cancelled during bind");

            throw new InvalidOperationException($"could not resolve window '{target.Selector}'");
        }
    }

    private sealed class ThrowingBinder : ITargetBinder
    {
        public Task<ResolvedTarget> BindAsync(BotTarget target, CancellationToken ct)
            => throw new InvalidOperationException("could not resolve window 'title:Game'");
    }
}
