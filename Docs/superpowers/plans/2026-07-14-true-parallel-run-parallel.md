# True-Parallel Run Parallel Implementation Plan — Slice 4 of 5

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Make `Run Parallel` branches actually run concurrently (today they run sequentially), and make the run's log/progress sinks safe under that concurrency.

**Architecture:** The branch executors are synchronous (`Task.FromResult`), so `Enumerable.Range(...).Select(RunBranchAsync)` + `Task.WhenAll` runs each branch to completion before the next starts (nothing ever yields). Offloading each branch walk with `Task.Run` puts them on separate thread-pool threads → real parallelism. Then serialize the shared log + progress sinks (a single per-run lock in `BotExecutor.RunAsync`) so concurrent branches can't corrupt a non-thread-safe sink. `Variables` is already a `ConcurrentDictionary`; `ActionsExecuted` already uses `Interlocked`; the frame store's snapshots are already immutable — so those need no change.

**Tech Stack:** C# / .NET 10, xUnit + hand-rolled fakes. Independent of the frame-store slices (branches from `main`).

**Design doc:** `Docs/superpowers/specs/2026-07-14-fast-frame-reads-and-library-manager-design.md`

**Branch:** `worktree-run-parallel` from `origin/main`. Backend-only.

**Concurrency-test principle (important):** the test that proves parallelism MUST use a *synchronous blocking* executor (a `Barrier`), NOT an async-await one. An `await` would yield and let the sibling start even on the buggy sequential code, hiding the bug. A `Barrier(2).SignalAndWait(timeout)` only completes if both branches are truly on different threads simultaneously.

---

### Task 1: Offload each branch via `Task.Run` (true parallelism) + concurrency test

**Files:**
- Modify: `AdbCore/Execution/ControlFlow/ParallelControlFlowExecutor.cs` (one line)
- Test: `AdbCore.Tests/Execution/ParallelExecutionTests.cs` (add a barrier concurrency test + a nested helper executor)

- [ ] **Step 1: Write the failing test.** In `AdbCore.Tests/Execution/ParallelExecutionTests.cs`, add this nested executor class (place it next to the existing `GatedExecutor` private class) and this `[Fact]` (place it after the existing parallel tests). Add `using System.Threading;` at the top of the file if not already present.

```csharp
    /// <summary>A SYNCHRONOUS executor (returns Task.FromResult, mimicking the real CPU-bound executors) that
    /// blocks the calling thread on a shared Barrier. Two branches sharing one Barrier(2) can only both pass if
    /// they run on different threads at the same time — i.e. genuinely in parallel. If they ran sequentially,
    /// the first blocks until its timeout and fails.</summary>
    private sealed class BarrierExecutor : IActionExecutor
    {
        public required string TypeKey { get; init; }
        public required Barrier Barrier { get; init; }

        public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
        {
            var passed = Barrier.SignalAndWait(TimeSpan.FromSeconds(3));
            return Task.FromResult(passed ? ActionResult.Ok(string.Empty) : ActionResult.Fail("barrier timeout — branch ran sequentially"));
        }
    }

    [Fact(Timeout = 20000)]
    public async Task Parallel_BranchesRunConcurrently()
    {
        var rp = RunParallel(out var rpId);
        var x = Node("x", out var xId);
        var y = Node("y", out var yId);
        var join = Node(JoinAction.JoinTypeKey, out var joinId);
        var done = Node("done", out var doneId);

        var bot = new Bot { Name = "par-concurrent" };
        bot.Actions.AddRange(new[] { rp, x, y, join, done });
        bot.Connections.Add(Edge(rpId, RunParallelAction.BranchPort(1), xId));
        bot.Connections.Add(Edge(rpId, RunParallelAction.BranchPort(2), yId));
        bot.Connections.Add(Edge(xId, "out", joinId));
        bot.Connections.Add(Edge(yId, "out", joinId));
        bot.Connections.Add(Edge(joinId, JoinAction.AllSucceededPort, doneId));

        using var barrier = new Barrier(2);
        var registry = new ActionExecutorRegistry();
        registry.Register(new BarrierExecutor { TypeKey = "x", Barrier = barrier });
        registry.Register(new BarrierExecutor { TypeKey = "y", Barrier = barrier });
        registry.Register(new FakeExecutor { TypeKey = "done", Behavior = c => ActionResult.Ok(string.Empty) });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success); // only possible if x and y ran on different threads simultaneously
    }
```

