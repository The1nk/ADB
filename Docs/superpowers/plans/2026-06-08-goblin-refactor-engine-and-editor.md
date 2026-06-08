# Goblin Refactor: Engine Lookups, Control-Flow Registry & Editor Logic Extraction — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Six behavior-preserving refactors in one PR — index the action graph (#1), make control-flow dispatch registry-driven (#2), and lift four pockets of stranded logic out of `MainWindow.xaml.cs` into testable `.Core` (A–D) — with no change to observable behavior and the full suite green at every commit.

**Architecture:** The engine work introduces `BotGraph` (a per-run index replacing linear `FirstOrDefault` scans) and an `IControlFlowExecutor`/`ControlFlowExecutorRegistry` pair so Loop and Run Parallel register like any other node instead of being hardcoded `if (TypeKey == …)` branches in `BotExecutor.WalkAsync`. The editor work moves bounds math, target resolution, and test-run path building into `BotEditorViewModel`/`TargetBarViewModel`/a new `TestRunArtifacts` helper, and replaces a triplicated `160` literal with the canonical `NodeLayout.CardWidth`.

**Tech Stack:** .NET 10, C# (nullable enabled), xUnit (hand-rolled fakes, no mock framework), WPF (BotBuilder code-behind + XAML).

**Non-negotiable invariants (every task):**
- Behavior-preserving. The existing 710 tests must stay green — especially `LoopExecutionTests`, `ParallelExecutionTests`, `BotExecutorTests`, `BotExecutorInterpolationTests`. Do **not** "improve" semantics while moving code (see Task 7's `null` fallback note).
- Build clean with **0 warnings** (`dotnet build ADB.slnx`): nullable-correct, file-scoped namespaces, `_camelCase` private fields, XML doc comments on new public types/methods.
- Commit after each task. Keep commits focused.

**Baseline (already verified at plan time):** `dotnet build ADB.slnx` succeeds, `dotnet test ADB.slnx` = 710 passed / 0 failed.

---

## File Structure

**New files:**
- `AdbCore/Execution/BotGraph.cs` — per-run action/connection index.
- `AdbCore/Execution/WalkOutcome.cs` — extracted from `BotExecutor`'s private nested class; now public.
- `AdbCore/Execution/IControlFlowExecutor.cs` — the control-flow abstraction.
- `AdbCore/Execution/ControlFlowContext.cs` — what a control-flow executor receives.
- `AdbCore/Execution/ControlFlowResult.cs` — what it returns (outcome + resume node).
- `AdbCore/Execution/ControlFlowExecutorRegistry.cs` — catalogue + `CreateDefault()`.
- `AdbCore/Execution/ControlFlow/LoopControlFlowExecutor.cs` — moved Loop logic.
- `AdbCore/Execution/ControlFlow/ParallelControlFlowExecutor.cs` — moved Run Parallel logic.
- `AdbCore.Tests/Execution/BotGraphTests.cs`
- `AdbCore.Tests/Execution/ControlFlowExecutorRegistryTests.cs`
- `BotBuilder.Core/Integration/TestRunArtifacts.cs` — test-run filename/path builder.
- `BotBuilder.Core.Tests/Integration/TestRunArtifactsTests.cs`
- `BotBuilder.Core.Tests/Targets/TargetBarViewModelTests.cs` (only if absent; otherwise add to existing).
- `BotBuilder.Core.Tests/FitViewportToNodesTests.cs`

**Modified files:**
- `AdbCore/Execution/BotExecutor.cs` — use `BotGraph`; gain optional `ControlFlowExecutorRegistry`; delete hardcoded control-flow branches and moved helpers.
- `BotBuilder.Core/BotEditorViewModel.cs` — add `FitViewportToNodes`.
- `BotBuilder.Core/Targets/TargetBarViewModel.cs` — add `ResolveForNode`.
- `BotBuilder/MainWindow.xaml.cs` — rewire `FitToNodes`, `PickCoordinates_Click`, `PickRegion_Click`, `TestRun_Click`; remove `NodeWidth` const.
- `BotBuilder/MainWindow.xaml` — `core:` namespace + `Width="{x:Static core:NodeLayout.CardWidth}"`.

**Task order matters:** Tasks 1–6 (engine) are sequential — each depends on the previous. Tasks 7–10 (editor) are independent of the engine and of each other.

---

## Task 1: `BotGraph` index (#1, part 1)

**Files:**
- Create: `AdbCore/Execution/BotGraph.cs`
- Test: `AdbCore.Tests/Execution/BotGraphTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `AdbCore.Tests/Execution/BotGraphTests.cs`:

```csharp
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Execution;

public class BotGraphTests
{
    private static BotAction Node(string typeKey, out Guid id)
    {
        id = Guid.NewGuid();
        return new BotAction { Id = id, TypeKey = typeKey, Label = typeKey };
    }

    private static ActionConnection Edge(Guid from, string port, Guid to)
        => new() { Id = Guid.NewGuid(), SourceActionId = from, SourcePort = port, TargetActionId = to, TargetPort = "in" };

    [Fact]
    public void EntryPoint_IsFirstActionWithNoIncomingEdge()
    {
        var a = Node("a", out var aId);
        var b = Node("b", out var bId);
        var bot = new Bot();
        bot.Actions.AddRange(new[] { a, b });
        bot.Connections.Add(Edge(aId, "out", bId));

        var graph = new BotGraph(bot);

        Assert.Same(a, graph.EntryPoint);
    }

    [Fact]
    public void EntryPoint_NullWhenEveryActionHasIncoming()
    {
        var a = Node("a", out var aId);
        var b = Node("b", out var bId);
        var bot = new Bot();
        bot.Actions.AddRange(new[] { a, b });
        bot.Connections.Add(Edge(aId, "out", bId));
        bot.Connections.Add(Edge(bId, "out", aId));

        Assert.Null(new BotGraph(bot).EntryPoint);
    }

    [Fact]
    public void FindNext_ReturnsTargetForWiredPort_NullForUnwired()
    {
        var a = Node("a", out var aId);
        var b = Node("b", out var bId);
        var bot = new Bot();
        bot.Actions.AddRange(new[] { a, b });
        bot.Connections.Add(Edge(aId, "out", bId));

        var graph = new BotGraph(bot);

        Assert.Same(b, graph.FindNext(aId, "out"));
        Assert.Null(graph.FindNext(aId, "onFailure"));
        Assert.Null(graph.FindNext(bId, "out"));
    }

    [Fact]
    public void FindNext_FirstConnectionWins_WhenPortDuplicated()
    {
        var a = Node("a", out var aId);
        var b = Node("b", out var bId);
        var c = Node("c", out var cId);
        var bot = new Bot();
        bot.Actions.AddRange(new[] { a, b, c });
        bot.Connections.Add(Edge(aId, "out", bId)); // first in document order
        bot.Connections.Add(Edge(aId, "out", cId));

        Assert.Same(b, new BotGraph(bot).FindNext(aId, "out"));
    }

    [Fact]
    public void Find_ReturnsActionById_NullWhenAbsent()
    {
        var a = Node("a", out var aId);
        var bot = new Bot();
        bot.Actions.Add(a);

        var graph = new BotGraph(bot);

        Assert.Same(a, graph.Find(aId));
        Assert.Null(graph.Find(Guid.NewGuid()));
    }

    [Fact]
    public void Outgoing_ReturnsEdgesFromAction_EmptyWhenNone()
    {
        var a = Node("a", out var aId);
        var b = Node("b", out var bId);
        var bot = new Bot();
        bot.Actions.AddRange(new[] { a, b });
        bot.Connections.Add(Edge(aId, "out", bId));

        var graph = new BotGraph(bot);

        Assert.Single(graph.Outgoing(aId));
        Assert.Empty(graph.Outgoing(bId));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~BotGraphTests"`
Expected: FAIL — `BotGraph` does not exist (compile error).

- [ ] **Step 3: Implement `BotGraph`**

Create `AdbCore/Execution/BotGraph.cs`:

```csharp
using AdbCore.Models;

namespace AdbCore.Execution;

/// <summary>An index over a <see cref="Bot"/>'s actions and connections, built once per run. Replaces the
/// repeated linear <c>FirstOrDefault</c> scans the graph walk would otherwise perform on every hop.</summary>
public sealed class BotGraph
{
    private readonly Dictionary<Guid, BotAction> _byId;
    private readonly Dictionary<Guid, List<ActionConnection>> _outgoing;

    public BotGraph(Bot bot)
    {
        ArgumentNullException.ThrowIfNull(bot);

        _byId = new Dictionary<Guid, BotAction>(bot.Actions.Count);
        foreach (var action in bot.Actions)
        {
            _byId[action.Id] = action;
        }

        _outgoing = new Dictionary<Guid, List<ActionConnection>>();
        var withIncoming = new HashSet<Guid>();
        foreach (var connection in bot.Connections)
        {
            if (!_outgoing.TryGetValue(connection.SourceActionId, out var edges))
            {
                edges = new List<ActionConnection>();
                _outgoing[connection.SourceActionId] = edges;
            }
            edges.Add(connection);
            withIncoming.Add(connection.TargetActionId);
        }

        EntryPoint = bot.Actions.FirstOrDefault(a => !withIncoming.Contains(a.Id));
    }

    /// <summary>The entry point: the first action (document order) with no incoming connection, or null when
    /// every action has one.</summary>
    public BotAction? EntryPoint { get; }

    /// <summary>The action with the given id, or null.</summary>
    public BotAction? Find(Guid id) => _byId.GetValueOrDefault(id);

    /// <summary>The action reached by following <paramref name="sourcePort"/> out of
    /// <paramref name="fromActionId"/>, or null when that port is unwired. Matches the first connection on
    /// that port in document order.</summary>
    public BotAction? FindNext(Guid fromActionId, string sourcePort)
    {
        if (!_outgoing.TryGetValue(fromActionId, out var edges))
        {
            return null;
        }
        var edge = edges.FirstOrDefault(c => c.SourcePort == sourcePort);
        return edge is null ? null : Find(edge.TargetActionId);
    }

    /// <summary>The outgoing connections from <paramref name="fromActionId"/> (empty when none).</summary>
    public IReadOnlyList<ActionConnection> Outgoing(Guid fromActionId)
        => _outgoing.TryGetValue(fromActionId, out var edges) ? edges : Array.Empty<ActionConnection>();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~BotGraphTests"`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Execution/BotGraph.cs AdbCore.Tests/Execution/BotGraphTests.cs
git commit -m "Add BotGraph per-run action/connection index"
```

---

## Task 2: Rewire `BotExecutor` to use `BotGraph` (#1, part 2)

This replaces `FindEntryPoint` and `FindNext` with `BotGraph` calls and threads `BotGraph` through `RunState` and the existing `JoinDistances` BFS. Control-flow branches stay hardcoded for now (Task 6 removes them). No new tests — existing `BotExecutorTests`/`Loop`/`Parallel`/`Interpolation` suites are the regression guard.

**Files:**
- Modify: `AdbCore/Execution/BotExecutor.cs`

- [ ] **Step 1: Add `BotGraph` to `RunState` and build it in `RunAsync`**

In `RunState`, add a `BotGraph Graph` property and constructor parameter (first parameter). Replace the `Bot Bot` member with `BotGraph Graph` everywhere it's used. Update the constructor:

```csharp
public RunState(
    BotGraph graph,
    ActionExecutorRegistry executors,
    BotExecutionContext context,
    Action<string> log,
    IProgress<ExecutionProgress>? progress)
{
    Graph = graph;
    Executors = executors;
    Context = context;
    Log = log;
    Progress = progress;
}

public BotGraph Graph { get; }
```

In `RunAsync`, replace `var entry = FindEntryPoint(bot);` and the `RunState` construction:

```csharp
var graph = new BotGraph(bot);
var entry = graph.EntryPoint;
if (entry is null)
{
    return new ExecutionResult
    {
        Success = false,
        ErrorMessage = "No entry point: every action has an incoming connection.",
    };
}

var state = new RunState(graph, _executors, context, options.Log ?? (_ => { }), progress);
```

- [ ] **Step 2: Replace `FindNext(state.Bot, …)` calls with `state.Graph.FindNext(…)`**

Every call of the form `FindNext(state.Bot, X, Y)` becomes `state.Graph.FindNext(X, Y)`. There are calls in `WalkAsync` (the `FailurePort` lookup and the trailing `current = FindNext(...)`), in `ExecuteLoopAsync` (`BodyPort`, `DonePort`), and in `ExecuteParallelAsync` (`BranchPort`, `SomeFailedPort`, `AllSucceededPort`).

In `FindConvergentJoin` and `JoinDistances`, replace the `Bot bot` parameter with `BotGraph graph`, and inside `JoinDistances` replace `bot.Actions.FirstOrDefault(a => a.Id == id)` with `graph.Find(id)` and `bot.Connections.Where(c => c.SourceActionId == id)` with `graph.Outgoing(id)`. Update their call sites to pass `state.Graph`.

- [ ] **Step 3: Delete the now-unused static helpers**

Delete `private static BotAction? FindEntryPoint(Bot bot)` and `private static BotAction? FindNext(Bot bot, Guid fromActionId, string sourcePort)` — both are fully replaced by `BotGraph`.

- [ ] **Step 4: Build and run the engine test suites**

Run: `dotnet build ADB.slnx` (expect 0 warnings, 0 errors), then
`dotnet test AdbCore.Tests --filter "FullyQualifiedName~Execution"`
Expected: PASS — all execution tests (BotExecutor, Loop, Parallel, Interpolation, TargetResolution) green.

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Execution/BotExecutor.cs
git commit -m "Use BotGraph index in BotExecutor instead of linear scans"
```

---

## Task 3: Extract `WalkOutcome` to a public type (#2, prep)

**Files:**
- Create: `AdbCore/Execution/WalkOutcome.cs`
- Modify: `AdbCore/Execution/BotExecutor.cs` (remove the nested class)

- [ ] **Step 1: Create the public `WalkOutcome`**

Create `AdbCore/Execution/WalkOutcome.cs` (verbatim copy of the current nested class, made public, top-level):

```csharp
namespace AdbCore.Execution;

/// <summary>The result of walking a (sub-)path of the action graph: completed, or failed at a specific
/// action. Returned by the graph walk and by <see cref="IControlFlowExecutor"/> implementations.</summary>
public sealed class WalkOutcome
{
    public bool Success { get; private init; }
    public string? ErrorMessage { get; private init; }
    public Guid? FailedActionId { get; private init; }

    public static WalkOutcome Completed() => new() { Success = true };

    public static WalkOutcome Failed(string? errorMessage, Guid failedActionId)
        => new() { Success = false, ErrorMessage = errorMessage, FailedActionId = failedActionId };
}
```

- [ ] **Step 2: Remove the nested `WalkOutcome` from `BotExecutor`**

Delete the `private sealed class WalkOutcome { … }` nested at the bottom of `BotExecutor.cs`. Everything else still refers to `WalkOutcome` unqualified (same namespace) — no other edits needed.

- [ ] **Step 3: Build and test**

Run: `dotnet build ADB.slnx` then `dotnet test AdbCore.Tests --filter "FullyQualifiedName~Execution"`
Expected: PASS (no behavior change).

- [ ] **Step 4: Commit**

```bash
git add AdbCore/Execution/WalkOutcome.cs AdbCore/Execution/BotExecutor.cs
git commit -m "Extract WalkOutcome to a public top-level type"
```

---

## Task 4: Control-flow abstraction types + registry (#2, part 1)

**Files:**
- Create: `AdbCore/Execution/IControlFlowExecutor.cs`
- Create: `AdbCore/Execution/ControlFlowContext.cs`
- Create: `AdbCore/Execution/ControlFlowResult.cs`
- Create: `AdbCore/Execution/ControlFlowExecutorRegistry.cs`
- Test: `AdbCore.Tests/Execution/ControlFlowExecutorRegistryTests.cs`

> Note: `ControlFlowExecutorRegistry.CreateDefault()` references `LoopControlFlowExecutor` and `ParallelControlFlowExecutor`, which are created in Tasks 5–6. To keep this task building on its own, **CreateDefault returns an empty registry in this task** and is filled in Task 6 once both executors exist. The registry tests here cover Register/TryGet only.

- [ ] **Step 1: Write the failing registry tests**

Create `AdbCore.Tests/Execution/ControlFlowExecutorRegistryTests.cs`:

```csharp
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Execution;

public class ControlFlowExecutorRegistryTests
{
    private sealed class FakeControlFlow : IControlFlowExecutor
    {
        public required string TypeKey { get; init; }
        public Task<ControlFlowResult> ExecuteAsync(ControlFlowContext context, CancellationToken ct)
            => Task.FromResult(ControlFlowResult.Continue(null));
    }

    [Fact]
    public void Register_ThenTryGet_ReturnsExecutor()
    {
        var registry = new ControlFlowExecutorRegistry();
        var cf = new FakeControlFlow { TypeKey = "control.fake" };
        registry.Register(cf);

        Assert.True(registry.TryGet("control.fake", out var found));
        Assert.Same(cf, found);
    }

    [Fact]
    public void TryGet_UnknownKey_ReturnsFalse()
    {
        var registry = new ControlFlowExecutorRegistry();
        Assert.False(registry.TryGet("nope", out var found));
        Assert.Null(found);
    }

    [Fact]
    public void Register_DuplicateKey_Throws()
    {
        var registry = new ControlFlowExecutorRegistry();
        registry.Register(new FakeControlFlow { TypeKey = "dup" });
        Assert.Throws<InvalidOperationException>(() => registry.Register(new FakeControlFlow { TypeKey = "dup" }));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~ControlFlowExecutorRegistryTests"`
Expected: FAIL — types don't exist (compile error).

- [ ] **Step 3: Create the four types**

`AdbCore/Execution/IControlFlowExecutor.cs`:

```csharp
namespace AdbCore.Execution;

/// <summary>An engine-native control-flow node (Loop, Run Parallel, …) that orchestrates sub-walks rather
/// than performing a single leaf action. Resolved by <see cref="ControlFlowExecutorRegistry"/> from its
/// TypeKey, mirroring how leaf actions resolve via <see cref="ActionExecutorRegistry"/>.</summary>
public interface IControlFlowExecutor
{
    /// <summary>Unique key matching the corresponding control-flow <c>IActionDefinition.TypeKey</c>.</summary>
    string TypeKey { get; }

    Task<ControlFlowResult> ExecuteAsync(ControlFlowContext context, CancellationToken ct);
}
```

`AdbCore/Execution/ControlFlowResult.cs`:

```csharp
using AdbCore.Models;

namespace AdbCore.Execution;

/// <summary>What an <see cref="IControlFlowExecutor"/> returns: either a halting failure, or success plus the
/// node from which the parent walk should resume (null = the path ends here).</summary>
public sealed class ControlFlowResult
{
    private ControlFlowResult(WalkOutcome outcome, BotAction? next)
    {
        Outcome = outcome;
        Next = next;
    }

    public WalkOutcome Outcome { get; }
    public BotAction? Next { get; }

    /// <summary>Success; resume the parent walk at <paramref name="next"/> (null ends the path).</summary>
    public static ControlFlowResult Continue(BotAction? next) => new(WalkOutcome.Completed(), next);

    /// <summary>Halt the walk with the given failure outcome.</summary>
    public static ControlFlowResult Halt(WalkOutcome failure) => new(failure, null);
}
```

`AdbCore/Execution/ControlFlowContext.cs`:

```csharp
using AdbCore.Models;

namespace AdbCore.Execution;

/// <summary>Everything an <see cref="IControlFlowExecutor"/> needs to orchestrate sub-walks: the graph index,
/// the control-flow node being executed, run-wide state, the log sink, and a callback to walk a sub-path.</summary>
public sealed class ControlFlowContext
{
    private readonly Func<BotAction?, Guid?, CancellationToken, Task<WalkOutcome>> _walk;

    public ControlFlowContext(
        BotGraph graph,
        BotAction action,
        BotExecutionContext runContext,
        Action<string> log,
        Func<BotAction?, Guid?, CancellationToken, Task<WalkOutcome>> walk)
    {
        Graph = graph;
        Action = action;
        RunContext = runContext;
        Log = log;
        _walk = walk;
    }

    /// <summary>The per-run action/connection index.</summary>
    public BotGraph Graph { get; }

    /// <summary>The control-flow node being executed.</summary>
    public BotAction Action { get; }

    /// <summary>Run-wide state (variables, resolved targets).</summary>
    public BotExecutionContext RunContext { get; }

    /// <summary>Emits a message to the run log sink.</summary>
    public Action<string> Log { get; }

    /// <summary>Walks a sub-path from <paramref name="start"/>, optionally stopping before
    /// <paramref name="stopBeforeId"/> (used to halt parallel branches at their convergent Join).</summary>
    public Task<WalkOutcome> WalkAsync(BotAction? start, CancellationToken ct, Guid? stopBeforeId = null)
        => _walk(start, stopBeforeId, ct);
}
```

`AdbCore/Execution/ControlFlowExecutorRegistry.cs`:

```csharp
namespace AdbCore.Execution;

/// <summary>Catalogue of engine-native control-flow executors, keyed by
/// <see cref="IControlFlowExecutor.TypeKey"/>. Mirrors <see cref="ActionExecutorRegistry"/>.</summary>
public sealed class ControlFlowExecutorRegistry
{
    private readonly Dictionary<string, IControlFlowExecutor> _byKey = new(StringComparer.Ordinal);

    public int Count => _byKey.Count;

    public void Register(IControlFlowExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        if (!_byKey.TryAdd(executor.TypeKey, executor))
        {
            throw new InvalidOperationException(
                $"A control-flow executor with TypeKey '{executor.TypeKey}' is already registered.");
        }
    }

    public bool TryGet(string typeKey, out IControlFlowExecutor? executor)
        => _byKey.TryGetValue(typeKey, out executor);

    /// <summary>The default set wired into <see cref="BotExecutor"/>. Populated in Task 6 with Loop and
    /// Run Parallel; returns an empty registry until then.</summary>
    public static ControlFlowExecutorRegistry CreateDefault() => new();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~ControlFlowExecutorRegistryTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Execution/IControlFlowExecutor.cs AdbCore/Execution/ControlFlowContext.cs AdbCore/Execution/ControlFlowResult.cs AdbCore/Execution/ControlFlowExecutorRegistry.cs AdbCore.Tests/Execution/ControlFlowExecutorRegistryTests.cs
git commit -m "Add IControlFlowExecutor abstraction and registry"
```

---

## Task 5: `LoopControlFlowExecutor` (#2, part 2)

Moves `BotExecutor.ExecuteLoopAsync` + `SplitItems` verbatim into an executor, navigating via `ControlFlowContext`. The existing `LoopExecutionTests` are the behavior guard. **Do not delete the originals from `BotExecutor` yet** (Task 6 does, when the dispatch is rewired) — at this point `BotExecutor` still uses its own copy, so both compile.

**Files:**
- Create: `AdbCore/Execution/ControlFlow/LoopControlFlowExecutor.cs`

- [ ] **Step 1: Create the executor**

```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;

namespace AdbCore.Execution.ControlFlow;

/// <summary>Engine-native Loop: re-walks the Body sub-path once per iteration (count or for-each), setting the
/// optional index/item variables, then resumes the parent walk at the Done port.</summary>
public sealed class LoopControlFlowExecutor : IControlFlowExecutor
{
    public string TypeKey => LoopAction.LoopTypeKey;

    public async Task<ControlFlowResult> ExecuteAsync(ControlFlowContext context, CancellationToken ct)
    {
        var loop = context.Action;
        var bodyStart = context.Graph.FindNext(loop.Id, LoopAction.BodyPort);
        var mode = ConfigValues.GetString(loop.Config, LoopAction.ModeKey, LoopAction.ModeCount);
        var indexVar = ConfigValues.GetString(loop.Config, LoopAction.IndexVariableKey);
        var itemVar = ConfigValues.GetString(loop.Config, LoopAction.ItemVariableKey);

        IReadOnlyList<string?> items;
        if (string.Equals(mode, LoopAction.ModeForEach, StringComparison.OrdinalIgnoreCase))
        {
            var collectionVar = ConfigValues.GetString(loop.Config, LoopAction.CollectionVariableKey);
            var raw = !string.IsNullOrEmpty(collectionVar)
                && context.RunContext.Variables.TryGetValue(collectionVar, out var v) ? v : null;
            items = SplitItems(raw);
        }
        else
        {
            // Fallback matches LoopAction's "count" ConfigField.DefaultValue (1): a dropped Loop whose Count
            // was never edited has no "count" key in Config, yet should iterate once, not zero times.
            var count = Math.Max(0, ConfigValues.GetInt(loop.Config, LoopAction.CountKey, 1));
            items = new string?[count];
        }

        for (var i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (!string.IsNullOrEmpty(indexVar))
            {
                context.RunContext.Variables[indexVar] = i;
            }
            if (!string.IsNullOrEmpty(itemVar) && items[i] is not null)
            {
                context.RunContext.Variables[itemVar] = items[i]!;
            }

            var bodyOutcome = await context.WalkAsync(bodyStart, ct);
            if (!bodyOutcome.Success)
            {
                return ControlFlowResult.Halt(bodyOutcome);
            }
        }

        return ControlFlowResult.Continue(context.Graph.FindNext(loop.Id, LoopAction.DonePort));
    }

    /// <summary>For-each item source: a comma-separated string. Empty/whitespace yields no items; each item
    /// is trimmed.</summary>
    private static IReadOnlyList<string?> SplitItems(object? raw)
    {
        var text = ConfigValues.AsString(raw);
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string?>();
        }
        return text.Split(',').Select(part => (string?)part.Trim()).ToList();
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build ADB.slnx`
Expected: 0 warnings, 0 errors. (Not yet wired in; just compiles.)

- [ ] **Step 3: Commit**

```bash
git add AdbCore/Execution/ControlFlow/LoopControlFlowExecutor.cs
git commit -m "Add LoopControlFlowExecutor (not yet wired)"
```

---

## Task 6: `ParallelControlFlowExecutor` + wire the dispatch (#2, part 3)

Creates the parallel executor (moving `ExecuteParallelAsync`, `ParseStrategy`, `FindConvergentJoin`, `JoinDistances`), fills `CreateDefault()`, rewires `BotExecutor.WalkAsync` to consult the registry, and **deletes** the now-dead hardcoded branches and moved methods from `BotExecutor`. The full `LoopExecutionTests` + `ParallelExecutionTests` are the guard.

**Files:**
- Create: `AdbCore/Execution/ControlFlow/ParallelControlFlowExecutor.cs`
- Modify: `AdbCore/Execution/ControlFlowExecutorRegistry.cs` (fill `CreateDefault`)
- Modify: `AdbCore/Execution/BotExecutor.cs` (add registry field/ctor param; registry-driven dispatch; delete moved code)

- [ ] **Step 1: Create `ParallelControlFlowExecutor`**

```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Models;

namespace AdbCore.Execution.ControlFlow;

/// <summary>Engine-native Run Parallel: runs each wired branch concurrently as a sub-walk that stops at the
/// convergent Join, aggregates the outcomes per <see cref="ParallelErrorStrategy"/>, and resumes the parent
/// walk at the Join's allSucceeded/someFailed port — or halts the run.</summary>
public sealed class ParallelControlFlowExecutor : IControlFlowExecutor
{
    public string TypeKey => RunParallelAction.RunParallelTypeKey;

    public async Task<ControlFlowResult> ExecuteAsync(ControlFlowContext context, CancellationToken ct)
    {
        var graph = context.Graph;
        var runParallel = context.Action;

        var strategy = ParseStrategy(
            ConfigValues.GetString(runParallel.Config, RunParallelAction.OnBranchFailureKey, nameof(ParallelErrorStrategy.HaltAll)));
        var branchCount = Math.Max(1,
            ConfigValues.GetInt(runParallel.Config, RunParallelAction.BranchesKey, RunParallelAction.DefaultBranchCount));

        var branchStarts = new List<BotAction>();
        for (var i = 1; i <= branchCount; i++)
        {
            var start = graph.FindNext(runParallel.Id, RunParallelAction.BranchPort(i));
            if (start is not null)
            {
                branchStarts.Add(start);
            }
        }

        if (branchStarts.Count == 0)
        {
            return ControlFlowResult.Halt(WalkOutcome.Failed("Run Parallel has no wired branch ports.", runParallel.Id));
        }

        var joinId = FindConvergentJoin(graph, branchStarts.Select(b => b.Id).ToList());
        if (joinId is null)
        {
            return ControlFlowResult.Halt(WalkOutcome.Failed("Run Parallel branches must converge on exactly one Join.", runParallel.Id));
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var outcomes = new WalkOutcome[branchStarts.Count];

        async Task RunBranchAsync(int index)
        {
            try
            {
                var outcome = await context.WalkAsync(branchStarts[index], linked.Token, joinId.Value);
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
            return ControlFlowResult.Continue(graph.FindNext(joinId.Value, JoinAction.AllSucceededPort));
        }

        // A branch failed. If someFailed is wired, route to it (handled) regardless of strategy.
        if (graph.FindNext(joinId.Value, JoinAction.SomeFailedPort) is not null)
        {
            return ControlFlowResult.Continue(graph.FindNext(joinId.Value, JoinAction.SomeFailedPort));
        }

        // Unhandled failure (someFailed unwired). Continue treats it as a warning and lets the run proceed
        // (the someFailed route simply dead-ends, hence null); the Halt strategies fail the run.
        if (strategy == ParallelErrorStrategy.Continue)
        {
            return ControlFlowResult.Continue(graph.FindNext(joinId.Value, JoinAction.SomeFailedPort));
        }

        return ControlFlowResult.Halt(WalkOutcome.Failed(firstFailure.ErrorMessage, firstFailure.FailedActionId ?? runParallel.Id));
    }

    private static ParallelErrorStrategy ParseStrategy(string value)
        => Enum.TryParse<ParallelErrorStrategy>(value, ignoreCase: true, out var s) ? s : ParallelErrorStrategy.HaltAll;

    /// <summary>Finds the single Join node all branches converge on, choosing the nearest common Join when more
    /// than one is reachable from every branch. Returns null if zero, or an ambiguous tie.</summary>
    private static Guid? FindConvergentJoin(BotGraph graph, IReadOnlyList<Guid> branchStartIds)
    {
        var perBranch = branchStartIds.Select(id => JoinDistances(graph, id)).ToList();
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
    private static Dictionary<Guid, int> JoinDistances(BotGraph graph, Guid startId)
    {
        var distances = new Dictionary<Guid, int>();
        var visited = new HashSet<Guid> { startId };
        var queue = new Queue<(Guid Id, int Depth)>();
        queue.Enqueue((startId, 0));

        while (queue.Count > 0)
        {
            var (id, depth) = queue.Dequeue();
            var node = graph.Find(id);
            if (node is not null && node.TypeKey == JoinAction.JoinTypeKey && !distances.ContainsKey(id))
            {
                distances[id] = depth;
            }

            foreach (var edge in graph.Outgoing(id))
            {
                if (visited.Add(edge.TargetActionId))
                {
                    queue.Enqueue((edge.TargetActionId, depth + 1));
                }
            }
        }

        return distances;
    }
}
```

- [ ] **Step 2: Fill `CreateDefault()`**

In `AdbCore/Execution/ControlFlowExecutorRegistry.cs`, add `using AdbCore.Execution.ControlFlow;` at the top and replace `CreateDefault`:

```csharp
/// <summary>The default set wired into <see cref="BotExecutor"/>: Loop and Run Parallel.</summary>
public static ControlFlowExecutorRegistry CreateDefault()
{
    var registry = new ControlFlowExecutorRegistry();
    registry.Register(new LoopControlFlowExecutor());
    registry.Register(new ParallelControlFlowExecutor());
    return registry;
}
```

- [ ] **Step 3: Rewire `BotExecutor` dispatch and delete moved code**

In `BotExecutor.cs`:

(a) Add the field and optional constructor parameter:

```csharp
private readonly ActionExecutorRegistry _executors;
private readonly ControlFlowExecutorRegistry _controlFlow;

public BotExecutor(ActionExecutorRegistry executors, ControlFlowExecutorRegistry? controlFlow = null)
{
    ArgumentNullException.ThrowIfNull(executors);
    _executors = executors;
    _controlFlow = controlFlow ?? ControlFlowExecutorRegistry.CreateDefault();
}
```

(b) Add `ControlFlowExecutorRegistry ControlFlow` to `RunState` (new constructor parameter + property), and pass `_controlFlow` when building `state` in `RunAsync`:

```csharp
var state = new RunState(graph, _executors, _controlFlow, context, options.Log ?? (_ => { }), progress);
```

```csharp
public RunState(
    BotGraph graph,
    ActionExecutorRegistry executors,
    ControlFlowExecutorRegistry controlFlow,
    BotExecutionContext context,
    Action<string> log,
    IProgress<ExecutionProgress>? progress)
{
    Graph = graph;
    Executors = executors;
    ControlFlow = controlFlow;
    Context = context;
    Log = log;
    Progress = progress;
}

public BotGraph Graph { get; }
public ActionExecutorRegistry Executors { get; }
public ControlFlowExecutorRegistry ControlFlow { get; }
```

(c) Replace the two hardcoded control-flow `if` blocks in `WalkAsync` (the `LoopAction.LoopTypeKey` block and the `RunParallelAction.RunParallelTypeKey` block) with one registry-driven block placed immediately after the `stopBeforeId` check:

```csharp
if (state.ControlFlow.TryGet(current.TypeKey, out var controlFlow) && controlFlow is not null)
{
    var cfContext = new ControlFlowContext(
        state.Graph, current, state.Context, state.Log,
        (start, stop, token) => WalkAsync(state, start, token, stop));
    var cfResult = await controlFlow.ExecuteAsync(cfContext, ct);
    if (!cfResult.Outcome.Success)
    {
        return cfResult.Outcome;
    }
    current = cfResult.Next;
    continue;
}
```

(d) Delete from `BotExecutor`: `ExecuteLoopAsync`, `SplitItems`, `ExecuteParallelAsync`, `ParseStrategy`, `FindConvergentJoin`, `JoinDistances`. Keep `ExecuteWithRetryAsync`, `WalkAsync` (leaf path), `RunAsync`, `RunState`. Remove any now-unused `using AdbCore.Actions.BuiltIn;` only if the file no longer references it (the leaf path no longer does — verify the build).

- [ ] **Step 4: Build and run the FULL suite**

Run: `dotnet build ADB.slnx` (0 warnings), then `dotnet test ADB.slnx`
Expected: PASS — **710 tests, 0 failures.** Loop and Parallel suites are the proof the move preserved behavior.

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Execution/ControlFlow/ParallelControlFlowExecutor.cs AdbCore/Execution/ControlFlowExecutorRegistry.cs AdbCore/Execution/BotExecutor.cs
git commit -m "Make control-flow dispatch registry-driven; move Loop/Parallel out of BotExecutor"
```

---

## Task 7: `BotEditorViewModel.FitViewportToNodes` (A)

**Files:**
- Modify: `BotBuilder.Core/BotEditorViewModel.cs`
- Modify: `BotBuilder/MainWindow.xaml.cs`
- Test: `BotBuilder.Core.Tests/FitViewportToNodesTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `BotBuilder.Core.Tests/FitViewportToNodesTests.cs`:

```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class FitViewportToNodesTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    [Fact]
    public void FitViewportToNodes_NoNodes_LeavesViewportUnchanged()
    {
        var editor = NewEditor();
        editor.New();

        editor.FitViewportToNodes(800, 600);

        Assert.Equal(1.0, editor.Viewport.Scale);
        Assert.Equal(0, editor.Viewport.OffsetX);
        Assert.Equal(0, editor.Viewport.OffsetY);
    }

    [Fact]
    public void FitViewportToNodes_CentersTheNodeBoundingBox()
    {
        var editor = NewEditor();
        editor.New();
        var a = editor.AddNode("control.start", 0, 0);
        var b = editor.AddNode("control.end", 300, 200);

        editor.FitViewportToNodes(800, 600);

        // Expected bounds use the canonical card width and per-node heights.
        var minX = 0d;
        var minY = 0d;
        var maxX = 300 + NodeLayout.CardWidth;
        var maxY = Math.Max(a.Height, 200 + b.Height);
        var expectedCentreX = (minX + maxX) / 2;
        var expectedCentreY = (minY + maxY) / 2;

        // After fitting, the viewport centre should map back to the bounding-box centre.
        var (worldX, worldY) = editor.Viewport.ScreenToWorld(400, 300);
        Assert.Equal(expectedCentreX, worldX, 3);
        Assert.Equal(expectedCentreY, worldY, 3);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test BotBuilder.Core.Tests --filter "FullyQualifiedName~FitViewportToNodesTests"`
Expected: FAIL — `FitViewportToNodes` does not exist.

- [ ] **Step 3: Implement `FitViewportToNodes`**

In `BotBuilder.Core/BotEditorViewModel.cs`, add this public method (e.g. just after `AutoLayout`). `NodeLayout` is in the same `BotBuilder.Core` namespace — no extra using needed:

```csharp
/// <summary>Frames the viewport so every node is visible — the "I panned away and lost my graph" rescue.
/// No-op when there are no nodes. Node width is the canonical <see cref="NodeLayout.CardWidth"/>; height is
/// each node's own.</summary>
public void FitViewportToNodes(double viewportWidth, double viewportHeight)
{
    if (Nodes.Count == 0)
    {
        return;
    }

    double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
    foreach (var n in Nodes)
    {
        minX = Math.Min(minX, n.X);
        minY = Math.Min(minY, n.Y);
        maxX = Math.Max(maxX, n.X + NodeLayout.CardWidth);
        maxY = Math.Max(maxY, n.Y + n.Height);
    }

    Viewport.FitTo(minX, minY, maxX, maxY, viewportWidth, viewportHeight);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test BotBuilder.Core.Tests --filter "FullyQualifiedName~FitViewportToNodesTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Rewire the code-behind**

In `BotBuilder/MainWindow.xaml.cs`:
- Delete the field `private const double NodeWidth = 160;` (and its comment on the line above).
- Replace the entire `FitToNodes()` method body with a one-line delegation:

```csharp
// Reframe the view so every node is visible — the "I panned away and lost my graph" rescue.
private void FitToNodes() => _editor.FitViewportToNodes(ViewportHost.ActualWidth, ViewportHost.ActualHeight);
```

- [ ] **Step 6: Build and test**

Run: `dotnet build ADB.slnx` (0 warnings), then `dotnet test BotBuilder.Core.Tests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add BotBuilder.Core/BotEditorViewModel.cs BotBuilder/MainWindow.xaml.cs BotBuilder.Core.Tests/FitViewportToNodesTests.cs
git commit -m "Move fit-to-nodes bounds math into BotEditorViewModel (A)"
```

---

## Task 8: `TargetBarViewModel.ResolveForNode` (B)

**Files:**
- Modify: `BotBuilder.Core/Targets/TargetBarViewModel.cs`
- Modify: `BotBuilder/MainWindow.xaml.cs`
- Test: `BotBuilder.Core.Tests/Targets/TargetBarViewModelTests.cs` (create if absent)

> **Behavior to preserve exactly:** the original inline code returns `null` (NOT the first target) when a node's `TargetId` is set but no target with that id exists. Keep that — do not fall back to the first target in that case.

- [ ] **Step 1: Write the failing tests**

Create `BotBuilder.Core.Tests/Targets/TargetBarViewModelTests.cs` (if a file by this name already exists, append these `[Fact]` methods to its class instead):

```csharp
using AdbCore.Models;
using BotBuilder.Core.Targets;
using Xunit;

namespace BotBuilder.Core.Tests.Targets;

public class TargetBarViewModelTests
{
    [Fact]
    public void ResolveForNode_NoTargets_ReturnsNull()
    {
        var bar = new TargetBarViewModel();
        Assert.Null(bar.ResolveForNode(null));
        Assert.Null(bar.ResolveForNode(Guid.NewGuid()));
    }

    [Fact]
    public void ResolveForNode_NullId_ReturnsFirstTarget()
    {
        var bar = new TargetBarViewModel();
        var first = bar.AddTarget();
        bar.AddTarget();

        Assert.Same(first, bar.ResolveForNode(null));
    }

    [Fact]
    public void ResolveForNode_KnownId_ReturnsThatTarget()
    {
        var bar = new TargetBarViewModel();
        bar.AddTarget();
        var second = bar.AddTarget();

        Assert.Same(second, bar.ResolveForNode(second.Id));
    }

    [Fact]
    public void ResolveForNode_UnknownId_ReturnsNull()
    {
        var bar = new TargetBarViewModel();
        bar.AddTarget();

        Assert.Null(bar.ResolveForNode(Guid.NewGuid()));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test BotBuilder.Core.Tests --filter "FullyQualifiedName~TargetBarViewModelTests"`
Expected: FAIL — `ResolveForNode` does not exist.

- [ ] **Step 3: Implement `ResolveForNode`**

In `BotBuilder.Core/Targets/TargetBarViewModel.cs`, add:

```csharp
/// <summary>Resolves the target a node is bound to: its explicit <paramref name="targetId"/> when set
/// (returning null when set but no longer present), otherwise the first target — or null when there are
/// no targets at all.</summary>
public TargetViewModel? ResolveForNode(Guid? targetId)
    => targetId is Guid id
        ? Targets.FirstOrDefault(t => t.Id == id)
        : Targets.FirstOrDefault();
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test BotBuilder.Core.Tests --filter "FullyQualifiedName~TargetBarViewModelTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Rewire the two call sites**

In `BotBuilder/MainWindow.xaml.cs`, inside `PickCoordinates_Click`, replace:

```csharp
var targets = _editor.TargetBar.Targets;
var target = node.TargetId is System.Guid id
    ? targets.FirstOrDefault(t => t.Id == id)
    : targets.FirstOrDefault();
```

with:

```csharp
var target = _editor.TargetBar.ResolveForNode(node.TargetId);
```

Do the identical replacement inside `PickRegion_Click`. (Both methods keep their subsequent `if (target is null) { MessageBox.Show(...); return; }` guard unchanged.)

- [ ] **Step 6: Build and test**

Run: `dotnet build ADB.slnx` (0 warnings), then `dotnet test BotBuilder.Core.Tests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add BotBuilder.Core/Targets/TargetBarViewModel.cs BotBuilder/MainWindow.xaml.cs BotBuilder.Core.Tests/Targets/TargetBarViewModelTests.cs
git commit -m "Extract node target resolution to TargetBarViewModel.ResolveForNode (B)"
```

---

## Task 9: `TestRunArtifacts` path/filename helper (C)

**Files:**
- Create: `BotBuilder.Core/Integration/TestRunArtifacts.cs`
- Modify: `BotBuilder/MainWindow.xaml.cs`
- Test: `BotBuilder.Core.Tests/Integration/TestRunArtifactsTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `BotBuilder.Core.Tests/Integration/TestRunArtifactsTests.cs`:

```csharp
using System.IO;
using BotBuilder.Core.Integration;
using Xunit;

namespace BotBuilder.Core.Tests.Integration;

public class TestRunArtifactsTests
{
    [Fact]
    public void SafeFileName_StripsInvalidCharacters()
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = "My" + invalid[0] + "Bot" + invalid[^1];

        Assert.Equal("MyBot", TestRunArtifacts.SafeFileName(name));
    }

    [Fact]
    public void SafeFileName_KeepsValidName()
    {
        Assert.Equal("Grinder 9000", TestRunArtifacts.SafeFileName("Grinder 9000"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SafeFileName_EmptyOrWhitespace_FallsBackToBot(string name)
    {
        Assert.Equal("bot", TestRunArtifacts.SafeFileName(name));
    }

    [Fact]
    public void SafeFileName_AllInvalidChars_FallsBackToBot()
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(new[] { invalid[0], invalid[1] });

        Assert.Equal("bot", TestRunArtifacts.SafeFileName(name));
    }

    [Fact]
    public void TempBotPath_ComposesRootSubdirAndSafeName()
    {
        var path = TestRunArtifacts.TempBotPath("C:\\temp", "My Bot");

        Assert.Equal(Path.Combine("C:\\temp", "adb-testrun", "My Bot.bot"), path);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test BotBuilder.Core.Tests --filter "FullyQualifiedName~TestRunArtifactsTests"`
Expected: FAIL — `TestRunArtifacts` does not exist.

- [ ] **Step 3: Implement `TestRunArtifacts`**

Create `BotBuilder.Core/Integration/TestRunArtifacts.cs`:

```csharp
namespace BotBuilder.Core.Integration;

/// <summary>Builds the temp <c>.bot</c> path a Test Run serializes to, sanitising the (free-form) bot name
/// into a valid filename. Pure for testing; the runtime caller supplies <c>Path.GetTempPath()</c> as the
/// root and ensures the directory exists.</summary>
public static class TestRunArtifacts
{
    private const string SubdirectoryName = "adb-testrun";

    /// <summary>Strips characters invalid in a filename from <paramref name="botName"/>, falling back to
    /// <c>"bot"</c> when nothing usable remains.</summary>
    public static string SafeFileName(string botName)
    {
        var safe = string.Concat(botName.Split(Path.GetInvalidFileNameChars()));
        return string.IsNullOrWhiteSpace(safe) ? "bot" : safe;
    }

    /// <summary>The temp <c>.bot</c> path for a Test Run:
    /// <paramref name="tempRoot"/>/adb-testrun/&lt;safe-name&gt;.bot.</summary>
    public static string TempBotPath(string tempRoot, string botName)
        => Path.Combine(tempRoot, SubdirectoryName, $"{SafeFileName(botName)}.bot");
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test BotBuilder.Core.Tests --filter "FullyQualifiedName~TestRunArtifactsTests"`
Expected: PASS (6 tests).

- [ ] **Step 5: Rewire `TestRun_Click`**

In `BotBuilder/MainWindow.xaml.cs`, replace step 1 of `TestRun_Click` (the temp-dir + safe-name + `botPath` block, currently lines that build `dir`, `safeName`, and `botPath`) with:

```csharp
// 1. Serialize the current editor state to a temp .bot so the run never depends on a saved file.
var botPath = BotBuilder.Core.Integration.TestRunArtifacts.TempBotPath(
    System.IO.Path.GetTempPath(), _editor.BotName);
System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(botPath)!);
_editor.Save(botPath);
```

Leave steps 2–4 of the method (runner lookup, target picker, spawn) unchanged.

- [ ] **Step 6: Build and test**

Run: `dotnet build ADB.slnx` (0 warnings), then `dotnet test BotBuilder.Core.Tests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add BotBuilder.Core/Integration/TestRunArtifacts.cs BotBuilder/MainWindow.xaml.cs BotBuilder.Core.Tests/Integration/TestRunArtifactsTests.cs
git commit -m "Extract test-run temp path + filename sanitization to TestRunArtifacts (C)"
```

---

## Task 10: De-duplicate the card-width literal in XAML (D)

No unit test (XAML-only); verified by build + the full suite + (later, by the user) a visual check that node cards still render at 160 wide. `MainWindow.xaml.cs`'s `NodeWidth` const was already removed in Task 7.

**Files:**
- Modify: `BotBuilder/MainWindow.xaml`

- [ ] **Step 1: Add the `core` XML namespace**

In `BotBuilder/MainWindow.xaml`, add this namespace declaration to the root `<Window …>` element, alongside the existing `xmlns:targets=…` (line ~5):

```xml
xmlns:core="clr-namespace:BotBuilder.Core;assembly=BotBuilder.Core"
```

- [ ] **Step 2: Replace the hardcoded width with the canonical constant**

At the node-card `<Border>` (the one with `Width="160" Height="{Binding Height}"`), change `Width="160"` to:

```xml
Width="{x:Static core:NodeLayout.CardWidth}"
```

(`NodeLayout.CardWidth` is a `public const double` = 160, so `x:Static` binds it at parse time.)

- [ ] **Step 3: Build and run the full suite**

Run: `dotnet build ADB.slnx` (0 warnings, 0 errors), then `dotnet test ADB.slnx`
Expected: PASS — 710 existing + new tests, 0 failures.

- [ ] **Step 4: Commit**

```bash
git add BotBuilder/MainWindow.xaml
git commit -m "Bind node-card width to NodeLayout.CardWidth instead of a literal (D)"
```

---

## Final Verification (before PR)

- [ ] `dotnet build ADB.slnx` → 0 warnings, 0 errors.
- [ ] `dotnet test ADB.slnx` → all green (710 prior + ~21 new = ~731), 0 failures.
- [ ] `git log --oneline` shows the ten focused commits.
- [ ] Grep confirms the hardcoded control-flow `if (current.TypeKey == LoopAction…/RunParallelAction…)` branches are gone from `BotExecutor.cs`, and `160` no longer appears as a literal in `MainWindow.xaml`/`MainWindow.xaml.cs`.

---

## Self-Review (performed at plan time)

**Spec coverage:** #1 → Tasks 1–2; #2 → Tasks 3–6; A → Task 7; B → Task 8; C → Task 9; D → Task 10. All six items covered.

**Placeholder scan:** No TBDs; every code step shows complete code; every test step shows the test and the exact filter command.

**Type consistency:** `BotGraph` (`EntryPoint` property, `Find`, `FindNext`, `Outgoing`) used identically in Tasks 1, 2, 5, 6. `WalkOutcome` (`Completed`/`Failed`) consistent across Tasks 3–6. `ControlFlowResult` (`Continue`/`Halt`), `ControlFlowContext` (`Graph`/`Action`/`RunContext`/`Log`/`WalkAsync`), and `IControlFlowExecutor.TypeKey`/`ExecuteAsync` consistent across Tasks 4–6. `FitViewportToNodes(double,double)`, `ResolveForNode(Guid?)`, `TestRunArtifacts.SafeFileName(string)`/`TempBotPath(string,string)` match between definition and call sites. `BotExecutor`'s new optional `ControlFlowExecutorRegistry?` ctor parameter leaves all existing single-arg callers compiling.

**Behavior-preservation traps flagged:** Task 8 preserves the set-but-missing-id → null semantics; Task 5 keeps the Loop count fallback of 1; Task 6 keeps the parallel `someFailed`/strategy routing (including the `Continue` → null dead-end) verbatim.
