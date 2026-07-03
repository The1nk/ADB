using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Execution;

public class ErrorHandlerExecutionTests
{
    private static BotAction Node(string typeKey, out Guid id, string? label = null)
    {
        id = Guid.NewGuid();
        return new BotAction { Id = id, TypeKey = typeKey, Label = label ?? typeKey };
    }

    private static ActionConnection Edge(Guid from, string port, Guid to)
        => new() { Id = Guid.NewGuid(), SourceActionId = from, SourcePort = port, TargetActionId = to, TargetPort = "in" };

    private static ActionExecutorRegistry BaseRegistry()
    {
        var execs = new ActionExecutorRegistry();
        execs.Register(new StartAction());
        execs.Register(new ErrorHandlerAction());
        return execs;
    }

    [Fact]
    public async Task UnhandledFailure_WithErrorHandler_RoutesRecoversAndSeedsErrorVars()
    {
        var start = Node("control.start", out var startId);
        var boom = Node("boom", out var boomId, "Do risky thing");
        var handler = Node(ErrorHandlerAction.Key, out var handlerId);
        var recover = Node("recover", out var recoverId);

        var bot = new Bot { Name = "eh" };
        bot.Actions.AddRange(new[] { start, boom, handler, recover });
        bot.Connections.Add(Edge(startId, "out", boomId));
        bot.Connections.Add(Edge(handlerId, "out", recoverId));

        Dictionary<string, object>? snapshot = null;
        var execs = BaseRegistry();
        execs.Register(new FakeExecutor { TypeKey = "boom", Behavior = _ => ActionResult.Fail("kaboom") });
        execs.Register(new FakeExecutor { TypeKey = "recover", Behavior = c => { snapshot = new(c.Context.Variables); return ActionResult.Ok("out"); } });

        var result = await new BotExecutor(execs).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(snapshot);
        Assert.Equal("kaboom", snapshot!["error.message"]);
        Assert.Equal("Do risky thing", snapshot["error.action"]);
        Assert.Equal(boomId.ToString(), snapshot["error.actionId"]);
        Assert.Equal("boom", snapshot["error.typeKey"]);
    }

    [Fact]
    public async Task UnhandledFailure_NoErrorHandler_RunFailsAsBefore()
    {
        var start = Node("control.start", out var startId);
        var boom = Node("boom", out var boomId);

        var bot = new Bot { Name = "no-eh" };
        bot.Actions.AddRange(new[] { start, boom });
        bot.Connections.Add(Edge(startId, "out", boomId));

        var execs = BaseRegistry();
        execs.Register(new FakeExecutor { TypeKey = "boom", Behavior = _ => ActionResult.Fail("kaboom") });

        var result = await new BotExecutor(execs).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.False(result.Success);
        Assert.Equal("kaboom", result.ErrorMessage);
    }

