# M5a2 — Run Parallel + Join Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add concurrent fan-out/join to the execution engine — engine-native **Run Parallel** (N branches as concurrent sub-walks) and **Join** (await all, route `allSucceeded`/`someFailed`), with a configurable `ParallelErrorStrategy`.

**Architecture:** Additive on the M5a1 recursive walker. `WalkAsync` gains an optional `stopBeforeId` boundary so a branch sub-walk halts just before the Join node. `ExecuteParallelAsync` statically finds the branches' convergent Join, runs each wired branch concurrently via `Task.WhenAll` over `WalkAsync`, aggregates outcomes, and routes through the Join. Run state is hardened for concurrency (`Interlocked` action counter, `ConcurrentDictionary` variables). Run Parallel and Join are engine-native (definition-only, no executor), exactly like Loop.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), xUnit. Project: `AdbCore` + `AdbCore.Tests`. Solution: `ADB.slnx`.

**Design reference:** `Docs/Specs/2026-06-01-m5-built-in-actions-design.md` §3.5 (Run Parallel + Join), §2.2 (sub-walk / concurrency-ready), slice M5a2 in §8.

---

## Background the implementer needs

- **The engine (post-M5a1)** is `AdbCore/Execution/BotExecutor.cs`: a recursive `private async Task<WalkOutcome> WalkAsync(RunState state, BotAction? start, CancellationToken ct)` that follows output ports until the path dead-ends. Engine-native control nodes are dispatched by `TypeKey` at the top of the `while (current is not null)` loop (Loop is the existing example: `if (current.TypeKey == LoopAction.LoopTypeKey) { ... ExecuteLoopAsync ...; current = FindNext(state.Bot, current.Id, LoopAction.DonePort); continue; }`). Leaf actions go through `IActionExecutor`. `RunState` (private nested) carries `Bot`, `Executors`, `Context` (`BotExecutionContext`), `Log`, `Progress`, and `ActionsExecuted`. `WalkOutcome` (private nested) is `Completed()` / `Failed(msg, id)`. `FindNext(Bot, Guid fromId, string port)` resolves the single edge from a node's port. Engine-native nodes are **not** counted in `ActionsExecuted` and do not report progress (consistent with Loop).
- **Definition-only pattern:** `LoopAction` implements `IActionDefinition` only (no executor); the engine handles its execution. Run Parallel and Join follow the same pattern. There is no test requiring every definition to have an executor.
- **`ConfigValues`** (`AdbCore/Actions/ConfigValues.cs`) reads config robustly: `GetString`, `GetInt`, `GetBool`, etc. Use it; never hand-parse config.
- **`BotExecutionContext.Variables`** is currently `Dictionary<string, object>`. Concurrent branches may write it, so this task changes it to `ConcurrentDictionary<string, object>` (Task 1). All current consumers use the indexer / `TryGetValue` / `IReadOnlyDictionary`, which `ConcurrentDictionary` supports.
- **Test double:** `AdbCore.Tests/Execution/FakeExecutor.cs` — `required string TypeKey`, synchronous `Func<ActionExecutionContext, ActionResult> Behavior`. For concurrency/cancellation tests this plan adds a small async `GatedExecutor` in the parallel test file (Task 5).

## ParallelErrorStrategy semantics (RESOLVED — the spec was ambiguous here)

The design doc's strategy table (`HaltAll` / `WaitThenHalt` / `Continue`) and the Join's `allSucceeded`/`someFailed` ports left "then halt" vs "proceed to Join" ambiguous. This plan implements the following coherent model (flagged in the PR for the user to confirm):

- The **Join always settles and routes**: `allSucceeded` if every branch succeeded, else `someFailed`.
- **The strategy controls (a) sibling cancellation on first failure, and (b) whether an *unhandled* failure halts the run:**
  - **HaltAll** — on the first branch failure, cancel all still-running sibling branches immediately.
  - **WaitThenHalt** — a branch failure does *not* cancel siblings; all branches run to completion first.
  - **Continue** — same as WaitThenHalt for cancellation (no cancel; wait for all).
- **Routing after branches settle:**
  - All succeeded → follow the Join's `allSucceeded` port (dead-ends successfully if unwired).
  - Some failed AND the Join's `someFailed` port **is wired** → follow it (handled; run continues — for *every* strategy).
  - Some failed AND `someFailed` is **unwired**:
    - `Continue` → treat as a warning; the parallel block succeeds and dead-ends.
    - `HaltAll` / `WaitThenHalt` → the run **halts** (fails) with the first branch's error.