- [ ] **Step 2: Run the test to verify it fails.** `dotnet test ADB.slnx --filter "FullyQualifiedName~ParallelExecutionTests.Parallel_BranchesRunConcurrently"` — expect FAIL: the run fails (`Assert.True(result.Success)` is false) because the branches run sequentially and the first BarrierExecutor times out. (The test's own `Timeout = 20000` guard prevents an indefinite hang; each SignalAndWait caps at 3s.)

- [ ] **Step 3: Implement the fix.** In `AdbCore/Execution/ControlFlow/ParallelControlFlowExecutor.cs`, find the line:

```csharp
        var tasks = Enumerable.Range(0, branchStarts.Count).Select(RunBranchAsync).ToArray();
```

and replace it with (offload each branch walk to the thread pool so the synchronous executors actually run concurrently):

```csharp
        // Offload each branch to the thread pool: the branch executors are largely synchronous, so without
        // this the branches would run to completion one after another (nothing yields). Do NOT pass a token to
        // Task.Run — RunBranchAsync handles sibling-cancel internally via the linked token and its catch filter,
        // and a token here would turn a sibling-halt into a faulted task that Task.WhenAll rethrows.
        var tasks = Enumerable.Range(0, branchStarts.Count).Select(i => Task.Run(() => RunBranchAsync(i))).ToArray();
```

- [ ] **Step 4: Run the test to verify it passes.** `dotnet test ADB.slnx --filter "FullyQualifiedName~ParallelExecutionTests.Parallel_BranchesRunConcurrently"` — expect PASS. Then run the WHOLE parallel suite to confirm no regression in cancellation/halt/nested semantics: `dotnet test ADB.slnx --filter "FullyQualifiedName~ParallelExecutionTests"` — expect ALL pass. Then `dotnet build ADB.slnx`.

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Execution/ControlFlow/ParallelControlFlowExecutor.cs AdbCore.Tests/Execution/ParallelExecutionTests.cs
git commit -m "fix: Run Parallel branches now run concurrently (Task.Run offload)"
```

---

### Task 2: Serialize the log + progress sinks under concurrency

**Files:**
- Modify: `AdbCore/Execution/BotExecutor.cs` (wrap log + progress in a per-run lock; add a `SynchronizedProgress` nested class)
- Test: `AdbCore.Tests/Execution/ParallelSinkSafetyTests.cs`

- [ ] **Step 1: Write the failing tests.** Create `AdbCore.Tests/Execution/ParallelSinkSafetyTests.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Execution;

public class ParallelSinkSafetyTests
{
    private static BotAction Node(string typeKey, out Guid id)
    {
        id = Guid.NewGuid();
        return new BotAction { Id = id, TypeKey = typeKey, Label = typeKey };
    }

    private static ActionConnection Edge(Guid from, string port, Guid to)
        => new() { Id = Guid.NewGuid(), SourceActionId = from, SourcePort = port, TargetActionId = to, TargetPort = "in" };

    private static BotAction RunParallel(out Guid id, int branches)
    {
        id = Guid.NewGuid();
        var n = new BotAction { Id = id, TypeKey = RunParallelAction.RunParallelTypeKey, Label = "rp" };
        n.Config[RunParallelAction.BranchesKey] = branches;
        n.Config[RunParallelAction.OnBranchFailureKey] = ParallelErrorStrategy.HaltAll.ToString();
        return n;
    }

    /// <summary>Detects whether it was ever entered by two threads at once, and counts calls non-atomically so a
    /// serialization failure would also drop updates.</summary>
    private sealed class OverlapDetector
    {
        private int _inside;
        private int _count;
        public bool Overlapped { get; private set; }
        public int Count => _count;

        public void Enter()
        {
            if (Interlocked.Increment(ref _inside) != 1) { Overlapped = true; }
            var c = _count;          // deliberately non-atomic read-modify-write
            Thread.SpinWait(200);
            _count = c + 1;
            Interlocked.Decrement(ref _inside);
        }
    }

    // A synchronous executor that hammers context.Log many times (exercises the log sink under concurrency).
    private sealed class LoggingExecutor : IActionExecutor
    {
        public required string TypeKey { get; init; }
        public required int Times { get; init; }
        public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
        {
            for (var i = 0; i < Times; i++) { context.Log($"{TypeKey}:{i}"); }
            return Task.FromResult(ActionResult.Ok("out"));
        }
    }

    private static Bot FanOut(int branches, out Guid[] leafIds)
    {
        var rp = RunParallel(out var rpId, branches);
        var join = Node(JoinAction.JoinTypeKey, out var joinId);
        var bot = new Bot { Name = "sink-safety" };
        bot.Actions.Add(rp);
        leafIds = new Guid[branches];
        for (var i = 0; i < branches; i++)
        {
            var leaf = Node($"leaf{i}", out var leafId);
            leafIds[i] = leafId;
            bot.Actions.Add(leaf);
            bot.Connections.Add(Edge(rpId, RunParallelAction.BranchPort(i + 1), leafId));
            bot.Connections.Add(Edge(leafId, "out", joinId));
        }
        bot.Actions.Add(join);
        return bot;
    }

    [Fact(Timeout = 20000)]
    public async Task ConcurrentBranches_LogSink_IsSerialized_NoOverlapNoLostMessages()
    {
        const int branches = 4;
        const int perBranch = 200;
        var bot = FanOut(branches, out _);

        var detector = new OverlapDetector();
        var registry = new ActionExecutorRegistry();
        for (var i = 0; i < branches; i++)
        {
            registry.Register(new LoggingExecutor { TypeKey = $"leaf{i}", Times = perBranch });
        }

        var options = new ExecutionOptions { Log = _ => detector.Enter() };
        var result = await new BotExecutor(registry).RunAsync(bot, options, null, default);

        Assert.True(result.Success);
        Assert.False(detector.Overlapped);              // no two branches inside the sink at once
        Assert.Equal(branches * perBranch, detector.Count); // no lost messages (serialized read-modify-write)
    }

    [Fact(Timeout = 20000)]
    public async Task ConcurrentBranches_ProgressSink_IsSerialized_NoOverlap()
    {
        const int branches = 4;
        var bot = FanOut(branches, out _);

        var detector = new OverlapDetector();
        var registry = new ActionExecutorRegistry();
        for (var i = 0; i < branches; i++)
        {
            // each leaf runs quickly; the per-action progress Report is what we stress across branches
            registry.Register(new LoggingExecutor { TypeKey = $"leaf{i}", Times = 50 });
        }

        IProgress<ExecutionProgress> progress = new DelegateProgress(_ => detector.Enter());
        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), progress, default);

        Assert.True(result.Success);
        Assert.False(detector.Overlapped);
    }

    private sealed class DelegateProgress : IProgress<ExecutionProgress>
    {
        private readonly Action<ExecutionProgress> _onReport;
        public DelegateProgress(Action<ExecutionProgress> onReport) => _onReport = onReport;
        public void Report(ExecutionProgress value) => _onReport(value);
    }
}
```

Note: confirm `ExecutionOptions` has a settable `Log` property (it is used across the codebase as `options.Log`). If the property name differs, adjust the test to match the real API (read `AdbCore/Execution/ExecutionOptions.cs`).

- [ ] **Step 2: Run the tests to verify they fail.** `dotnet test ADB.slnx --filter "FullyQualifiedName~ParallelSinkSafetyTests"` — expect FAIL: `detector.Overlapped` is true (and/or `Count` short) because branches now run concurrently (Task 1) but the sinks aren't serialized yet.

- [ ] **Step 3: Implement the hardening.** In `AdbCore/Execution/BotExecutor.cs`, find the line that constructs `RunState` (currently `var state = new RunState(graph, _executors, _controlFlow, context, options.Log ?? (_ => { }), progress);`) and replace it with:

```csharp
        // Serialize the shared log + progress sinks so concurrent Run Parallel branches can't corrupt a
        // non-thread-safe sink (file writer, UI list, etc.). A single per-run lock; uncontended for the common
        // single-threaded run. Monitor is reentrant, so a progress handler that itself logs won't deadlock.
        var sinkGate = new object();
        var rawLog = options.Log ?? (_ => { });
        void SynchronizedLog(string message) { lock (sinkGate) { rawLog(message); } }
        var synchronizedProgress = progress is null ? null : new SynchronizedProgress(progress, sinkGate);

        var state = new RunState(graph, _executors, _controlFlow, context, SynchronizedLog, synchronizedProgress);
