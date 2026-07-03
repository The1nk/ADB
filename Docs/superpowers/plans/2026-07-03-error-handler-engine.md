# Global Error Handler node — engine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a per-bot **Error Handler** node that catches any otherwise-unhandled failure and routes execution into an author-wired recovery flow (enabling reboot→reinit→retry loops), plus the engine routing that drives it.

**Architecture:** A new engine-native marker node `control.errorHandler` (modeled on `StartAction` — a no-op executor). `BotGraph` exposes the bot's Error Handler node. `BotExecutor.RunAsync` wraps the top-level walk in a loop: when the walk ends in an unhandled failure and the bot has an Error Handler, it seeds `error.*` run variables and starts a fresh walk from the handler node. The handler's output flow is normal graph wiring; wiring it back to an earlier node builds a retry loop. Iterative at the top level (no stack growth) and cancellable. No Error Handler → behavior is exactly as before.

**Tech Stack:** C# / .NET 10, xUnit, AdbCore execution engine.

**Spec:** `Docs/superpowers/specs/2026-07-03-nested-logging-and-error-handler-design.md` (Feature 2). This PR is the engine slice only; the UI/palette slice is a separate PR.

---

### Task 1: Engine — Error Handler node, graph lookup, and routing

**Files:**
- Create: `AdbCore/Actions/BuiltIn/ErrorHandlerAction.cs`
- Modify: `AdbCore/Actions/BuiltIn/BuiltInActions.cs` (register it)
- Modify: `AdbCore/Execution/BotGraph.cs` (expose `ErrorHandler`; exclude it from entry fallback)
- Modify: `AdbCore/Execution/BotExecutor.cs` (routing loop + `error.*` vars + log marker)
- Test: `AdbCore.Tests/Execution/ErrorHandlerExecutionTests.cs` (create)
- Test: `AdbCore.Tests/Serialization/ErrorHandlerSerializationTests.cs` (create)

Reference patterns: `AdbCore/Actions/BuiltIn/StartAction.cs` (marker node shape), `AdbCore.Tests/Execution/ParallelExecutionTests.cs` (`Node`/`Edge` helpers, `FakeExecutor`), `AdbCore.Tests/Execution/FakeExecutor.cs` (shared test double).

- [ ] **Step 1: Create the Error Handler node**

Create `AdbCore/Actions/BuiltIn/ErrorHandlerAction.cs`:

```csharp
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn;

/// <summary>A per-bot last-chance catch. When present, the engine routes any otherwise-unhandled failure
/// into this node (see <see cref="BotExecutor"/>) and continues from its output — so an author can wire a
/// recovery flow (e.g. reboot + re-initialize) that loops back into the graph. Reached only via the error
/// cascade, never a normal edge; does no work itself.</summary>
public sealed class ErrorHandlerAction : IActionDefinition, IActionExecutor
{
    /// <summary>The registry key for the Error Handler node. The graph walk routes unhandled failures here.</summary>
    public const string Key = "control.errorHandler";

    public string TypeKey => Key;
    public string DisplayName => "Error Handler";
    public string Category => "Control Flow";
    public string Description => "Catches any otherwise-unhandled error; wire its output into a recovery flow.";
    public List<PortDefinition> InputPorts { get; } = new();
    public List<PortDefinition> OutputPorts { get; } = new() { new PortDefinition { Name = "out", Label = "On Error Handled" } };
    public List<ConfigField> ConfigFields { get; } = new();
    public bool SupportsRetry => false;

    public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
        => Task.FromResult(ActionResult.Ok("out"));
}
```

- [ ] **Step 2: Register it in `BuiltInActions.Register`**

In `AdbCore/Actions/BuiltIn/BuiltInActions.cs`, find:

```csharp
        Add(new StartAction(), definitions, executors);
        Add(new EndAction(), definitions, executors);
```

Insert after the `EndAction` line:

```csharp
        Add(new ErrorHandlerAction(), definitions, executors);
```

- [ ] **Step 3: Expose the Error Handler on `BotGraph` and keep it out of the entry fallback**

