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

        var badRan = false;
        var recovered = false;
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "good", Behavior = c => ActionResult.Ok("out") });
        registry.Register(new FakeExecutor { TypeKey = "bad", Behavior = c => { badRan = true; return ActionResult.Fail("nope"); } });
        registry.Register(new FakeExecutor { TypeKey = "recover", Behavior = c => { recovered = true; return ActionResult.Ok(string.Empty); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);   // failure was handled by the wired someFailed path
        Assert.True(badRan);
        Assert.True(recovered);
        Assert.Equal(3, result.ActionsExecuted); // good + bad + recover (a failed leaf is still counted)
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
        Assert.Equal(3, result.ActionsExecuted); // a + b + okPath; failPath not reached
    }

    /// <summary>An async executor that blocks on a manually-released gate, so tests can deterministically
    /// hold a branch "in flight" and observe whether a strategy cancels it.</summary>
    private sealed class GatedExecutor : IActionExecutor
    {
        public required string TypeKey { get; init; }
        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Completed { get; private set; }

        public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
        {
            await Gate.Task.WaitAsync(ct); // throws OperationCanceledException if ct is cancelled first
            Completed = true;
            return ActionResult.Ok(string.Empty);
        }
    }

    [Fact(Timeout = 5000)] // guard: a cancellation regression would otherwise hang on the never-released gate
    public async Task Parallel_HaltAll_CancelsInFlightSiblingOnFirstFailure()
    {
        var rp = RunParallel(out var rpId, ParallelErrorStrategy.HaltAll);
        var bad = Node("bad", out var badId);
        var blocker = Node("blocker", out var blockerId);
        var join = Node(JoinAction.JoinTypeKey, out var joinId);

        var bot = new Bot { Name = "par-haltall" };
        bot.Actions.AddRange(new[] { rp, bad, blocker, join });
        bot.Connections.Add(Edge(rpId, RunParallelAction.BranchPort(1), badId));
        bot.Connections.Add(Edge(rpId, RunParallelAction.BranchPort(2), blockerId));
        bot.Connections.Add(Edge(badId, "out", joinId));
        bot.Connections.Add(Edge(blockerId, "out", joinId));
        // Join.someFailed unwired -> HaltAll halts the run

        var gated = new GatedExecutor { TypeKey = "blocker" }; // gate never released
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "bad", Behavior = c => ActionResult.Fail("boom") });
        registry.Register(gated);

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.False(result.Success);       // unhandled failure under HaltAll halts the run
        Assert.Equal("boom", result.ErrorMessage);
        Assert.False(gated.Completed);      // the blocked sibling was cancelled, never completed
    }

    [Fact(Timeout = 5000)]
    public async Task Parallel_WaitThenHalt_LetsSiblingFinishThenHalts()
    {
        var rp = RunParallel(out var rpId, ParallelErrorStrategy.WaitThenHalt);
        var bad = Node("bad", out var badId);
        var blocker = Node("blocker", out var blockerId);
        var join = Node(JoinAction.JoinTypeKey, out var joinId);

        var bot = new Bot { Name = "par-waitthenhalt" };
        bot.Actions.AddRange(new[] { rp, bad, blocker, join });
        bot.Connections.Add(Edge(rpId, RunParallelAction.BranchPort(1), badId));
        bot.Connections.Add(Edge(rpId, RunParallelAction.BranchPort(2), blockerId));
        bot.Connections.Add(Edge(badId, "out", joinId));
        bot.Connections.Add(Edge(blockerId, "out", joinId));
        // Join.someFailed unwired -> halt after siblings settle

        var gated = new GatedExecutor { TypeKey = "blocker" };
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "bad", Behavior = c => ActionResult.Fail("boom") });
        registry.Register(gated);

        var runTask = new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);
        gated.Gate.SetResult(); // release the sibling; WaitThenHalt does not cancel it
        var result = await runTask;

        Assert.False(result.Success);   // unhandled failure still halts under WaitThenHalt
        Assert.Equal("boom", result.ErrorMessage);
        Assert.True(gated.Completed);   // but the sibling was allowed to finish first
    }

    [Fact(Timeout = 5000)]
    public async Task Parallel_Continue_UnhandledFailure_SucceedsAndLetsSiblingFinish()
    {
        var rp = RunParallel(out var rpId, ParallelErrorStrategy.Continue);
        var bad = Node("bad", out var badId);
        var blocker = Node("blocker", out var blockerId);
        var join = Node(JoinAction.JoinTypeKey, out var joinId);

        var bot = new Bot { Name = "par-continue" };
        bot.Actions.AddRange(new[] { rp, bad, blocker, join });
        bot.Connections.Add(Edge(rpId, RunParallelAction.BranchPort(1), badId));
        bot.Connections.Add(Edge(rpId, RunParallelAction.BranchPort(2), blockerId));
        bot.Connections.Add(Edge(badId, "out", joinId));
        bot.Connections.Add(Edge(blockerId, "out", joinId));
        // Join.someFailed unwired -> Continue swallows the failure

        var gated = new GatedExecutor { TypeKey = "blocker" };
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "bad", Behavior = c => ActionResult.Fail("boom") });
        registry.Register(gated);

        var runTask = new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);
        gated.Gate.SetResult();
        var result = await runTask;

        Assert.True(result.Success);    // Continue: failure is a warning, run proceeds
        Assert.True(gated.Completed);
    }

    [Fact]
    public async Task Parallel_NoWiredBranches_Fails()
    {
        var rp = RunParallel(out var rpId);
        var bot = new Bot { Name = "par-no-branches" };
        bot.Actions.Add(rp); // RunParallel is the entry point, nothing wired to its branch ports

        var result = await new BotExecutor(new ActionExecutorRegistry()).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.False(result.Success);
        Assert.Contains("no wired branch", result.ErrorMessage);
    }

    [Fact]
    public async Task Parallel_BranchesDoNotConvergeOnJoin_Fails()
    {
        // branch1 -> a (dead-ends, no Join) ; branch2 -> b (dead-ends, no Join)
        var rp = RunParallel(out var rpId);
        var a = Node("a", out var aId);
        var b = Node("b", out var bId);

        var bot = new Bot { Name = "par-no-join" };
        bot.Actions.AddRange(new[] { rp, a, b });
        bot.Connections.Add(Edge(rpId, RunParallelAction.BranchPort(1), aId));
        bot.Connections.Add(Edge(rpId, RunParallelAction.BranchPort(2), bId));

        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "a", Behavior = c => ActionResult.Ok("out") });
        registry.Register(new FakeExecutor { TypeKey = "b", Behavior = c => ActionResult.Ok("out") });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.False(result.Success);
        Assert.Contains("converge on exactly one Join", result.ErrorMessage);
    }

    [Fact]
    public async Task Parallel_NestedParallelInsideBranch_RunsAllLeaves()
    {
        // outer branch1 -> innerRP (branchA -> la -> innerJoin ; branchB -> lb -> innerJoin) ; innerJoin allSucceeded -> outerJoin
        // outer branch2 -> o2 -> outerJoin ; outerJoin allSucceeded -> done
        var outer = RunParallel(out var outerId);
        var inner = RunParallel(out var innerId);
        var la = Node("la", out var laId);
        var lb = Node("lb", out var lbId);
        var innerJoin = Node(JoinAction.JoinTypeKey, out var innerJoinId);
        var o2 = Node("o2", out var o2Id);
        var outerJoin = Node(JoinAction.JoinTypeKey, out var outerJoinId);
        var done = Node("done", out var doneId);

        var bot = new Bot { Name = "par-nested" };
        bot.Actions.AddRange(new[] { outer, inner, la, lb, innerJoin, o2, outerJoin, done });
        bot.Connections.Add(Edge(outerId, RunParallelAction.BranchPort(1), innerId));
        bot.Connections.Add(Edge(outerId, RunParallelAction.BranchPort(2), o2Id));
        bot.Connections.Add(Edge(innerId, RunParallelAction.BranchPort(1), laId));
        bot.Connections.Add(Edge(innerId, RunParallelAction.BranchPort(2), lbId));
        bot.Connections.Add(Edge(laId, "out", innerJoinId));
        bot.Connections.Add(Edge(lbId, "out", innerJoinId));
        bot.Connections.Add(Edge(innerJoinId, JoinAction.AllSucceededPort, outerJoinId));
        bot.Connections.Add(Edge(o2Id, "out", outerJoinId));
        bot.Connections.Add(Edge(outerJoinId, JoinAction.AllSucceededPort, doneId));

        var ran = new System.Collections.Concurrent.ConcurrentBag<string>();
        var doneReached = false;
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "la", Behavior = c => { ran.Add("la"); return ActionResult.Ok("out"); } });
        registry.Register(new FakeExecutor { TypeKey = "lb", Behavior = c => { ran.Add("lb"); return ActionResult.Ok("out"); } });
        registry.Register(new FakeExecutor { TypeKey = "o2", Behavior = c => { ran.Add("o2"); return ActionResult.Ok("out"); } });
        registry.Register(new FakeExecutor { TypeKey = "done", Behavior = c => { doneReached = true; return ActionResult.Ok(string.Empty); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);
        Assert.True(doneReached);
        Assert.Equal(new[] { "la", "lb", "o2" }, ran.OrderBy(x => x).ToArray());
    }
}
