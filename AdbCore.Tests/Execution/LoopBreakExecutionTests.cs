using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Execution;

public class LoopBreakExecutionTests
{
    private static BotAction Node(string typeKey, out Guid id)
    {
        id = Guid.NewGuid();
        return new BotAction { Id = id, TypeKey = typeKey, Label = typeKey };
    }

    private static ActionConnection Edge(Guid from, string port, Guid to)
        => new() { Id = Guid.NewGuid(), SourceActionId = from, SourcePort = port, TargetActionId = to, TargetPort = "in" };

    [Fact]
    public async Task LoopBreak_ExitsCountLoopEarly_ThenFollowsDone()
    {
        // Count=5 loop; body routes to Loop-Break once the index reaches 2 (its 3rd iteration).
        var loop = Node(LoopAction.LoopTypeKey, out var loopId);
        loop.Config[LoopAction.ModeKey] = LoopAction.ModeCount;
        loop.Config[LoopAction.CountKey] = 5;
        loop.Config[LoopAction.IndexVariableKey] = "i";
        var body = Node("body", out var bodyId);
        var brk = Node(LoopBreakAction.LoopBreakTypeKey, out var brkId);
        var done = Node("done", out var doneId);

        var bot = new Bot { Name = "loopbreak-count" };
        bot.Actions.AddRange(new[] { loop, body, brk, done });
        bot.Connections.Add(Edge(loopId, LoopAction.BodyPort, bodyId));
        bot.Connections.Add(Edge(bodyId, "brk", brkId));       // body -> Loop-Break (taken at i>=2)
        bot.Connections.Add(Edge(loopId, LoopAction.DonePort, doneId));

        var bodyCalls = 0;
        var doneReached = false;
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor
        {
            TypeKey = "body",
            Behavior = c =>
            {
                bodyCalls++;
                var i = ConfigValues.GetIntVar(c.Context.Variables, "i");
                return ActionResult.Ok(i >= 2 ? "brk" : "out"); // "out" is unwired -> iteration ends, loop continues
            },
        });
        registry.Register(new FakeExecutor { TypeKey = "done", Behavior = c => { doneReached = true; return ActionResult.Ok(string.Empty); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);
        Assert.Equal(3, bodyCalls);   // i=0 (out), i=1 (out), i=2 (brk) -> break; iterations 4 & 5 skipped
        Assert.True(doneReached);
    }

    [Fact]
    public async Task LoopBreak_NestedLoops_BreaksInnerOnly()
    {
        // Outer count=2; inner count=5; inner body always breaks the inner loop on its first iteration.
        var outer = Node(LoopAction.LoopTypeKey, out var outerId);
        outer.Config[LoopAction.ModeKey] = LoopAction.ModeCount;
        outer.Config[LoopAction.CountKey] = 2;
        var inner = Node(LoopAction.LoopTypeKey, out var innerId);
        inner.Config[LoopAction.ModeKey] = LoopAction.ModeCount;
        inner.Config[LoopAction.CountKey] = 5;
        var innerBody = Node("innerBody", out var innerBodyId);
        var brk = Node(LoopBreakAction.LoopBreakTypeKey, out var brkId);

        var bot = new Bot { Name = "loopbreak-nested" };
        bot.Actions.AddRange(new[] { outer, inner, innerBody, brk });
        bot.Connections.Add(Edge(outerId, LoopAction.BodyPort, innerId));
        bot.Connections.Add(Edge(innerId, LoopAction.BodyPort, innerBodyId));
        bot.Connections.Add(Edge(innerBodyId, "out", brkId));

        var innerCalls = 0;
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "innerBody", Behavior = c => { innerCalls++; return ActionResult.Ok("out"); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);
        Assert.Equal(2, innerCalls); // inner breaks after 1 call per outer iteration; outer still runs twice (==1 would mean it broke the outer)
    }

    [Fact]
    public async Task LoopBreak_ForEachLoop_ExitsEarly()
    {
        var seed = Node("seed", out var seedId);
        var loop = Node(LoopAction.LoopTypeKey, out var loopId);
        loop.Config[LoopAction.ModeKey] = LoopAction.ModeForEach;
        loop.Config[LoopAction.CollectionVariableKey] = "items";
        var body = Node("body", out var bodyId);
        var brk = Node(LoopBreakAction.LoopBreakTypeKey, out var brkId);
        var done = Node("done", out var doneId);

        var bot = new Bot { Name = "loopbreak-foreach" };
        bot.Actions.AddRange(new[] { seed, loop, body, brk, done });
        bot.Connections.Add(Edge(seedId, "out", loopId));
        bot.Connections.Add(Edge(loopId, LoopAction.BodyPort, bodyId));
        bot.Connections.Add(Edge(bodyId, "out", brkId)); // first item breaks
        bot.Connections.Add(Edge(loopId, LoopAction.DonePort, doneId));

        var bodyCalls = 0;
        var doneReached = false;
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "seed", Behavior = c => { c.Context.Variables["items"] = "a,b,c"; return ActionResult.Ok("out"); } });
        registry.Register(new FakeExecutor { TypeKey = "body", Behavior = c => { bodyCalls++; return ActionResult.Ok("out"); } });
        registry.Register(new FakeExecutor { TypeKey = "done", Behavior = c => { doneReached = true; return ActionResult.Ok(string.Empty); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);
        Assert.Equal(1, bodyCalls); // breaks on first item; b and c skipped
        Assert.True(doneReached);
    }

    [Fact]
    public async Task LoopBreak_NoEnclosingLoop_EndsPathAndCompletes()
    {
        var seed = Node("seed", out var seedId);
        var brk = Node(LoopBreakAction.LoopBreakTypeKey, out var brkId);

        var bot = new Bot { Name = "loopbreak-toplevel" };
        bot.Actions.AddRange(new[] { seed, brk });
        bot.Connections.Add(Edge(seedId, "out", brkId));

        var seedRan = false;
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "seed", Behavior = c => { seedRan = true; return ActionResult.Ok("out"); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success); // a top-level break is success (path just ends), not a failure
        Assert.True(seedRan);
    }

    [Fact]
    public async Task LoopBreak_AfterJoin_BreaksEnclosingLoop()
    {
        // Supported parallel-aware pattern: loop body runs a Parallel, Join converges, then Loop-Break (after the
        // Join) exits the loop. Count=3 but the loop breaks after its first iteration's Join.
        var loop = Node(LoopAction.LoopTypeKey, out var loopId);
        loop.Config[LoopAction.ModeKey] = LoopAction.ModeCount;
        loop.Config[LoopAction.CountKey] = 3;
        var rp = Node(RunParallelAction.RunParallelTypeKey, out var rpId);
        rp.Config[RunParallelAction.BranchesKey] = 2;
        var a = Node("a", out var aId);
        var b = Node("b", out var bId);
        var join = Node(JoinAction.JoinTypeKey, out var joinId);
        var brk = Node(LoopBreakAction.LoopBreakTypeKey, out var brkId);
        var done = Node("done", out var doneId);

        var bot = new Bot { Name = "loopbreak-after-join" };
        bot.Actions.AddRange(new[] { loop, rp, a, b, join, brk, done });
        bot.Connections.Add(Edge(loopId, LoopAction.BodyPort, rpId));
        bot.Connections.Add(Edge(rpId, RunParallelAction.BranchPort(1), aId));
        bot.Connections.Add(Edge(rpId, RunParallelAction.BranchPort(2), bId));
        bot.Connections.Add(Edge(aId, "out", joinId));
        bot.Connections.Add(Edge(bId, "out", joinId));
        bot.Connections.Add(Edge(joinId, JoinAction.AllSucceededPort, brkId)); // after Join -> Loop-Break
        bot.Connections.Add(Edge(loopId, LoopAction.DonePort, doneId));

        var aCalls = 0;
        var doneReached = false;
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "a", Behavior = c => { aCalls++; return ActionResult.Ok("out"); } });
        registry.Register(new FakeExecutor { TypeKey = "b", Behavior = c => ActionResult.Ok("out") });
        registry.Register(new FakeExecutor { TypeKey = "done", Behavior = c => { doneReached = true; return ActionResult.Ok(string.Empty); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);
        Assert.Equal(1, aCalls);   // loop broke after the first iteration's Join (==3 would mean no break)
        Assert.True(doneReached);
    }

    [Fact]
    public async Task LoopBreak_TerminalInParallelBranch_FailsConvergence()
    {
        // Documents the boundary: a branch ending in Loop-Break reaches no Join, so the existing Parallel
        // convergence rule fails the run (same as End placed terminally in a branch).
        var rp = Node(RunParallelAction.RunParallelTypeKey, out var rpId);
        rp.Config[RunParallelAction.BranchesKey] = 2;
        var brk = Node(LoopBreakAction.LoopBreakTypeKey, out var brkId);
        var b = Node("b", out var bId);
        var join = Node(JoinAction.JoinTypeKey, out var joinId);

        var bot = new Bot { Name = "loopbreak-in-branch" };
        bot.Actions.AddRange(new[] { rp, brk, b, join });
        bot.Connections.Add(Edge(rpId, RunParallelAction.BranchPort(1), brkId)); // terminal Loop-Break, no Join
        bot.Connections.Add(Edge(rpId, RunParallelAction.BranchPort(2), bId));
        bot.Connections.Add(Edge(bId, "out", joinId));

        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "b", Behavior = c => ActionResult.Ok("out") });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.False(result.Success);
        Assert.Contains("converge on exactly one Join", result.ErrorMessage);
    }

    [Fact]
    public async Task LoopBreak_InParallelBranch_HaltsEvenUnderContinueStrategy()
    {
        // Even with the Continue error strategy and no someFailed wiring (the config that downgrades normal
        // branch failures to warnings), a Loop-Break crossing the Parallel boundary is a graph authoring
        // error and must halt the run — it must NOT be downgraded to success.
        var rp = Node(RunParallelAction.RunParallelTypeKey, out var rpId);
        rp.Config[RunParallelAction.BranchesKey] = 2;
        rp.Config[RunParallelAction.OnBranchFailureKey] = ParallelErrorStrategy.Continue.ToString();
        var gate = Node("gate", out var gateId);
        var brk = Node(LoopBreakAction.LoopBreakTypeKey, out var brkId);
        var b = Node("b", out var bId);
        var join = Node(JoinAction.JoinTypeKey, out var joinId);
        var done = Node("done", out var doneId);

        var bot = new Bot { Name = "loopbreak-branch-continue" };
        bot.Actions.AddRange(new[] { rp, gate, brk, b, join, done });
        bot.Connections.Add(Edge(rpId, RunParallelAction.BranchPort(1), gateId));
        bot.Connections.Add(Edge(gateId, "ok", joinId));
        bot.Connections.Add(Edge(gateId, "brk", brkId));
        bot.Connections.Add(Edge(rpId, RunParallelAction.BranchPort(2), bId));
        bot.Connections.Add(Edge(bId, "out", joinId));
        bot.Connections.Add(Edge(joinId, JoinAction.AllSucceededPort, doneId));
        // NOTE: someFailed deliberately left unwired

        var doneReached = false;
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "gate", Behavior = c => ActionResult.Ok("brk") });
        registry.Register(new FakeExecutor { TypeKey = "b", Behavior = c => ActionResult.Ok("out") });
        registry.Register(new FakeExecutor { TypeKey = "done", Behavior = c => { doneReached = true; return ActionResult.Ok(string.Empty); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.False(result.Success);
        Assert.Contains("Loop-Break cannot cross a Run Parallel branch boundary", result.ErrorMessage);
        Assert.False(doneReached);
    }

    [Fact]
    public async Task LoopBreak_ConditionallyReachedInParallelBranch_FailsNotSwallowed()
    {
        // A gate node routes branch 1 to a Loop-Break on one port while the Join stays reachable via its
        // other port, so the graph passes Parallel convergence and the branch actually runs. The break has
        // no enclosing loop inside the branch, so it must surface as a failure — never be silently swallowed.
        var rp = Node(RunParallelAction.RunParallelTypeKey, out var rpId);
        rp.Config[RunParallelAction.BranchesKey] = 2;
        var gate = Node("gate", out var gateId);
        var brk = Node(LoopBreakAction.LoopBreakTypeKey, out var brkId);
        var b = Node("b", out var bId);
        var join = Node(JoinAction.JoinTypeKey, out var joinId);
        var done = Node("done", out var doneId);

        var bot = new Bot { Name = "loopbreak-gated-in-branch" };
        bot.Actions.AddRange(new[] { rp, gate, brk, b, join, done });
        bot.Connections.Add(Edge(rpId, RunParallelAction.BranchPort(1), gateId));
        bot.Connections.Add(Edge(gateId, "ok", joinId));   // keeps the Join reachable from branch 1 (convergence passes)
        bot.Connections.Add(Edge(gateId, "brk", brkId));   // runtime route: gate -> Loop-Break (no enclosing loop)
        bot.Connections.Add(Edge(rpId, RunParallelAction.BranchPort(2), bId));
        bot.Connections.Add(Edge(bId, "out", joinId));
        bot.Connections.Add(Edge(joinId, JoinAction.AllSucceededPort, doneId));

        var doneReached = false;
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "gate", Behavior = c => ActionResult.Ok("brk") }); // takes the break path
        registry.Register(new FakeExecutor { TypeKey = "b", Behavior = c => ActionResult.Ok("out") });
        registry.Register(new FakeExecutor { TypeKey = "done", Behavior = c => { doneReached = true; return ActionResult.Ok(string.Empty); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.False(result.Success);                                              // not silently swallowed
        Assert.Contains("Loop-Break cannot cross a Run Parallel branch boundary", result.ErrorMessage);
        Assert.False(doneReached);                                                 // AllSucceeded path not taken
    }
}