    [Fact]
    public async Task NodeOnFailure_TakesPrecedenceOverErrorHandler()
    {
        var start = Node("control.start", out var startId);
        var boom = Node("boom", out var boomId);
        var onFail = Node("onfail", out var onFailId);
        var handler = Node(ErrorHandlerAction.Key, out var handlerId);
        var handlerPath = Node("handlerpath", out var handlerPathId);

        var bot = new Bot { Name = "prec" };
        bot.Actions.AddRange(new[] { start, boom, onFail, handler, handlerPath });
        bot.Connections.Add(Edge(startId, "out", boomId));
        bot.Connections.Add(Edge(boomId, "onFailure", onFailId));
        bot.Connections.Add(Edge(handlerId, "out", handlerPathId));

        var onFailRan = false;
        var handlerRan = false;
        var execs = BaseRegistry();
        execs.Register(new FakeExecutor { TypeKey = "boom", Behavior = _ => ActionResult.Fail("x") });
        execs.Register(new FakeExecutor { TypeKey = "onfail", Behavior = _ => { onFailRan = true; return ActionResult.Ok("out"); } });
        execs.Register(new FakeExecutor { TypeKey = "handlerpath", Behavior = _ => { handlerRan = true; return ActionResult.Ok("out"); } });

        var result = await new BotExecutor(execs).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);
        Assert.True(onFailRan);
        Assert.False(handlerRan);
    }

    [Fact]
    public async Task ErrorHandler_ReentersUntilRecoveryFlowSucceeds()
    {
        var start = Node("control.start", out var startId);
        var work = Node("work", out var workId);
        var done = Node("done", out var doneId);
        var handler = Node(ErrorHandlerAction.Key, out var handlerId);

        var bot = new Bot { Name = "reentry" };
        bot.Actions.AddRange(new[] { start, work, done, handler });
        bot.Connections.Add(Edge(startId, "out", workId));
        bot.Connections.Add(Edge(workId, "out", doneId));
        bot.Connections.Add(Edge(handlerId, "out", workId));

        var attempts = 0;
        var execs = BaseRegistry();
        execs.Register(new FakeExecutor { TypeKey = "work", Behavior = _ => { attempts++; return attempts < 3 ? ActionResult.Fail("retry me") : ActionResult.Ok("out"); } });
        execs.Register(new FakeExecutor { TypeKey = "done", Behavior = _ => ActionResult.Ok(string.Empty) });

        var result = await new BotExecutor(execs).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task ParallelHalt_ReachesErrorHandler()
    {
        var start = Node("control.start", out var startId);
        var rp = Node(RunParallelAction.RunParallelTypeKey, out var rpId);
        rp.Config[RunParallelAction.BranchesKey] = 2;
        rp.Config[RunParallelAction.OnBranchFailureKey] = ParallelErrorStrategy.HaltAll.ToString();
        var good = Node("good", out var goodId);
        var bad = Node("bad", out var badId);
        var join = Node(JoinAction.JoinTypeKey, out var joinId);
        var handler = Node(ErrorHandlerAction.Key, out var handlerId);
        var recover = Node("recover", out var recoverId);

        var bot = new Bot { Name = "par-eh" };
        bot.Actions.AddRange(new[] { start, rp, good, bad, join, handler, recover });
        bot.Connections.Add(Edge(startId, "out", rpId));
        bot.Connections.Add(Edge(rpId, RunParallelAction.BranchPort(1), goodId));
        bot.Connections.Add(Edge(rpId, RunParallelAction.BranchPort(2), badId));
        bot.Connections.Add(Edge(goodId, "out", joinId));
        bot.Connections.Add(Edge(badId, "out", joinId));
        bot.Connections.Add(Edge(handlerId, "out", recoverId));

        var recovered = false;
        var execs = BaseRegistry();
        execs.Register(new FakeExecutor { TypeKey = "good", Behavior = _ => ActionResult.Ok("out") });
        execs.Register(new FakeExecutor { TypeKey = "bad", Behavior = _ => ActionResult.Fail("branch boom") });
        execs.Register(new FakeExecutor { TypeKey = "recover", Behavior = _ => { recovered = true; return ActionResult.Ok("out"); } });

        var result = await new BotExecutor(execs).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(recovered);
    }

    [Fact]
    public async Task NestedFailure_BubblesToParentErrorHandler()
    {
        var childStart = Node("control.start", out var csId);
        var childBoom = Node("boom", out var cbId);
        var child = new Bot { Id = Guid.NewGuid(), Name = "Child" };
        child.Actions.AddRange(new[] { childStart, childBoom });
        child.Connections.Add(Edge(csId, "out", cbId));

        var pStart = Node("control.start", out var psId);
        var card = new BotAction { Id = Guid.NewGuid(), TypeKey = NestedBotAction.NestedBotTypeKey, Label = "Sub", Config = { ["nestedBotId"] = child.Id.ToString() } };
        var handler = Node(ErrorHandlerAction.Key, out var hId);
        var recover = Node("recover", out var rId);
        var parent = new Bot { Id = Guid.NewGuid(), Name = "Parent" };
        parent.Actions.AddRange(new[] { pStart, card, handler, recover });
        parent.Connections.Add(Edge(psId, "out", card.Id));
        parent.Connections.Add(Edge(hId, "out", rId));

        var recovered = false;
        var execs = BaseRegistry();
        execs.Register(new FakeExecutor { TypeKey = "boom", Behavior = _ => ActionResult.Fail("child boom") });
        execs.Register(new FakeExecutor { TypeKey = "recover", Behavior = _ => { recovered = true; return ActionResult.Ok("out"); } });
        execs.Register(new NestedBotExecutor(execs));

        var options = new ExecutionOptions { NestedBotLibrary = new Dictionary<Guid, Bot> { [child.Id] = child } };
        var result = await new BotExecutor(execs).RunAsync(parent, options, null, default);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(recovered);
    }

    [Fact]
    public void BotGraph_ExposesErrorHandlerNode()
    {
        var start = Node("control.start", out _);
        var handler = Node(ErrorHandlerAction.Key, out var hId);
        var bot = new Bot { Name = "g" };
        bot.Actions.AddRange(new[] { start, handler });

        var graph = new BotGraph(bot);

        Assert.NotNull(graph.ErrorHandler);
        Assert.Equal(hId, graph.ErrorHandler!.Id);
    }

    [Fact]
    public void BotGraph_ErrorHandler_NotChosenAsEntryFallback()
    {
        var handler = Node(ErrorHandlerAction.Key, out _);
        var leaf = Node("work", out var wId);
        var bot = new Bot { Name = "g2" };
        bot.Actions.AddRange(new[] { handler, leaf });

        var graph = new BotGraph(bot);

        Assert.NotNull(graph.EntryPoint);
        Assert.Equal(wId, graph.EntryPoint!.Id);
    }
}
