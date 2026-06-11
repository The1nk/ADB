using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Execution;

public class NestedBotExecutorTests
{
    // A trivial leaf executor used inside nested bots: optionally sets a variable, optionally fails.
    private sealed class FakeLeaf : IActionDefinition, IActionExecutor
    {
        public string TypeKey => "test.leaf";
        public string DisplayName => "Leaf";
        public string Category => "Test";
        public string Description => "";
        public List<PortDefinition> InputPorts { get; } = new() { new() { Name = "in", Label = "In" } };
        public List<PortDefinition> OutputPorts { get; } = new() { new() { Name = "out", Label = "Out" } };
        public List<ConfigField> ConfigFields { get; } = new();
        public bool SupportsRetry => false;

        public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
        {
            if (ConfigValues.GetBool(context.Action.Config, "fail")) return Task.FromResult(ActionResult.Fail("boom"));
            var setName = ConfigValues.GetString(context.Action.Config, "set");
            if (!string.IsNullOrEmpty(setName)) context.Context.Variables[setName] = "done";
            return Task.FromResult(ActionResult.Ok("out"));
        }
    }

    private static ActionExecutorRegistry Registry(out ActionRegistry defs)
    {
        defs = new ActionRegistry();
        var execs = new ActionExecutorRegistry();
        var leaf = new FakeLeaf();
        defs.Register(leaf); execs.Register(leaf);
        defs.Register(new StartAction()); execs.Register(new StartAction());
        var nested = new NestedBotAction();
        defs.Register(nested);
        execs.Register(new NestedBotExecutor(execs));
        return execs;
    }

    private static Bot NestedBot(string name, bool fail = false, string? setVar = null)
    {
        var start = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.start" };
        var leaf = new BotAction { Id = Guid.NewGuid(), TypeKey = "test.leaf" };
        if (fail) leaf.Config["fail"] = true;
        if (setVar is not null) leaf.Config["set"] = setVar;
        var bot = new Bot { Id = Guid.NewGuid(), Name = name, Actions = { start, leaf } };
        bot.Connections.Add(new ActionConnection { SourceActionId = start.Id, SourcePort = "out", TargetActionId = leaf.Id, TargetPort = "in" });
        return bot;
    }

    private static async Task<ActionResult> RunCard(ActionExecutorRegistry execs, BotExecutionContext ctx, BotAction card)
    {
        var exec = new NestedBotExecutor(execs);
        return await exec.ExecuteAsync(new ActionExecutionContext(card, ctx, _ => { }), CancellationToken.None);
    }

    [Fact]
    public async Task Unassigned_Fails()
    {
        var execs = Registry(out _);
        var card = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.nestedBot" };
        var result = await RunCard(execs, new BotExecutionContext(), card);
        Assert.False(result.Success);
        Assert.Contains("no bot assigned", result.ErrorMessage);
    }

    [Fact]
    public async Task MissingId_Fails()
    {
        var execs = Registry(out _);
        var card = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.nestedBot", Config = { ["nestedBotId"] = Guid.NewGuid().ToString() } };
        var result = await RunCard(execs, new BotExecutionContext(), card);
        Assert.False(result.Success);
        Assert.Contains("not found", result.ErrorMessage);
    }

    [Fact]
    public async Task Success_RoutesOnSuccess()
    {
        var execs = Registry(out _);
        var nested = NestedBot("Child");
        var ctx = new BotExecutionContext { NestedBots = new Dictionary<Guid, Bot> { [nested.Id] = nested } };
        var card = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.nestedBot", Config = { ["nestedBotId"] = nested.Id.ToString() } };
        var result = await RunCard(execs, ctx, card);
        Assert.True(result.Success);
        Assert.Equal("onSuccess", result.OutputPort);
    }