So `Continue` differs from the Halt strategies only when a failure is unhandled (unwired `someFailed`): Continue swallows it, the Halt strategies fail the run. `HaltAll` differs from `WaitThenHalt` only in whether siblings are cancelled on first failure.

**Default strategy:** `HaltAll` (matches the engine's halt-by-default philosophy and the design table order).

## Build / test commands (run from the worktree root)

- Single test class: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Execution.ParallelExecutionTests"`
- Full suite: `dotnet test ADB.slnx`
- Zero-warning build (hard gate): `dotnet build ADB.slnx`

---

## File Structure

- **Modify** `AdbCore/Execution/BotExecutionContext.cs` — `Variables` → `ConcurrentDictionary<string, object>`.
- **Modify** `AdbCore/Execution/BotExecutor.cs` — `RunState` thread-safe action counter; `WalkAsync` `stopBeforeId` boundary; Run Parallel dispatch; `ExecuteParallelAsync`; static join detection (`FindConvergentJoin`/`JoinDistances`); `ParseStrategy`.
- **Create** `AdbCore/Models/ParallelErrorStrategy.cs` — the enum.
- **Create** `AdbCore/Actions/BuiltIn/RunParallelAction.cs` — `control.runParallel`, definition-only.
- **Create** `AdbCore/Actions/BuiltIn/JoinAction.cs` — `control.join`, definition-only.
- **Modify** `AdbCore/Actions/BuiltIn/BuiltInActions.cs` — register the two definitions.
- **Create** `AdbCore.Tests/Execution/ParallelExecutionTests.cs` — engine behavior tests.
- **Create** `AdbCore.Tests/Actions/BuiltIn/ParallelDefinitionsTests.cs` — definition metadata tests.
- **Modify** `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs` — registry counts.
- **Modify** `BotBuilder.Core.Tests/PaletteViewModelTests.cs` — palette counts.

---

## Task 1: Harden run state for concurrency (behavior-preserving)

No observable behavior change; the existing 196 tests stay green. This makes the shared run state safe for concurrent branch sub-walks.

**Files:**
- Modify: `AdbCore/Execution/BotExecutionContext.cs`
- Modify: `AdbCore/Execution/BotExecutor.cs` (`RunState` + the one `ActionsExecuted++` call site)

- [ ] **Step 1: Confirm the suite is green before changes**

Run: `dotnet test ADB.slnx`
Expected: PASS (196 tests).

- [ ] **Step 2: Make `Variables` concurrent**

Replace `AdbCore/Execution/BotExecutionContext.cs` with:

```csharp
using System.Collections.Concurrent;

namespace AdbCore.Execution;

/// <summary>Run-wide state that flows through an entire bot execution. <see cref="Variables"/> is a
/// concurrent dictionary because parallel branches may read and write it simultaneously.</summary>
public class BotExecutionContext
{
    /// <summary>Variables read/written by actions, keyed by name.</summary>
    public ConcurrentDictionary<string, object> Variables { get; } = new();

    /// <summary>Targets resolved at run start, keyed by <c>BotTarget.Id</c>.</summary>
    public Dictionary<Guid, ResolvedTarget> Targets { get; } = new();
}
```

- [ ] **Step 3: Make the action counter thread-safe**

In `AdbCore/Execution/BotExecutor.cs`, replace the `RunState` nested class's `public int ActionsExecuted { get; set; }` line with:

```csharp
        private int _actionsExecuted;
        public int ActionsExecuted => Volatile.Read(ref _actionsExecuted);
        public void RecordActionExecuted() => Interlocked.Increment(ref _actionsExecuted);
```

Then in `WalkAsync`, replace the line `state.ActionsExecuted++;` with:

```csharp
            state.RecordActionExecuted();
```

(`RunAsync` already reads `state.ActionsExecuted` for `ExecutionResult.ActionsExecuted` — that read now goes through the property, unchanged externally.)

- [ ] **Step 4: Verify the full suite still passes unchanged**

Run: `dotnet test ADB.slnx`
Expected: PASS (196 tests), no test edits.

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Execution/BotExecutionContext.cs AdbCore/Execution/BotExecutor.cs
git commit -m "refactor(core): make run state concurrency-safe (concurrent variables, interlocked counter)"
```

---

## Task 2: `ParallelErrorStrategy` enum + Run Parallel / Join definitions

**Files:**
- Create: `AdbCore/Models/ParallelErrorStrategy.cs`
- Create: `AdbCore/Actions/BuiltIn/RunParallelAction.cs`
- Create: `AdbCore/Actions/BuiltIn/JoinAction.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/ParallelDefinitionsTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `AdbCore.Tests/Actions/BuiltIn/ParallelDefinitionsTests.cs`:

```csharp
using AdbCore.Actions.BuiltIn;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class ParallelDefinitionsTests
{
    [Fact]
    public void RunParallel_Definition_HasBranchPortsAndStrategyConfig()
    {
        var def = new RunParallelAction();

        Assert.Equal("control.runParallel", def.TypeKey);
        Assert.Equal("Control Flow", def.Category);
        Assert.Equal(new[] { "in" }, def.InputPorts.Select(p => p.Name));
        Assert.Equal(new[] { "branch1", "branch2" }, def.OutputPorts.Select(p => p.Name));
        Assert.False(def.SupportsRetry);

        Assert.Contains(def.ConfigFields, f => f.Key == RunParallelAction.BranchesKey);
        var strategy = def.ConfigFields.Single(f => f.Key == RunParallelAction.OnBranchFailureKey);
        Assert.Equal(new[] { "HaltAll", "WaitThenHalt", "Continue" }, strategy.Options);
    }

    [Fact]
    public void RunParallel_BranchPort_FormatsOneBasedName()
    {
        Assert.Equal("branch1", RunParallelAction.BranchPort(1));
        Assert.Equal("branch3", RunParallelAction.BranchPort(3));
    }

    [Fact]
    public void Join_Definition_HasAllSucceededAndSomeFailedPorts()
    {
        var def = new JoinAction();

        Assert.Equal("control.join", def.TypeKey);
        Assert.Equal("Control Flow", def.Category);
        Assert.Equal(new[] { "in" }, def.InputPorts.Select(p => p.Name));
        Assert.Equal(new[] { "allSucceeded", "someFailed" }, def.OutputPorts.Select(p => p.Name));
        Assert.False(def.SupportsRetry);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Actions.BuiltIn.ParallelDefinitionsTests"`
Expected: FAIL to compile (types don't exist).

- [ ] **Step 3: Create the enum**

Create `AdbCore/Models/ParallelErrorStrategy.cs`:

```csharp
namespace AdbCore.Models;

/// <summary>How a Run Parallel block reacts when one of its branches fails.</summary>
public enum ParallelErrorStrategy
{
    /// <summary>Cancel all still-running sibling branches immediately on the first failure.</summary>
    HaltAll,

    /// <summary>Let all in-flight branches finish; do not cancel siblings.</summary>
    WaitThenHalt,

    /// <summary>Treat failures as warnings; never cancel, never halt the run.</summary>
    Continue,
}
```

- [ ] **Step 4: Create the Run Parallel definition**

Create `AdbCore/Actions/BuiltIn/RunParallelAction.cs`:

```csharp
using AdbCore.Models;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Fans out its wired branch ports as concurrent sub-walks that converge on a Join.
/// Execution is engine-native (see <c>BotExecutor.ExecuteParallelAsync</c>); this type supplies
/// palette/properties metadata only and has no executor.</summary>
public sealed class RunParallelAction : IActionDefinition
{
    public const string RunParallelTypeKey = "control.runParallel";
    public const string BranchesKey = "branches";
    public const string OnBranchFailureKey = "onBranchFailure";
    public const string BranchPortPrefix = "branch";
    public const int DefaultBranchCount = 2;

    /// <summary>The output port name for the 1-based branch index, e.g. <c>branch1</c>.</summary>
    public static string BranchPort(int oneBasedIndex) => $"{BranchPortPrefix}{oneBasedIndex}";

    public string TypeKey => RunParallelTypeKey;
    public string DisplayName => "Run Parallel";
    public string Category => "Control Flow";
    public string Description => "Runs each wired branch concurrently; branches converge on a Join.";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public List<PortDefinition> OutputPorts { get; } = new()
    {
        new PortDefinition { Name = BranchPort(1), Label = "Branch 1" },
        new PortDefinition { Name = BranchPort(2), Label = "Branch 2" },
    };
    public List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = BranchesKey, Label = "Branches", Type = ConfigFieldType.Number, DefaultValue = DefaultBranchCount },
        new ConfigField
        {
            Key = OnBranchFailureKey,
            Label = "On Branch Failure",
            Type = ConfigFieldType.Enum,
            DefaultValue = nameof(ParallelErrorStrategy.HaltAll),
            Options = new()
            {
                nameof(ParallelErrorStrategy.HaltAll),
                nameof(ParallelErrorStrategy.WaitThenHalt),
                nameof(ParallelErrorStrategy.Continue),
            },
        },
    };
    public bool SupportsRetry => false;
}
```

> Note: the definition exposes the default two branch ports for the palette/canvas. The engine reads the `branches` config and probes `branch1..branchN`, so a bot may wire more branches than the two default ports (builder UI for adding/removing branch ports is a later builder-side concern, out of scope for this engine slice).

- [ ] **Step 5: Create the Join definition**

Create `AdbCore/Actions/BuiltIn/JoinAction.cs`:

```csharp
namespace AdbCore.Actions.BuiltIn;

/// <summary>Synchronization point for a Run Parallel: the engine awaits all branches, then routes
/// <c>allSucceeded</c> or <c>someFailed</c>. Execution is engine-native; this type supplies
/// palette/properties metadata only and has no executor.</summary>
public sealed class JoinAction : IActionDefinition
{
    public const string JoinTypeKey = "control.join";
    public const string AllSucceededPort = "allSucceeded";
    public const string SomeFailedPort = "someFailed";

    public string TypeKey => JoinTypeKey;
    public string DisplayName => "Join";
    public string Category => "Control Flow";
    public string Description => "Waits for all parallel branches, then routes by success.";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public List<PortDefinition> OutputPorts { get; } = new()
    {
        new PortDefinition { Name = AllSucceededPort, Label = "All Succeeded" },
        new PortDefinition { Name = SomeFailedPort, Label = "Some Failed" },
    };
    public List<ConfigField> ConfigFields { get; } = new();
    public bool SupportsRetry => false;
}
```

- [ ] **Step 6: Run to verify the tests pass**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Actions.BuiltIn.ParallelDefinitionsTests"`
Expected: PASS (3 tests).

- [ ] **Step 7: Commit**

```bash
git add AdbCore/Models/ParallelErrorStrategy.cs AdbCore/Actions/BuiltIn/RunParallelAction.cs AdbCore/Actions/BuiltIn/JoinAction.cs AdbCore.Tests/Actions/BuiltIn/ParallelDefinitionsTests.cs
git commit -m "feat(actions): add Run Parallel + Join definitions and ParallelErrorStrategy enum"
```

---

## Task 3: Engine — concurrent fan-out, static join detection, happy path

Adds the `stopBeforeId` boundary to `WalkAsync`, the static join finder, and `ExecuteParallelAsync` for the all-succeed case, plus the Run Parallel dispatch. This task wires the full machinery; later tasks add failure routing and strategies (the implementation below already contains them so the engine is complete, and tasks 4–5 add the tests that exercise them).

**Files:**
- Modify: `AdbCore/Execution/BotExecutor.cs`
- Test: `AdbCore.Tests/Execution/ParallelExecutionTests.cs`

- [ ] **Step 1: Write the failing happy-path tests**

Create `AdbCore.Tests/Execution/ParallelExecutionTests.cs`:

```csharp
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
        // Start -> RunParallel ; branch1 -> A -> Join ; branch2 -> B -> Join ; Join allSucceeded -> Done
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
        // branches config = 2 but only branch1 wired
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
        // Join.allSucceeded intentionally unwired

        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "a", Behavior = c => ActionResult.Ok("out") });
        registry.Register(new FakeExecutor { TypeKey = "b", Behavior = c => ActionResult.Ok("out") });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Execution.ParallelExecutionTests"`
Expected: FAIL — the engine hits "No executor registered for TypeKey 'control.runParallel'".

- [ ] **Step 3: Add the `stopBeforeId` boundary to `WalkAsync`**

In `AdbCore/Execution/BotExecutor.cs`, change the `WalkAsync` signature to add the optional boundary parameter:

```csharp
    private async Task<WalkOutcome> WalkAsync(RunState state, BotAction? start, CancellationToken ct, Guid? stopBeforeId = null)
```

Then, as the FIRST statement inside `while (current is not null)` — immediately after `ct.ThrowIfCancellationRequested();` and before the Loop dispatch — add:

```csharp
            if (stopBeforeId is not null && current.Id == stopBeforeId.Value)
            {
                return WalkOutcome.Completed();
            }
```

(The existing `WalkAsync` calls — entry point and loop body — pass no `stopBeforeId`, so their behavior is unchanged.)

- [ ] **Step 4: Add the Run Parallel dispatch**

In `WalkAsync`, immediately AFTER the Loop dispatch block (`if (current.TypeKey == LoopAction.LoopTypeKey) { ... }`) and before the executor lookup, add:

```csharp
            if (current.TypeKey == RunParallelAction.RunParallelTypeKey)
            {
                var (parallelOutcome, joinId, joinPort) = await ExecuteParallelAsync(state, current, ct);
                if (!parallelOutcome.Success)
                {
                    return parallelOutcome;
                }

                current = joinId is null ? null : FindNext(state.Bot, joinId.Value, joinPort);
                continue;
            }
```

- [ ] **Step 5: Add `ExecuteParallelAsync`, join detection, and the strategy parser**

Add these members to the `BotExecutor` class (place them after `ExecuteLoopAsync`/`SplitItems`). `using AdbCore.Models;` is already present at the top of the file (the Loop code uses it); confirm it is there.

```csharp
    /// <summary>Engine-native Run Parallel: runs each wired branch concurrently as a sub-walk that stops
    /// at the convergent Join, aggregates the outcomes per <see cref="ParallelErrorStrategy"/>, and reports
    /// where execution should continue (the Join's allSucceeded/someFailed port), or a halting failure.</summary>
    private async Task<(WalkOutcome Outcome, Guid? JoinId, string JoinPort)> ExecuteParallelAsync(
        RunState state, BotAction runParallel, CancellationToken ct)
    {
        var strategy = ParseStrategy(
            ConfigValues.GetString(runParallel.Config, RunParallelAction.OnBranchFailureKey, nameof(ParallelErrorStrategy.HaltAll)));
        var branchCount = Math.Max(1,
            ConfigValues.GetInt(runParallel.Config, RunParallelAction.BranchesKey, RunParallelAction.DefaultBranchCount));

        var branchStarts = new List<BotAction>();
        for (var i = 1; i <= branchCount; i++)
        {
            var start = FindNext(state.Bot, runParallel.Id, RunParallelAction.BranchPort(i));
            if (start is not null)
            {
                branchStarts.Add(start);
            }
        }

        if (branchStarts.Count == 0)
        {
            return (WalkOutcome.Failed("Run Parallel has no wired branch ports.", runParallel.Id), null, string.Empty);
        }

        var joinId = FindConvergentJoin(state.Bot, branchStarts.Select(b => b.Id).ToList());
        if (joinId is null)
        {
            return (WalkOutcome.Failed("Run Parallel branches must converge on exactly one Join.", runParallel.Id), null, string.Empty);
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var outcomes = new WalkOutcome[branchStarts.Count];

        async Task RunBranchAsync(int index)
        {
            try
            {
                var outcome = await WalkAsync(state, branchStarts[index], linked.Token, joinId.Value);
                outcomes[index] = outcome;
                if (!outcome.Success && strategy == ParallelErrorStrategy.HaltAll)
                {
                    linked.Cancel();
                }
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                // Cancelled because a sibling failed under HaltAll — not a failure of this branch itself.
                outcomes[index] = WalkOutcome.Completed();
            }
        }

        var tasks = Enumerable.Range(0, branchStarts.Count).Select(RunBranchAsync).ToArray();
        await Task.WhenAll(tasks); // a genuine user cancellation (outer ct) surfaces as OperationCanceledException

        var firstFailure = outcomes.FirstOrDefault(o => o is not null && !o.Success);
        if (firstFailure is null)
        {
            return (WalkOutcome.Completed(), joinId, JoinAction.AllSucceededPort);
        }

        // A branch failed. If someFailed is wired, route to it (handled) regardless of strategy.
        if (FindNext(state.Bot, joinId.Value, JoinAction.SomeFailedPort) is not null)
        {
            return (WalkOutcome.Completed(), joinId, JoinAction.SomeFailedPort);
        }

        // Unhandled failure: Continue swallows it; Halt strategies fail the run.
        if (strategy == ParallelErrorStrategy.Continue)
        {
            return (WalkOutcome.Completed(), joinId, JoinAction.SomeFailedPort);
        }

        return (WalkOutcome.Failed(firstFailure.ErrorMessage, firstFailure.FailedActionId ?? runParallel.Id), null, string.Empty);
    }

    private static ParallelErrorStrategy ParseStrategy(string value)
        => Enum.TryParse<ParallelErrorStrategy>(value, ignoreCase: true, out var s) ? s : ParallelErrorStrategy.HaltAll;

    /// <summary>Finds the single Join node all branches converge on, choosing the nearest common Join when
    /// more than one is reachable from every branch. Returns null if zero, or an ambiguous tie.</summary>
    private static Guid? FindConvergentJoin(Bot bot, IReadOnlyList<Guid> branchStartIds)
    {
        var perBranch = branchStartIds.Select(id => JoinDistances(bot, id)).ToList();
        if (perBranch.Count == 0)
        {
            return null;
        }

        IEnumerable<Guid> common = perBranch[0].Keys;
        foreach (var map in perBranch.Skip(1))
        {
            common = common.Intersect(map.Keys);
        }

        var commonJoins = common.ToList();
        if (commonJoins.Count == 0)
        {
            return null;
        }

        if (commonJoins.Count == 1)
        {
            return commonJoins[0];
        }

        // Nearest common Join = the one with the smallest worst-case distance across branches.
        Guid? best = null;
        var bestScore = int.MaxValue;
        var tie = false;
        foreach (var join in commonJoins)
        {
            var score = perBranch.Max(map => map[join]);
            if (score < bestScore)
            {
                bestScore = score;
                best = join;
                tie = false;
            }
            else if (score == bestScore)
            {
                tie = true;
            }
        }

        return tie ? null : best;
    }

    /// <summary>BFS forward from <paramref name="startId"/> over all outgoing edges, returning the shortest
    /// distance to each reachable Join node.</summary>
    private static Dictionary<Guid, int> JoinDistances(Bot bot, Guid startId)
    {
        var distances = new Dictionary<Guid, int>();
        var visited = new HashSet<Guid> { startId };
        var queue = new Queue<(Guid Id, int Depth)>();
        queue.Enqueue((startId, 0));

        while (queue.Count > 0)
        {
            var (id, depth) = queue.Dequeue();
            var node = bot.Actions.FirstOrDefault(a => a.Id == id);
            if (node is not null && node.TypeKey == JoinAction.JoinTypeKey && !distances.ContainsKey(id))
            {
                distances[id] = depth;
            }

            foreach (var edge in bot.Connections.Where(c => c.SourceActionId == id))
            {
                if (visited.Add(edge.TargetActionId))
                {
                    queue.Enqueue((edge.TargetActionId, depth + 1));
                }
            }
        }

        return distances;
    }
```

- [ ] **Step 6: Run the happy-path tests**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Execution.ParallelExecutionTests"`
Expected: PASS (3 tests). Then run the full suite `dotnet test ADB.slnx` (expect all green — the engine changes are additive; `stopBeforeId` defaults null).

- [ ] **Step 7: Commit**

```bash
git add AdbCore/Execution/BotExecutor.cs AdbCore.Tests/Execution/ParallelExecutionTests.cs
git commit -m "feat(core): engine-native Run Parallel/Join fan-out with static join detection (happy path)"
```

---

## Task 4: Branch-failure routing through the Join

The engine logic already exists (Task 3 Step 5). This task adds the tests that pin down `allSucceeded` vs `someFailed` selection and the wired-`someFailed` recovery path.

**Files:**
- Modify: `AdbCore.Tests/Execution/ParallelExecutionTests.cs`

- [ ] **Step 1: Append the failure-routing tests**

Append to the `ParallelExecutionTests` class:

```csharp
    [Fact]
    public async Task Parallel_BranchFails_WiredSomeFailed_FollowsRecoveryPath()
    {
        // branch1 -> good -> Join ; branch2 -> bad(fails) -> Join ; Join someFailed -> recover
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
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Execution.ParallelExecutionTests"`
Expected: PASS (5 tests).

- [ ] **Step 3: Commit**

```bash
git add AdbCore.Tests/Execution/ParallelExecutionTests.cs
git commit -m "test(core): cover Run Parallel failure routing through Join ports"
```

---

## Task 5: Strategy behavior — HaltAll cancels siblings; WaitThenHalt/Continue do not; unhandled-failure halt

Deterministic concurrency tests using a small gated async executor. Engine logic already exists; this task adds the executor double and the strategy tests.

**Files:**
- Modify: `AdbCore.Tests/Execution/ParallelExecutionTests.cs`

- [ ] **Step 1: Append the gated executor and strategy tests**

Append to the `ParallelExecutionTests` class:

```csharp
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

    [Fact]
    public async Task Parallel_HaltAll_CancelsInFlightSiblingOnFirstFailure()
    {
        // branch1 -> bad (fails immediately) ; branch2 -> blocker (gate never released) -> Join
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

    [Fact]
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
        Assert.True(gated.Completed);   // but the sibling was allowed to finish first
    }

    [Fact]
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
```

- [ ] **Step 2: Run the strategy tests**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Execution.ParallelExecutionTests"`
Expected: PASS (8 tests). These are deterministic: the HaltAll blocker is released only by cancellation; the WaitThenHalt/Continue blockers are released by the test before awaiting.

- [ ] **Step 3: Commit**

```bash
git add AdbCore.Tests/Execution/ParallelExecutionTests.cs
git commit -m "test(core): cover ParallelErrorStrategy (HaltAll cancels; WaitThenHalt/Continue do not)"
```

---

## Task 6: Validation + nested parallel

Engine logic already exists; this task adds tests for the validation failures and a nested Run Parallel (parallel inside a branch).

**Files:**
- Modify: `AdbCore.Tests/Execution/ParallelExecutionTests.cs`

- [ ] **Step 1: Append the validation and nesting tests**

Append to the `ParallelExecutionTests` class:

```csharp
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
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Execution.ParallelExecutionTests"`
Expected: PASS (11 tests). If the nested test fails, the bug is in `FindConvergentJoin`/`JoinDistances` (the nearest-common-join logic) — investigate the engine, not the test.

- [ ] **Step 3: Commit**

```bash
git add AdbCore.Tests/Execution/ParallelExecutionTests.cs
git commit -m "test(core): cover Run Parallel validation errors and nested parallel"
```

---

## Task 7: Register Run Parallel + Join; update counts; gate; spec status

**Files:**
- Modify: `AdbCore/Actions/BuiltIn/BuiltInActions.cs`
- Modify: `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs`
- Modify: `BotBuilder.Core.Tests/PaletteViewModelTests.cs`
- Modify: `Docs/Specs/2026-06-01-m5-built-in-actions-design.md`

After this task: definitions = 8 (Start, End, Log, Delay, Branch, Loop, RunParallel, Join); executors = 5 (Loop, RunParallel, Join are all engine-native, definition-only).

- [ ] **Step 1: Update the registration assertions (failing first)**

Replace the `Register_AddsAllBuiltInsToBothRegistries` test in `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs` with:

```csharp
    [Fact]
    public void Register_AddsAllBuiltInsToBothRegistries()
    {
        var defs = new ActionRegistry();
        var execs = new ActionExecutorRegistry();

        BuiltInActions.Register(defs, execs);

        foreach (var key in new[] { "control.start", "control.end", "data.log", "control.delay", "control.branch" })
        {
            Assert.True(defs.TryGet(key, out _));
            Assert.True(execs.TryGet(key, out _));
        }

        // Engine-native nodes: definitions only, no executors.
        foreach (var key in new[] { "control.loop", "control.runParallel", "control.join" })
        {
            Assert.True(defs.TryGet(key, out _));
            Assert.False(execs.TryGet(key, out _));
        }

        Assert.Equal(8, defs.Count);
        Assert.Equal(5, execs.Count);
    }
```

- [ ] **Step 2: Update the palette counts**

In `BotBuilder.Core.Tests/PaletteViewModelTests.cs`:
- In `Categories_GroupBuiltInsByCategory`, change the control-items assertion to:

```csharp
        Assert.Equal(7, control.Items.Count); // Start, End, Delay, Branch, Loop, Run Parallel, Join
```

- In `ClearingSearch_RestoresAll`, change the total assertion to:

```csharp
        Assert.Equal(8, palette.Categories.SelectMany(c => c.Items).Count()); // 7 Control Flow + 1 Data
```

(`Assert.Single(data.Items)` and the `Search_MatchesByCategoryName` Start/End assertions remain correct.)

- [ ] **Step 3: Run those tests to verify they FAIL**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Actions.BuiltIn.BuiltInActionsTests|FullyQualifiedName~BotBuilder.Core.Tests.PaletteViewModelTests"`
Expected: FAIL (counts don't match; not registered yet).

- [ ] **Step 4: Register the two definitions**

In `AdbCore/Actions/BuiltIn/BuiltInActions.cs`, in the `Register` method, after the existing `definitions.Register(new LoopAction());` line, add:

```csharp
        // Run Parallel and Join are engine-native: register their definitions only (no executors).
        definitions.Register(new RunParallelAction());
        definitions.Register(new JoinAction());
```

- [ ] **Step 5: Run the full suite**

Run: `dotnet test ADB.slnx`
Expected: ALL green. If any other test asserts a built-in count, update it to reflect the two added Control Flow definitions.

- [ ] **Step 6: Zero-warning build gate**

Run: `dotnet build ADB.slnx`
Expected: Build succeeded, **0 Warning(s), 0 Error(s)**. Fix any warnings.

- [ ] **Step 7: Update the spec status line**

In `Docs/Specs/2026-06-01-m5-built-in-actions-design.md`, change the status line to:

```
**Status:** Approved — M5a1 + M5a2 (engine v2 + Branch/Loop/Delay + Run Parallel/Join) implemented
```

- [ ] **Step 8: Commit**

```bash
git add AdbCore/Actions/BuiltIn/BuiltInActions.cs AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs BotBuilder.Core.Tests/PaletteViewModelTests.cs Docs/Specs/2026-06-01-m5-built-in-actions-design.md
git commit -m "feat(actions): register Run Parallel + Join control-flow definitions"
```

---

## Manual Verification Checklist (for the user)

After merge, in BotBuilder:
- [ ] The Control Flow palette lists **Run Parallel** (Branch 1 / Branch 2 output ports) and **Join** (All Succeeded / Some Failed ports).
- [ ] Build a multi-branch bot: Start → Run Parallel; Branch 1 → Log "A" → Join; Branch 2 → Log "B" → Join; Join All Succeeded → End. Save and run via BotRunner; confirm both "A" and "B" log and the run succeeds.
- [ ] Make one branch fail (e.g. a Branch action routing to a dead-end that the engine treats as failure, or once Input/Screen exist, a failing action). With `On Branch Failure = HaltAll` and Some Failed unwired, the run should report failure; wiring Some Failed → a Log should make it succeed via the recovery path.

> Note: deep multi-client behavior (real concurrent Window/Input targets) is exercised once M5c/M5d land; M5a2 verifies the engine fan-out/join + strategy routing.

---

## Self-Review (completed by plan author)

**Spec coverage (design §3.5 / slice M5a2):**
- Run Parallel: N user-configurable branch ports, concurrent fan-out, per-branch TargetId — Task 2 (definition) + Task 3 (engine; TargetId rides on each branch action and is honored by leaf executors unchanged). ✓
- `ParallelErrorStrategy` HaltAll/WaitThenHalt/Continue — Task 2 (enum) + Task 3 (engine) + Task 5 (tests). ✓
- Join allSucceeded/someFailed routing — Task 3 + Task 4. ✓
- Branch convergence on a single Join + validation — Task 3 (`FindConvergentJoin`) + Task 6. ✓
- Concurrency-ready run state — Task 1. ✓
- Nesting (parallel-inside-branch) — Task 6. ✓
- Registration + palette — Task 7. ✓

**Resolved ambiguity:** the halt-vs-continue/someFailed-routing semantics are defined in the "ParallelErrorStrategy semantics" section above and implemented in `ExecuteParallelAsync`; flagged for the user in the PR.

**Placeholder scan:** none — every step has complete code/commands.

**Type consistency:** `RunParallelAction.{RunParallelTypeKey,BranchesKey,OnBranchFailureKey,BranchPort,DefaultBranchCount}`, `JoinAction.{JoinTypeKey,AllSucceededPort,SomeFailedPort}`, `ParallelErrorStrategy.{HaltAll,WaitThenHalt,Continue}`, and engine members `ExecuteParallelAsync`/`ParseStrategy`/`FindConvergentJoin`/`JoinDistances`/`WalkAsync(...,Guid? stopBeforeId)`/`RunState.RecordActionExecuted` are referenced consistently across tasks.

**Out of scope (later slices / follow-ups):** builder UI for adding/removing branch ports (dynamic port count); loop/parallel-level progress events; cyclic-`.bot` stack-depth hardening; Set Variable/Comment (M5b); Input (M5c); Screen (M5d). Chained parallel blocks where a later Join is common to all branches are resolved by nearest-distance; a genuine distance tie is rejected as ambiguous (documented).