In `AdbCore/Execution/BotGraph.cs`, find:

```csharp
        // A dedicated Start node is always the entry point when present; only when a bot has none do we fall
        // back to the first action with no incoming edge (older bots, or fragments without a Start).
        EntryPoint = bot.Actions.FirstOrDefault(a => a.TypeKey == StartAction.Key)
            ?? bot.Actions.FirstOrDefault(a => !withIncoming.Contains(a.Id));
```

Replace with (adds the Error Handler lookup and excludes it from the no-incoming fallback, since it has no input port and is reached only via the error cascade):

```csharp
        // A dedicated Start node is always the entry point when present; only when a bot has none do we fall
        // back to the first action with no incoming edge (older bots, or fragments without a Start). The Error
        // Handler is never an entry point — it has no input port and is reached only via the error cascade.
        EntryPoint = bot.Actions.FirstOrDefault(a => a.TypeKey == StartAction.Key)
            ?? bot.Actions.FirstOrDefault(a => !withIncoming.Contains(a.Id) && a.TypeKey != ErrorHandlerAction.Key);

        ErrorHandler = bot.Actions.FirstOrDefault(a => a.TypeKey == ErrorHandlerAction.Key);
```

Then add the property next to `EntryPoint` (below its XML-doc):

```csharp
    /// <summary>The bot's Error Handler node when present (first wins on duplicates), else null. Unhandled
    /// failures route here instead of aborting the run (see <see cref="BotExecutor"/>).</summary>
    public BotAction? ErrorHandler { get; }
```

`BotGraph.cs` already has `using AdbCore.Actions.BuiltIn;` (it references `StartAction`), so `ErrorHandlerAction.Key` resolves with no new using.

- [ ] **Step 4: Add the routing loop to `BotExecutor.RunAsync`**

In `AdbCore/Execution/BotExecutor.cs`, find:

```csharp
        var state = new RunState(graph, _executors, _controlFlow, context, options.Log ?? (_ => { }), progress);
        var outcome = await WalkAsync(state, entry, ct);

        return new ExecutionResult
        {
            Success = outcome.Success,
            ErrorMessage = outcome.ErrorMessage,
            FailedActionId = outcome.FailedActionId,
            ActionsExecuted = state.ActionsExecuted,
            FinalVariables = new Dictionary<string, object>(context.Variables),
        };
```

Replace with:

```csharp
        var state = new RunState(graph, _executors, _controlFlow, context, options.Log ?? (_ => { }), progress);
        var outcome = await WalkAsync(state, entry, ct);

        // Global Error Handler: when the walk ends in an unhandled failure and the bot has an Error Handler
        // node, route into it (a fresh walk from that node) rather than aborting. The handler's own flow can
        // wire back to an earlier node to build a reboot/retry loop; if that flow fails again, we re-enter the
        // handler. Iterative here (no stack growth) and cancellable via ct — same shape as an always-on Loop.
        // With no Error Handler this loop never runs, so the failed outcome bubbles exactly as before.
        var errorHandler = graph.ErrorHandler;
        while (!outcome.Success && errorHandler is not null)
        {
            SeedErrorContext(context, graph, outcome);
            state.Log($"⚠ unhandled error → Error Handler: {outcome.ErrorMessage}");
            outcome = await WalkAsync(state, errorHandler, ct);
        }

        return new ExecutionResult
        {
            Success = outcome.Success,
            ErrorMessage = outcome.ErrorMessage,
            FailedActionId = outcome.FailedActionId,
            ActionsExecuted = state.ActionsExecuted,
            FinalVariables = new Dictionary<string, object>(context.Variables),
        };
```

Then add this helper method to the `BotExecutor` class (e.g. just below `RunAsync`):