```

Then add this nested class inside `BotExecutor` (next to the existing `RunState` nested class):

```csharp
    /// <summary>Wraps an <see cref="IProgress{T}"/> so concurrent Run Parallel branches report one at a time,
    /// sharing the run's sink lock with the log delegate.</summary>
    private sealed class SynchronizedProgress : IProgress<ExecutionProgress>
    {
        private readonly IProgress<ExecutionProgress> _inner;
        private readonly object _gate;

        public SynchronizedProgress(IProgress<ExecutionProgress> inner, object gate)
        {
            _inner = inner;
            _gate = gate;
        }

        public void Report(ExecutionProgress value)
        {
            lock (_gate) { _inner.Report(value); }
        }
    }
```

- [ ] **Step 4: Run the tests to verify they pass.** `dotnet test ADB.slnx --filter "FullyQualifiedName~ParallelSinkSafetyTests"` — expect PASS (2). Then the FULL suite `dotnet test ADB.slnx` — expect all pass (the wrapping must not change single-threaded behavior). Then `dotnet build ADB.slnx`.

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Execution/BotExecutor.cs AdbCore.Tests/Execution/ParallelSinkSafetyTests.cs
git commit -m "fix: serialize log/progress sinks for concurrent Run Parallel branches"
```

---

### Task 3: Docs

