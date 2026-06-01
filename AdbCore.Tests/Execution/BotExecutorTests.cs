using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Execution;

public class BotExecutorTests
{
    private static BotAction Node(string typeKey, out Guid id)
    {
        id = Guid.NewGuid();
        return new BotAction { Id = id, TypeKey = typeKey, Label = typeKey };
    }

    private static ActionConnection Edge(Guid from, string port, Guid to)
        => new() { Id = Guid.NewGuid(), SourceActionId = from, SourcePort = port, TargetActionId = to, TargetPort = "in" };

    [Fact]
    public async Task RunAsync_LinearPath_ExecutesAllInOrderAndSucceeds()
    {
        var start = Node("a", out var startId);
        var mid = Node("b", out var midId);
        var end = Node("c", out var endId);
        var bot = new Bot { Name = "linear" };
        bot.Actions.AddRange(new[] { start, mid, end });
        bot.Connections.Add(Edge(startId, "out", midId));
        bot.Connections.Add(Edge(midId, "out", endId));

        var order = new List<string>();
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "a", Behavior = c => { order.Add("a"); return ActionResult.Ok("out"); } });
        registry.Register(new FakeExecutor { TypeKey = "b", Behavior = c => { order.Add("b"); return ActionResult.Ok("out"); } });
        registry.Register(new FakeExecutor { TypeKey = "c", Behavior = c => { order.Add("c"); return ActionResult.Ok(string.Empty); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);
        Assert.Equal(3, result.ActionsExecuted);
        Assert.Equal(new[] { "a", "b", "c" }, order);
    }

    [Fact]
    public async Task RunAsync_FollowsNamedOutputPort()
    {
        var branch = Node("branch", out var branchId);
        var yes = Node("yes", out var yesId);
        var no = Node("no", out var noId);
        var bot = new Bot { Name = "ports" };
        bot.Actions.AddRange(new[] { branch, yes, no });
        bot.Connections.Add(Edge(branchId, "true", yesId));
        bot.Connections.Add(Edge(branchId, "false", noId));

        var taken = "";
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "branch", Behavior = c => ActionResult.Ok("true") });
        registry.Register(new FakeExecutor { TypeKey = "yes", Behavior = c => { taken = "yes"; return ActionResult.Ok(string.Empty); } });
        registry.Register(new FakeExecutor { TypeKey = "no", Behavior = c => { taken = "no"; return ActionResult.Ok(string.Empty); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);
        Assert.Equal("yes", taken);
    }

    [Fact]
    public async Task RunAsync_MissingExecutor_FailsGracefully()
    {
        var only = Node("ghost", out _);
        var bot = new Bot { Name = "missing" };
        bot.Actions.Add(only);

        var result = await new BotExecutor(new ActionExecutorRegistry()).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.False(result.Success);
        Assert.Contains("ghost", result.ErrorMessage);
    }

    [Fact]
    public async Task RunAsync_NoEntryPoint_Fails()
    {
        var a = Node("a", out var aId);
        var b = Node("b", out var bId);
        var bot = new Bot { Name = "cycle-ish" };
        bot.Actions.AddRange(new[] { a, b });
        bot.Connections.Add(Edge(aId, "out", bId));
        bot.Connections.Add(Edge(bId, "out", aId));

        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "a" });
        registry.Register(new FakeExecutor { TypeKey = "b" });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.False(result.Success);
        Assert.Contains("entry point", result.ErrorMessage);
    }

    [Fact]
    public async Task RunAsync_FailureWithNoFailurePort_HaltsByDefault()
    {
        var start = Node("start", out var startId);
        var boom = Node("boom", out var boomId);
        var never = Node("never", out var neverId);
        var bot = new Bot { Name = "halt" };
        bot.Actions.AddRange(new[] { start, boom, never });
        bot.Connections.Add(Edge(startId, "out", boomId));
        bot.Connections.Add(Edge(boomId, "out", neverId));

        var reachedNever = false;
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "start", Behavior = c => ActionResult.Ok("out") });
        registry.Register(new FakeExecutor { TypeKey = "boom", Behavior = c => ActionResult.Fail("kaboom") });
        registry.Register(new FakeExecutor { TypeKey = "never", Behavior = c => { reachedNever = true; return ActionResult.Ok(string.Empty); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.False(result.Success);
        Assert.Equal("kaboom", result.ErrorMessage);
        Assert.Equal(boomId, result.FailedActionId);
        Assert.False(reachedNever);
    }

    [Fact]
    public async Task RunAsync_FailureWithFailurePort_FollowsIt()
    {
        var boom = Node("boom", out var boomId);
        var handler = Node("handler", out var handlerId);
        var bot = new Bot { Name = "recover" };
        bot.Actions.AddRange(new[] { boom, handler });
        bot.Connections.Add(Edge(boomId, "onFailure", handlerId));

        var recovered = false;
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "boom", Behavior = c => ActionResult.Fail("kaboom") });
        registry.Register(new FakeExecutor { TypeKey = "handler", Behavior = c => { recovered = true; return ActionResult.Ok(string.Empty); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);
        Assert.True(recovered);
    }

    [Fact]
    public async Task RunAsync_RetriesFailingActionUpToMaxAttempts()
    {
        var flaky = Node("flaky", out var flakyId);
        flaky.Retry = new RetryPolicy { MaxAttempts = 3, DelayMs = 0 };
        var bot = new Bot { Name = "retry" };
        bot.Actions.Add(flaky);

        var attempts = 0;
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor
        {
            TypeKey = "flaky",
            Behavior = c => { attempts++; return attempts < 3 ? ActionResult.Fail("not yet") : ActionResult.Ok(string.Empty); },
        });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task RunAsync_ReportsProgressPerAction()
    {
        var start = Node("start", out var startId);
        var end = Node("end", out var endId);
        var bot = new Bot { Name = "progress" };
        bot.Actions.AddRange(new[] { start, end });
        bot.Connections.Add(Edge(startId, "out", endId));

        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "start", Behavior = c => ActionResult.Ok("out") });
        registry.Register(new FakeExecutor { TypeKey = "end", Behavior = c => ActionResult.Ok(string.Empty) });

        var reports = new List<ExecutionProgress>();
        var progress = new InlineTestProgress(reports.Add);

        await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), progress, default);

        Assert.Equal(2, reports.Count);
        Assert.All(reports, r => Assert.True(r.Success));
    }

    private sealed class InlineTestProgress : IProgress<ExecutionProgress>
    {
        private readonly Action<ExecutionProgress> _h;
        public InlineTestProgress(Action<ExecutionProgress> h) => _h = h;
        public void Report(ExecutionProgress value) => _h(value);
    }
}