```csharp
    /// <summary>Publishes an unhandled failure's details as run variables the Error Handler flow can read
    /// via <c>${error.message}</c> etc. before it runs.</summary>
    private static void SeedErrorContext(BotExecutionContext context, BotGraph graph, WalkOutcome outcome)
    {
        var failed = outcome.FailedActionId is Guid id ? graph.Find(id) : null;
        context.Variables["error.message"] = outcome.ErrorMessage ?? string.Empty;
        context.Variables["error.action"] = failed?.Label ?? string.Empty;
        context.Variables["error.actionId"] = outcome.FailedActionId?.ToString() ?? string.Empty;
        context.Variables["error.typeKey"] = failed?.TypeKey ?? string.Empty;
    }
```

Note: `⚠` is ⚠ and `→` is → — using escapes keeps the source ASCII-safe.

- [ ] **Step 5: Write the engine tests**

Create `AdbCore.Tests/Execution/ErrorHandlerExecutionTests.cs`:

```csharp
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

    // Registry seeded with the engine-native marker executors both paths need.
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
        Assert.False(handlerRan); // handled at the node; the global handler is never reached
    }

    [Fact]
    public async Task ErrorHandler_ReentersUntilRecoveryFlowSucceeds()
    {
        // "work" fails twice, succeeds the third time. Handler routes back to "work" -> reboot/retry loop.
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
        Assert.Equal(3, attempts); // failed twice (handler entered twice), succeeded on the third
    }

    [Fact]
    public async Task ParallelHalt_ReachesErrorHandler()
    {
        // A Run Parallel branch fails, Join.someFailed is unwired, strategy HaltAll -> the halt bubbles into
        // the normal walk and reaches the Error Handler (parallel aggregates at the Join first, then hands off).
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
        // Child has no Error Handler and its card has no onFailure -> the child failure bubbles to the parent's
        // Error Handler.
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
        // No Start node; the Error Handler is first in document order with no incoming edge, but a normal leaf
        // must be chosen as the entry point instead.
        var handler = Node(ErrorHandlerAction.Key, out _);
        var leaf = Node("work", out var wId);
        var bot = new Bot { Name = "g2" };
        bot.Actions.AddRange(new[] { handler, leaf });

        var graph = new BotGraph(bot);

        Assert.NotNull(graph.EntryPoint);
        Assert.Equal(wId, graph.EntryPoint!.Id);
    }
}
```

- [ ] **Step 6: Write the serialization round-trip test**

Create `AdbCore.Tests/Serialization/ErrorHandlerSerializationTests.cs`:

```csharp
using AdbCore.Actions.BuiltIn;
using AdbCore.Models;
using AdbCore.Serialization;
using Xunit;

namespace AdbCore.Tests.Serialization;

public class ErrorHandlerSerializationTests
{
    [Fact]
    public void Bot_WithErrorHandlerNode_RoundTrips()
    {
        var handlerId = Guid.NewGuid();
        var bot = new Bot
        {
            Id = Guid.NewGuid(),
            Name = "HasHandler",
            Actions =
            {
                new BotAction { Id = Guid.NewGuid(), TypeKey = "control.start" },
                new BotAction { Id = handlerId, TypeKey = ErrorHandlerAction.Key, Label = "Error Handler" },
            },
        };

        var serializer = new BotSerializer();
        var loaded = serializer.Deserialize(serializer.Serialize(bot));

        var handler = Assert.Single(loaded.Actions, a => a.TypeKey == ErrorHandlerAction.Key);
        Assert.Equal(handlerId, handler.Id);
    }
}
```

- [ ] **Step 7: Run the new tests**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~ErrorHandler"`
Expected: PASS (9 tests total across the two files).

- [ ] **Step 8: Run the full AdbCore suite (no regressions)**

