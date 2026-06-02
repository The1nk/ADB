using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Execution;

public class ParallelExecutionTests
{
    private static BotAction Node(string typeKey, out Guid id)
    {
        id = Guid.NewGuid();
        return new BotAction { Id = id, TypeKey = typeKey, Label = typeKey };
    }

    private static ActionConnection Edge(Guid from, string port, Guid to)
        => new() { Id = Guid.NewGuid(), SourceActionId = from, SourcePort = port, TargetActionId = to, TargetPort = "in" };

    private static BotAction RunParallel(out Guid id, ParallelErrorStrategy strategy = ParallelErrorStrategy.HaltAll, int branches = 2)
    {
        var n = Node(RunParallelAction.RunParallelTypeKey, out id);
        n.Config[RunParallelAction.BranchesKey] = branches;
        n.Config[RunParallelAction.OnBranchFailureKey] = strategy.ToString();
        return n;
    }

    [Fact]
    public async Task Parallel_AllBranchesSucceed_RunsBothAndFollowsAllSucceeded()
    {
        var rp = RunParallel(out var rpId);
        var a = Node("a", out var aId);
        var b = Node("b", out var bId);
        var join = Node(JoinAction.JoinTypeKey, out var joinId);
        var done = Node("done", out var doneId);

        var bot = new Bot { Name = "par-happy" };
        bot.Actions.AddRange(new[] { rp, a, b, join, done });
        bot.Connections.Add(Edge(rpId, RunParallelAction.BranchPort(1), aId));
        bot.Connections.Add(Edge(rpId, RunParallelAction.BranchPort(2), bId));
        bot.Connections.Add(Edge(aId, "out", joinId));
        bot.Connections.Add(Edge(bId, "out", joinId));
        bot.Connections.Add(Edge(joinId, JoinAction.AllSucceededPort, doneId));

        var aRan = false;
        var bRan = false;
        var doneReached = false;
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "a", Behavior = c => { aRan = true; return ActionResult.Ok("out"); } });
        registry.Register(new FakeExecutor { TypeKey = "b", Behavior = c => { bRan = true; return ActionResult.Ok("out"); } });
        registry.Register(new FakeExecutor { TypeKey = "done", Behavior = c => { doneReached = true; return ActionResult.Ok(string.Empty); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);
        Assert.True(aRan);
        Assert.True(bRan);
        Assert.True(doneReached);
        Assert.Equal(3, result.ActionsExecuted); // a + b + done; RunParallel and Join are engine-native (uncounted)
    }

    [Fact]
    public async Task Parallel_OnlyWiredBranchesRun()
    {
        var rp = RunParallel(out var rpId);
        var a = Node("a", out var aId);
        var join = Node(JoinAction.JoinTypeKey, out var joinId);
        var done = Node("done", out var doneId);

        var bot = new Bot { Name = "par-one-branch" };
        bot.Actions.AddRange(new[] { rp, a, join, done });
        bot.Connections.Add(Edge(rpId, RunParallelAction.BranchPort(1), aId));
        bot.Connections.Add(Edge(aId, "out", joinId));
        bot.Connections.Add(Edge(joinId, JoinAction.AllSucceededPort, doneId));

        var doneReached = false;
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "a", Behavior = c => ActionResult.Ok("out") });
        registry.Register(new FakeExecutor { TypeKey = "done", Behavior = c => { doneReached = true; return ActionResult.Ok(string.Empty); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);
        Assert.True(doneReached);
    }

    [Fact]
    public async Task Parallel_AllSucceeded_UnwiredPort_DeadEndsSuccessfully()
    {
        var rp = RunParallel(out var rpId);
        var a = Node("a", out var aId);
        var b = Node("b", out var bId);
        var join = Node(JoinAction.JoinTypeKey, out var joinId);

        var bot = new Bot { Name = "par-no-after" };
        bot.Actions.AddRange(new[] { rp, a, b, join });
        bot.Connections.Add(Edge(rpId, RunParallelAction.BranchPort(1), aId));
        bot.Connections.Add(Edge(rpId, RunParallelAction.BranchPort(2), bId));
        bot.Connections.Add(Edge(aId, "out", joinId));
        bot.Connections.Add(Edge(bId, "out", joinId));

        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "a", Behavior = c => ActionResult.Ok("out") });
        registry.Register(new FakeExecutor { TypeKey = "b", Behavior = c => ActionResult.Ok("out") });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Parallel_BranchFails_WiredSomeFailed_FollowsRecoveryPath()
    {
        var rp = RunParallel(out var rpId, ParallelErrorStrategy.WaitThenHalt);
        var good = Node("good", out var goodId);
        var bad = Node("bad", out var badId);
        var join = Node(JoinAction.JoinTypeKey, out var joinId);
        var recover = Node("recover", out var recoverId);

        var bot = new Bot { Name = "par-recover" };
        bot.Actions.AddRange(new[] { rp, good, bad, join, recover });
        bot.Connections.Add(Edge(rpId, RunParallelAction.BranchPort(1), goodId));
        bot.Connections.Add(Edge(rpId, RunParallelAction.BranchPort(2), badId));
        bot.Connections.Add(Edge(goodId, "out", joinId));
        bot.Connections.Add(Edge(badId, "out", joinId));
        bot.Connections.Add(Edge(joinId, JoinAction.SomeFailedPort, recoverId));

        var recovered = false;
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "good", Behavior = c => ActionResult.Ok("out") });
        registry.Register(new FakeExecutor { TypeKey = "bad", Behavior = c => ActionResult.Fail("nope") });
        registry.Register(new FakeExecutor { TypeKey = "recover", Behavior = c => { recovered = true; return ActionResult.Ok(string.Empty); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);   // failure was handled by the wired someFailed path
        Assert.True(recovered);
    }

    [Fact]
    public async Task Parallel_AllSucceeded_DoesNotFollowSomeFailed()
    {
        var rp = RunParallel(out var rpId, ParallelErrorStrategy.WaitThenHalt);
        var a = Node("a", out var aId);
        var b = Node("b", out var bId);
        var join = Node(JoinAction.JoinTypeKey, out var joinId);
        var okPath = Node("okPath", out var okId);
        var failPath = Node("failPath", out var failId);

        var bot = new Bot { Name = "par-route-ok" };
        bot.Actions.AddRange(new[] { rp, a, b, join, okPath, failPath });
        bot.Connections.Add(Edge(rpId, RunParallelAction.BranchPort(1), aId));
        bot.Connections.Add(Edge(rpId, RunParallelAction.BranchPort(2), bId));
        bot.Connections.Add(Edge(aId, "out", joinId));
        bot.Connections.Add(Edge(bId, "out", joinId));
        bot.Connections.Add(Edge(joinId, JoinAction.AllSucceededPort, okId));
        bot.Connections.Add(Edge(joinId, JoinAction.SomeFailedPort, failId));

        var okReached = false;
        var failReached = false;
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "a", Behavior = c => ActionResult.Ok("out") });
        registry.Register(new FakeExecutor { TypeKey = "b", Behavior = c => ActionResult.Ok("out") });
        registry.Register(new FakeExecutor { TypeKey = "okPath", Behavior = c => { okReached = true; return ActionResult.Ok(string.Empty); } });
        registry.Register(new FakeExecutor { TypeKey = "failPath", Behavior = c => { failReached = true; return ActionResult.Ok(string.Empty); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);
        Assert.True(okReached);
        Assert.False(failReached);
    }
}
