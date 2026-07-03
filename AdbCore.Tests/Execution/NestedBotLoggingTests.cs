using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Execution;

public class NestedBotLoggingTests
{
    // Leaf that (a) optionally fails, (b) optionally emits a Log-sink message from config "log".
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
            var msg = ConfigValues.GetString(context.Action.Config, "log");
            if (!string.IsNullOrEmpty(msg)) context.Log(msg);
            if (ConfigValues.GetBool(context.Action.Config, "fail")) return Task.FromResult(ActionResult.Fail("boom"));
            return Task.FromResult(ActionResult.Ok("out"));
        }
    }

    private static ActionExecutorRegistry Registry()
    {
        var defs = new ActionRegistry();
        var execs = new ActionExecutorRegistry();
        var leaf = new FakeLeaf();
        defs.Register(leaf); execs.Register(leaf);
        defs.Register(new StartAction()); execs.Register(new StartAction());
        defs.Register(new NestedBotAction());
        execs.Register(new NestedBotExecutor(execs));
        return execs;
    }

    // Start -> leaf(label, optional fail / optional log message).
    private static Bot NestedBot(string name, string leafLabel, bool fail = false, string? logMessage = null)
    {
        var start = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.start" };
        var leaf = new BotAction { Id = Guid.NewGuid(), TypeKey = "test.leaf", Label = leafLabel };
        if (fail) leaf.Config["fail"] = true;
        if (logMessage is not null) leaf.Config["log"] = logMessage;
        var bot = new Bot { Id = Guid.NewGuid(), Name = name, Actions = { start, leaf } };
        bot.Connections.Add(new ActionConnection { SourceActionId = start.Id, SourcePort = "out", TargetActionId = leaf.Id, TargetPort = "in" });
        return bot;
    }

    private static BotAction Card(Guid nestedBotId) => new()
    {
        Id = Guid.NewGuid(),
        TypeKey = "control.nestedBot",
        Config = { ["nestedBotId"] = nestedBotId.ToString() },
    };

    private static async Task<List<string>> RunCardCapturingLog(ActionExecutorRegistry execs, BotExecutionContext ctx, BotAction card)
    {
        var log = new List<string>();
        var exec = new NestedBotExecutor(execs);
        await exec.ExecuteAsync(new ActionExecutionContext(card, ctx, log.Add), CancellationToken.None);
        return log;
    }

    [Fact]
    public async Task NestedAction_EmitsPrefixedSuccessLine()
    {
        var execs = Registry();
        var nested = NestedBot("Login", "Click username");
        var ctx = new BotExecutionContext { NestedBots = new Dictionary<Guid, Bot> { [nested.Id] = nested } };

        var log = await RunCardCapturingLog(execs, ctx, Card(nested.Id));

        Assert.Contains("[Login] ✓ Click username", log);
    }

    [Fact]
    public async Task NestedAction_Failure_EmitsPrefixedCrossLineWithError()
    {
        var execs = Registry();
        var nested = NestedBot("Login", "Click submit", fail: true);
        var ctx = new BotExecutionContext { NestedBots = new Dictionary<Guid, Bot> { [nested.Id] = nested } };

        var log = await RunCardCapturingLog(execs, ctx, Card(nested.Id));

        Assert.Contains("[Login] ✗ Click submit: boom", log);
    }

    [Fact]
    public async Task ExplicitLogAction_InsideNestedBot_IsPrefixed()
    {
        var execs = Registry();
        var nested = NestedBot("Login", "Say", logMessage: "logging in");
        var ctx = new BotExecutionContext { NestedBots = new Dictionary<Guid, Bot> { [nested.Id] = nested } };

        var log = await RunCardCapturingLog(execs, ctx, Card(nested.Id));

        Assert.Contains("[Login] logging in", log);
    }

    [Fact]
    public async Task BlankNestedBotName_FallsBackToNestedPrefix()
    {
        var execs = Registry();
        var nested = NestedBot("", "Do thing");
        var ctx = new BotExecutionContext { NestedBots = new Dictionary<Guid, Bot> { [nested.Id] = nested } };

        var log = await RunCardCapturingLog(execs, ctx, Card(nested.Id));

        Assert.Contains("[Nested] ✓ Do thing", log);
    }

    [Fact]
    public async Task DeepNesting_ComposesPrefixes()
    {
        // outer(top) -> card(A); A -> card(B); B -> leaf. Running outer top-level, B's leaf line is [A] [B] prefixed.
        var execs = Registry();

        var botB = NestedBot("B", "deep click");

        var bStart = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.start" };
        var bCard = Card(botB.Id);
        var botA = new Bot { Id = Guid.NewGuid(), Name = "A", Actions = { bStart, bCard } };
        botA.Connections.Add(new ActionConnection { SourceActionId = bStart.Id, SourcePort = "out", TargetActionId = bCard.Id, TargetPort = "in" });

        var oStart = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.start" };
        var aCard = Card(botA.Id);
        var outer = new Bot { Id = Guid.NewGuid(), Name = "Outer", Actions = { oStart, aCard } };
        outer.Connections.Add(new ActionConnection { SourceActionId = oStart.Id, SourcePort = "out", TargetActionId = aCard.Id, TargetPort = "in" });

        var library = new Dictionary<Guid, Bot> { [botA.Id] = botA, [botB.Id] = botB };
        var log = new List<string>();
        var options = new ExecutionOptions { NestedBotLibrary = library, Log = log.Add };

        var result = await new BotExecutor(execs).RunAsync(outer, options, progress: null, CancellationToken.None);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Contains("[A] [B] ✓ deep click", log);
    }

    [Fact]
    public async Task LineOrdering_IsDeterministic()
    {
        // Two leaves in sequence produce lines in execution order (synchronous progress).
        var execs = Registry();
        var start = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.start" };
        var first = new BotAction { Id = Guid.NewGuid(), TypeKey = "test.leaf", Label = "first" };
        var second = new BotAction { Id = Guid.NewGuid(), TypeKey = "test.leaf", Label = "second" };
        var nested = new Bot { Id = Guid.NewGuid(), Name = "Seq", Actions = { start, first, second } };
        nested.Connections.Add(new ActionConnection { SourceActionId = start.Id, SourcePort = "out", TargetActionId = first.Id, TargetPort = "in" });
        nested.Connections.Add(new ActionConnection { SourceActionId = first.Id, SourcePort = "out", TargetActionId = second.Id, TargetPort = "in" });
        var ctx = new BotExecutionContext { NestedBots = new Dictionary<Guid, Bot> { [nested.Id] = nested } };

        var log = await RunCardCapturingLog(execs, ctx, Card(nested.Id));

        var firstIdx = log.IndexOf("[Seq] ✓ first");
        var secondIdx = log.IndexOf("[Seq] ✓ second");
        Assert.True(firstIdx >= 0 && secondIdx >= 0);
        Assert.True(firstIdx < secondIdx);
    }
}