**Files:**
- Modify: `CLAUDE.md`
- Modify: `README.md`
- Wiki: `C:/git/ADB.wiki` — update the Run Parallel reference (write only; do NOT commit/push)

- [ ] **Step 1: Full build + test.** `dotnet build ADB.slnx` (clean) and `dotnet test ADB.slnx` (all pass).

- [ ] **Step 2: CLAUDE.md.** Find where Run Parallel is described (search "Run Parallel"). Update the description to state branches now execute **concurrently** (each branch walk offloaded to the thread pool), and that the run's log/progress sinks are serialized so concurrent branches share them safely; `Variables` is a `ConcurrentDictionary`. Keep the existing aggregation/Join semantics text accurate (that behavior is unchanged). Ground every claim in the code just written.

- [ ] **Step 3: README.md.** If Run Parallel is mentioned in the arsenal/features, adjust any wording implying/undermining concurrency so it reads that branches run in parallel. Keep the goblin voice. (If README doesn't mention Run Parallel, skip — note that in the report.)

- [ ] **Step 4: Wiki (write only, no commit/push).** Update the Run Parallel page in `C:/git/ADB.wiki` (search for a file mentioning Run Parallel; if none, create `Run-Parallel.md` and add a `_Sidebar.md` link) to document: branches run concurrently on thread-pool threads; shared run state that's safe (`Variables` concurrent, log/progress serialized); and the one caveat carried from the frame-store slice — capturing into the same frame slot concurrently, or fresh-capturing inside parallel branches (esp. one Android device), is unsupported; capture before the split and read the shared frame. Do NOT run git in `C:/git/ADB.wiki`.

- [ ] **Step 5: Commit the worktree docs**

```bash
git add CLAUDE.md README.md
git commit -m "docs: Run Parallel now runs branches concurrently"
```

---

## Self-Review

**Spec coverage (Slice 4):** true concurrency via Task.Run → Task 1 (+ a barrier test that only passes under real parallelism). Thread-safe log/progress sinks → Task 2. Variables already concurrent / ActionsExecuted already Interlocked / frame snapshots immutable → no change needed (noted). Docs → Task 3. Documented constraint (concurrent same-slot capture / fresh-capture-in-branch unsupported) → Task 3 wiki.

**Placeholder scan:** none — full code/edits in every step; the two spots that depend on exact existing API (the `Select(RunBranchAsync)` line, the `RunState` construction line, and `ExecutionOptions.Log`) are quoted with instructions to match the real source.

**Type consistency:** `Task.Run(() => RunBranchAsync(i))`; `SynchronizedLog` (Action<string>) + `SynchronizedProgress : IProgress<ExecutionProgress>` sharing one `sinkGate`; test helpers `BarrierExecutor`, `OverlapDetector`, `LoggingExecutor`, `DelegateProgress` — consistent within/across tasks.

## Execution Handoff
Backend-only, independent of the frame-store slices → PRs to `main` directly (not stacked).