Run: `dotnet test AdbCore.Tests/AdbCore.Tests.csproj`
Expected: PASS — existing `BotExecutorTests`, `ParallelExecutionTests`, `NestedBotExecutorTests`, `LoopExecutionTests`, etc. unaffected (the loop is a no-op when there's no Error Handler).

- [ ] **Step 9: Commit**

```bash
git add AdbCore/Actions/BuiltIn/ErrorHandlerAction.cs AdbCore/Actions/BuiltIn/BuiltInActions.cs AdbCore/Execution/BotGraph.cs AdbCore/Execution/BotExecutor.cs AdbCore.Tests/Execution/ErrorHandlerExecutionTests.cs AdbCore.Tests/Serialization/ErrorHandlerSerializationTests.cs
git commit -m "Add global Error Handler node: route unhandled failures into a recovery flow"
```

---

### Task 2: Docs sync

**Files:**
- Modify: `ADB.wiki/Control-Flow.md` (new "Error Handler" section)
- Modify: `CLAUDE.md` (control-flow node list / execution-flow notes)
- Modify: `README.md` (control-flow features list; keep the goblin voice)

- [ ] **Step 1: Wiki — add an Error Handler section**

In `ADB.wiki/Control-Flow.md`, add a new `## Error Handler` section (near the other control-flow nodes). Cover, grounded in what Task 1 ships:
- `control.errorHandler`, at most one per bot, reached only via the error cascade (no input port), single output port **On Error Handled**.
- The cascade: retry → the node's own `onFailure` → the bot's Error Handler → (bubbling up) the parent Nested Bot card's `onFailure` → the parent's Error Handler.
- Reaching it = handled: the run doesn't abort/bubble; execution continues from its output. Wire the output back to an earlier node to build a reboot→reinit→retry loop (infinite by design; stop with the Stop button).
- Exposes `${error.message}`, `${error.action}`, `${error.actionId}`, `${error.typeKey}` for the recovery flow to read.
- Run Parallel is the exception: a branch failure is aggregated at the Join first (strategy + `someFailed`); only a resulting halt reaches the Error Handler.

Also add `[Error Handler](#error-handler)` to the node nav line at the top of the page.

- [ ] **Step 2: CLAUDE.md — note the node + routing**

Add a concise, precise note where control-flow nodes / execution flow are described: the `control.errorHandler` node and the `BotExecutor.RunAsync` routing loop (unhandled failure → seed `error.*` vars → walk from the Error Handler; absent → unchanged). Keep it plain (engineering reference).

- [ ] **Step 3: README.md — one goblin-voiced bullet**

Add a short bullet to the control-flow feature list describing the Error Handler ("when your bot faceplants, catch it and reboot instead of dying") — accurate to behavior, on-brand voice.

- [ ] **Step 4: Commit (wiki repo, then pointer)**

```bash
cd ADB.wiki && git add Control-Flow.md && git commit -m "Document the Error Handler node" && git push origin HEAD:master
cd .. && git add ADB.wiki CLAUDE.md README.md && git commit -m "Docs: document the global Error Handler node"
```

(If the wiki push is rejected as non-fast-forward, `git fetch origin && git rebase origin/master` in `ADB.wiki` first, then push, then re-`git add ADB.wiki` in the superproject.)

---

## Self-Review

- **Spec coverage (Feature 2 engine):** node `control.errorHandler` (Task 1 Step 1) ✓; registration ✓; `BotGraph.ErrorHandler` + entry-fallback exclusion ✓; `RunAsync` routing loop with re-entry ✓; `error.*` vars ✓; observability marker ✓; cascade precedence (node `onFailure` first) — test ✓; parallel-halt reaches handler — test ✓; nested→parent bubble — test ✓; serialization round-trip ✓; regression (no handler) ✓. UI slice intentionally deferred to a separate PR.
- **Placeholder scan:** none — all steps carry real code/commands. The docs task specifies exact content to write per surface.
- **Type consistency:** `ErrorHandlerAction.Key = "control.errorHandler"`; `graph.ErrorHandler` (BotAction?); `SeedErrorContext(BotExecutionContext, BotGraph, WalkOutcome)`; `WalkOutcome.FailedActionId` (Guid?), `.ErrorMessage` (string?), `.Success`; `context.Variables` (mutable dict of object); `FakeExecutor { TypeKey, Behavior }`; `RunParallelAction.RunParallelTypeKey/BranchesKey/OnBranchFailureKey/BranchPort`, `JoinAction.JoinTypeKey`, `ParallelErrorStrategy.HaltAll`, `NestedBotAction.NestedBotTypeKey`, `BotSerializer.Serialize/Deserialize` — all match existing code.
