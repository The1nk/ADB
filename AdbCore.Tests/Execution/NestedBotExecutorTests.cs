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
}
