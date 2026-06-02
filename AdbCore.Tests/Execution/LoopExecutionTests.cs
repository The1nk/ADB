using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Execution;

public class LoopExecutionTests
{
    private static BotAction Node(string typeKey, out Guid id)
    {
        id = Guid.NewGuid();
        return new BotAction { Id = id, TypeKey = typeKey, Label = typeKey };
    }

    private static ActionConnection Edge(Guid from, string port, Guid to)
        => new() { Id = Guid.NewGuid(), SourceActionId = from, SourcePort = port, TargetActionId = to, TargetPort = "in" };

    [Fact]
    public async Task Loop_Count_RunsBodyNTimesThenFollowsDone()
    {
        var loop = Node(LoopAction.LoopTypeKey, out var loopId);
        loop.Config[LoopAction.ModeKey] = LoopAction.ModeCount;
        loop.Config[LoopAction.CountKey] = 3;
        var body = Node("body", out var bodyId);
        var done = Node("done", out var doneId);

        var bot = new Bot { Name = "loop-count" };
        bot.Actions.AddRange(new[] { loop, body, done });
        bot.Connections.Add(Edge(loopId, LoopAction.BodyPort, bodyId));
        bot.Connections.Add(Edge(loopId, LoopAction.DonePort, doneId));

        var bodyCalls = 0;
        var doneReached = false;
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "body", Behavior = c => { bodyCalls++; return ActionResult.Ok(string.Empty); } });
        registry.Register(new FakeExecutor { TypeKey = "done", Behavior = c => { doneReached = true; return ActionResult.Ok(string.Empty); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);
        Assert.Equal(3, bodyCalls);
        Assert.True(doneReached);
        Assert.Equal(4, result.ActionsExecuted); // 3 body + 1 done; the loop node itself is not counted
    }

    [Fact]
    public async Task Loop_CountZero_SkipsBodyButFollowsDone()
    {
        var loop = Node(LoopAction.LoopTypeKey, out var loopId);
        loop.Config[LoopAction.ModeKey] = LoopAction.ModeCount;
        loop.Config[LoopAction.CountKey] = 0;
        var body = Node("body", out var bodyId);
        var done = Node("done", out var doneId);

        var bot = new Bot { Name = "loop-zero" };
        bot.Actions.AddRange(new[] { loop, body, done });
        bot.Connections.Add(Edge(loopId, LoopAction.BodyPort, bodyId));
        bot.Connections.Add(Edge(loopId, LoopAction.DonePort, doneId));

        var bodyCalls = 0;
        var doneReached = false;
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "body", Behavior = c => { bodyCalls++; return ActionResult.Ok(string.Empty); } });
        registry.Register(new FakeExecutor { TypeKey = "done", Behavior = c => { doneReached = true; return ActionResult.Ok(string.Empty); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);
        Assert.Equal(0, bodyCalls);
        Assert.True(doneReached);
    }

    [Fact]
    public async Task Loop_SetsIndexVariableEachIteration()
    {
        var loop = Node(LoopAction.LoopTypeKey, out var loopId);
        loop.Config[LoopAction.ModeKey] = LoopAction.ModeCount;
        loop.Config[LoopAction.CountKey] = 3;
        loop.Config[LoopAction.IndexVariableKey] = "i";
        var body = Node("body", out var bodyId);

        var bot = new Bot { Name = "loop-index" };
        bot.Actions.AddRange(new[] { loop, body });
        bot.Connections.Add(Edge(loopId, LoopAction.BodyPort, bodyId));

        var seen = new List<int>();
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor
        {
            TypeKey = "body",
            Behavior = c => { seen.Add(ConfigValues.GetIntVar(c.Context.Variables, "i")); return ActionResult.Ok(string.Empty); },
        });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);
        Assert.Equal(new[] { 0, 1, 2 }, seen);
    }

    [Fact]
    public async Task Loop_EmptyBody_StillCompletes()
    {
        var loop = Node(LoopAction.LoopTypeKey, out var loopId);
        loop.Config[LoopAction.ModeKey] = LoopAction.ModeCount;
        loop.Config[LoopAction.CountKey] = 2;
        var done = Node("done", out var doneId);

        var bot = new Bot { Name = "loop-empty-body" };
        bot.Actions.AddRange(new[] { loop, done });
        bot.Connections.Add(Edge(loopId, LoopAction.DonePort, doneId)); // no body edge

        var doneReached = false;
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "done", Behavior = c => { doneReached = true; return ActionResult.Ok(string.Empty); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);
        Assert.True(doneReached);
    }

    [Fact]
    public async Task Loop_BodyFailure_HaltsRun()
    {
        var loop = Node(LoopAction.LoopTypeKey, out var loopId);
        loop.Config[LoopAction.ModeKey] = LoopAction.ModeCount;
        loop.Config[LoopAction.CountKey] = 3;
        var body = Node("body", out var bodyId);
        var done = Node("done", out var doneId);

        var bot = new Bot { Name = "loop-fail" };
        bot.Actions.AddRange(new[] { loop, body, done });
        bot.Connections.Add(Edge(loopId, LoopAction.BodyPort, bodyId));
        bot.Connections.Add(Edge(loopId, LoopAction.DonePort, doneId));

        var bodyCalls = 0;
        var doneReached = false;
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "body", Behavior = c => { bodyCalls++; return ActionResult.Fail("boom"); } });
        registry.Register(new FakeExecutor { TypeKey = "done", Behavior = c => { doneReached = true; return ActionResult.Ok(string.Empty); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.False(result.Success);
        Assert.Equal("boom", result.ErrorMessage);
        Assert.Equal(bodyId, result.FailedActionId);
        Assert.Equal(1, bodyCalls);          // halts on the first iteration's failure
        Assert.False(doneReached);
    }

    [Fact]
    public async Task Loop_Nested_RunsInnerBodyForEachOuterIteration()
    {
        var outer = Node(LoopAction.LoopTypeKey, out var outerId);
        outer.Config[LoopAction.ModeKey] = LoopAction.ModeCount;
        outer.Config[LoopAction.CountKey] = 2;
        var inner = Node(LoopAction.LoopTypeKey, out var innerId);
        inner.Config[LoopAction.ModeKey] = LoopAction.ModeCount;
        inner.Config[LoopAction.CountKey] = 3;
        var innerBody = Node("innerBody", out var innerBodyId);

        var bot = new Bot { Name = "loop-nested" };
        bot.Actions.AddRange(new[] { outer, inner, innerBody });
        bot.Connections.Add(Edge(outerId, LoopAction.BodyPort, innerId));   // outer body -> inner loop
        bot.Connections.Add(Edge(innerId, LoopAction.BodyPort, innerBodyId)); // inner body -> leaf

        var innerCalls = 0;
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "innerBody", Behavior = c => { innerCalls++; return ActionResult.Ok(string.Empty); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);
        Assert.Equal(6, innerCalls); // 2 outer * 3 inner
    }
}