    [Fact]
    public async Task ChildFailure_FailsCard()
    {
        var execs = Registry(out _);
        var nested = NestedBot("Child", fail: true);
        var ctx = new BotExecutionContext { NestedBots = new Dictionary<Guid, Bot> { [nested.Id] = nested } };
        var card = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.nestedBot", Config = { ["nestedBotId"] = nested.Id.ToString() } };
        var result = await RunCard(execs, ctx, card);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task ReceiveVars_MergesChildVarsBack()
    {
        var execs = Registry(out _);
        var nested = NestedBot("Child", setVar: "flag");
        var ctx = new BotExecutionContext { NestedBots = new Dictionary<Guid, Bot> { [nested.Id] = nested } };
        var card = new BotAction
        {
            Id = Guid.NewGuid(),
            TypeKey = "control.nestedBot",
            Config = { ["nestedBotId"] = nested.Id.ToString(), ["receiveVars"] = true },
        };
        var result = await RunCard(execs, ctx, card);
        Assert.True(result.Success);
        Assert.Equal("done", ctx.Variables["flag"]);
    }

    [Fact]
    public async Task ReceiveVarsOff_DoesNotLeak()
    {
        var execs = Registry(out _);
        var nested = NestedBot("Child", setVar: "flag");
        var ctx = new BotExecutionContext { NestedBots = new Dictionary<Guid, Bot> { [nested.Id] = nested } };
        var card = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.nestedBot", Config = { ["nestedBotId"] = nested.Id.ToString() } };
        await RunCard(execs, ctx, card);
        Assert.False(ctx.Variables.ContainsKey("flag"));
    }

    [Fact]
    public async Task Cycle_IsDetected()
    {
        var execs = Registry(out _);
        var nested = NestedBot("Child");
        var ctx = new BotExecutionContext
        {
            NestedBots = new Dictionary<Guid, Bot> { [nested.Id] = nested },
            NestedAncestry = new[] { nested.Id }, // already running
        };
        var card = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.nestedBot", Config = { ["nestedBotId"] = nested.Id.ToString() } };
        var result = await RunCard(execs, ctx, card);
        Assert.False(result.Success);
        Assert.Contains("cycle", result.ErrorMessage);
    }

    // -------------------------------------------------------------------------
    // Nit 1: OverlayParentTargetsByName — behavioral coverage via sendTargets
    // -------------------------------------------------------------------------

    // A leaf that records whether the execution context's Targets dictionary
    // contains any entry whose Handle is reference-equal to a sentinel, writing
    // "true"/"false" into a named variable so receiveVars can surface it.
    private sealed class FakeTargetProbe : IActionDefinition, IActionExecutor
    {
        private readonly object _sentinel;
        private readonly string _resultVar;

        public FakeTargetProbe(object sentinel, string resultVar)
        {
            _sentinel = sentinel;
            _resultVar = resultVar;
        }

        public string TypeKey => "test.targetProbe";
        public string DisplayName => "TargetProbe";
        public string Category => "Test";
        public string Description => "";
        public List<PortDefinition> InputPorts { get; } = new() { new() { Name = "in", Label = "In" } };
        public List<PortDefinition> OutputPorts { get; } = new() { new() { Name = "out", Label = "Out" } };
        public List<ConfigField> ConfigFields { get; } = new();
        public bool SupportsRetry => false;

        public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
        {
            bool found = context.Context.Targets.Values.Any(t => ReferenceEquals(t.Handle, _sentinel));
            context.Context.Variables[_resultVar] = found ? "true" : "false";
            return Task.FromResult(ActionResult.Ok("out"));
        }
    }

    [Fact]
    public async Task SendTargets_OverlaysMatchingParentHandleByName()
    {
        // Arrange: sentinel handle object — any reference type works.
        var sentinel = new object();
        var parentTargetId = Guid.NewGuid();

        // Nested bot has its OWN target with the same name "Main" but a different Guid.
        var nestedTargetId = Guid.NewGuid();
        var probe = new FakeTargetProbe(sentinel, "gotTarget");

        // Build a nested bot: Start -> probe leaf
        var nStart = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.start" };
        var nLeaf = new BotAction { Id = Guid.NewGuid(), TypeKey = "test.targetProbe" };
        var nestedBot = new Bot
        {
            Id = Guid.NewGuid(),
            Name = "NestedWithMain",
            Targets = { new BotTarget { Id = nestedTargetId, Name = "Main" } },
            Actions = { nStart, nLeaf },
        };
        nestedBot.Connections.Add(new ActionConnection
        {
            SourceActionId = nStart.Id, SourcePort = "out",
            TargetActionId = nLeaf.Id, TargetPort = "in",
        });

        // Build executor registry: standard leaf + the probe.
        var defs = new ActionRegistry();
        var execs = new ActionExecutorRegistry();
        var leaf = new FakeLeaf();
        defs.Register(leaf); execs.Register(leaf);
        defs.Register(new StartAction()); execs.Register(new StartAction());
        defs.Register(probe); execs.Register(probe);
        defs.Register(new NestedBotAction());
        execs.Register(new NestedBotExecutor(execs));

        // Parent context: parentTargetId maps to "Main" and carries the sentinel handle.
        var parentCtx = new BotExecutionContext
        {
            NestedBots = new Dictionary<Guid, Bot> { [nestedBot.Id] = nestedBot },
        };
        parentCtx.Targets[parentTargetId] = new ResolvedTarget
        {
            Selector = "process:Game",
            Handle = sentinel,
        };
        parentCtx.TargetNames = new Dictionary<Guid, string> { [parentTargetId] = "Main" };

        var card = new BotAction
        {
            Id = Guid.NewGuid(),
            TypeKey = "control.nestedBot",
            Config =
            {
                ["nestedBotId"] = nestedBot.Id.ToString(),
                ["sendTargets"] = true,
                ["receiveVars"] = true,
            },
        };

        // Act (positive case: sendTargets=true)
        var result = await RunCard(execs, parentCtx, card);

        // Assert: nested run saw the sentinel.
        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(parentCtx.Variables.TryGetValue("gotTarget", out var val));
        Assert.Equal("true", val);
    }

    [Fact]
    public async Task SendTargets_Off_NestedRunSeesNoParentHandle()
    {
        // Same setup as above, but sendTargets=false — nested leaf must NOT see the sentinel.
        var sentinel = new object();
        var parentTargetId = Guid.NewGuid();
        var nestedTargetId = Guid.NewGuid();
        var probe = new FakeTargetProbe(sentinel, "gotTarget");

        var nStart = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.start" };
        var nLeaf = new BotAction { Id = Guid.NewGuid(), TypeKey = "test.targetProbe" };
        var nestedBot = new Bot
        {
            Id = Guid.NewGuid(),
            Name = "NestedWithMain",
            Targets = { new BotTarget { Id = nestedTargetId, Name = "Main" } },
            Actions = { nStart, nLeaf },
        };
        nestedBot.Connections.Add(new ActionConnection
        {
            SourceActionId = nStart.Id, SourcePort = "out",
            TargetActionId = nLeaf.Id, TargetPort = "in",
        });

        var defs = new ActionRegistry();
        var execs = new ActionExecutorRegistry();
        var leaf = new FakeLeaf();
        defs.Register(leaf); execs.Register(leaf);
        defs.Register(new StartAction()); execs.Register(new StartAction());
        defs.Register(probe); execs.Register(probe);
        defs.Register(new NestedBotAction());
        execs.Register(new NestedBotExecutor(execs));

        var parentCtx = new BotExecutionContext
        {
            NestedBots = new Dictionary<Guid, Bot> { [nestedBot.Id] = nestedBot },
        };
        parentCtx.Targets[parentTargetId] = new ResolvedTarget { Selector = "process:Game", Handle = sentinel };
        parentCtx.TargetNames = new Dictionary<Guid, string> { [parentTargetId] = "Main" };

        var card = new BotAction
        {
            Id = Guid.NewGuid(),
            TypeKey = "control.nestedBot",
            Config =
            {
                ["nestedBotId"] = nestedBot.Id.ToString(),
                // sendTargets intentionally omitted (defaults false)
                ["receiveVars"] = true,
            },
        };

        // Act (negative case: sendTargets=false)
        var result = await RunCard(execs, parentCtx, card);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(parentCtx.Variables.TryGetValue("gotTarget", out var val));
        Assert.Equal("false", val);
    }

    // -------------------------------------------------------------------------
    // Nit 2: Transitive cycle A -> B -> A terminates with a cycle failure
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TransitiveCycle_ARefersB_BRefersA_Terminates()
    {
        var execs = Registry(out _);

        // Build A and B in a forward-reference pattern.
        // We need their Ids up-front so each can reference the other.
        var botAId = Guid.NewGuid();
        var botBId = Guid.NewGuid();

        // Bot A: Start -> NestedBot(botBId)
        var aStart = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.start" };
        var aCard = new BotAction
        {
            Id = Guid.NewGuid(),
            TypeKey = "control.nestedBot",
            Config = { ["nestedBotId"] = botBId.ToString() },
        };
        var botA = new Bot { Id = botAId, Name = "BotA", Actions = { aStart, aCard } };
        botA.Connections.Add(new ActionConnection
        {
            SourceActionId = aStart.Id, SourcePort = "out",
            TargetActionId = aCard.Id, TargetPort = "in",
        });

        // Bot B: Start -> NestedBot(botAId)
        var bStart = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.start" };
        var bCard = new BotAction
        {
            Id = Guid.NewGuid(),
            TypeKey = "control.nestedBot",
            Config = { ["nestedBotId"] = botAId.ToString() },
        };
        var botB = new Bot { Id = botBId, Name = "BotB", Actions = { bStart, bCard } };
        botB.Connections.Add(new ActionConnection
        {
            SourceActionId = bStart.Id, SourcePort = "out",
            TargetActionId = bCard.Id, TargetPort = "in",
        });

        // Library that both bots can see.
        var library = new Dictionary<Guid, Bot>
        {
            [botAId] = botA,
            [botBId] = botB,
        };

        // Run A as the top-level entry via BotExecutor with the shared library.
        var options = new ExecutionOptions { NestedBotLibrary = library };
        var result = await new BotExecutor(execs).RunAsync(botA, options, null, CancellationToken.None);

        // Must terminate (test completes; no stack overflow) and report a cycle failure.
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("cycle", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // Nit 3: Failure routing — child error message is surfaced on the card result
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ChildFailure_ErrorMessageIsSurfaced()
    {
        var execs = Registry(out _);
        var nested = NestedBot("Child", fail: true);
        var ctx = new BotExecutionContext { NestedBots = new Dictionary<Guid, Bot> { [nested.Id] = nested } };
        var card = new BotAction
        {
            Id = Guid.NewGuid(),
            TypeKey = "control.nestedBot",
            Config = { ["nestedBotId"] = nested.Id.ToString() },
        };
        var result = await RunCard(execs, ctx, card);

        Assert.False(result.Success);
        Assert.False(string.IsNullOrEmpty(result.ErrorMessage));
    }
}
