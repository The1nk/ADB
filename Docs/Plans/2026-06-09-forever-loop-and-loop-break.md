# Forever Loop & Loop-Break Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `Forever` loop mode (runs until the user stops the run) and a `Loop-Break` node that exits the innermost enclosing loop early.

**Architecture:** `Break` becomes a first-class walk signal: `WalkOutcome.Break()` / `ControlFlowResult.Break()`. A new engine-native `Loop-Break` control-flow node returns the break signal; `BotExecutor.WalkAsync` unwinds the current sub-walk on it; the innermost `LoopControlFlowExecutor` consumes it and exits via `Done`. `Forever` mode is a third option on the existing Loop node, using a `long` iteration counter to avoid `int` overflow on long runs.

**Tech Stack:** C# / .NET 10, xUnit. Engine code in `AdbCore/Execution`, action metadata in `AdbCore/Actions/BuiltIn`, tests in `AdbCore.Tests`.

**Spec:** `Docs/Specs/2026-06-09-forever-loop-and-loop-break-design.md`

---

## File map

- Modify `AdbCore/Execution/WalkOutcome.cs` — add `IsBreak` + `Break()`.
- Modify `AdbCore/Execution/ControlFlowResult.cs` — add `IsBreak` + `Break()`.
- Create `AdbCore/Actions/BuiltIn/LoopBreakAction.cs` — definition (metadata only).
- Create `AdbCore/Execution/ControlFlow/LoopBreakControlFlowExecutor.cs` — returns `ControlFlowResult.Break()`.
- Modify `AdbCore/Execution/BotExecutor.cs` — translate a break `ControlFlowResult` into a returned `WalkOutcome.Break()`.
- Modify `AdbCore/Execution/ControlFlow/LoopControlFlowExecutor.cs` — Forever branch + consume-break in every loop body.
- Modify `AdbCore/Actions/BuiltIn/LoopAction.cs` — `ModeForever` const + add to `Mode` options.
- Modify `AdbCore/Actions/BuiltIn/BuiltInActions.cs` — register the new definition.
- Modify `AdbCore/Execution/ControlFlowExecutorRegistry.cs` — register the new executor in `CreateDefault`.
- Create `AdbCore.Tests/Execution/LoopBreakExecutionTests.cs` — break behaviour.
- Modify `AdbCore.Tests/Execution/LoopExecutionTests.cs` — Forever tests.
- Modify `AdbCore.Tests/Execution/ControlFlowExecutorRegistryTests.cs` — count 2 → 3 + Loop-Break assertion.
- Modify `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs` — defs 45 → 46, add `control.loopBreak` to the engine-native set.

---

## Task 1: Break signal primitives

**Files:**
- Modify: `AdbCore/Execution/WalkOutcome.cs`
- Modify: `AdbCore/Execution/ControlFlowResult.cs`
- Test: `AdbCore.Tests/Execution/BreakSignalTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `AdbCore.Tests/Execution/BreakSignalTests.cs`:
```csharp
using AdbCore.Execution;
using Xunit;

namespace AdbCore.Tests.Execution;

public class BreakSignalTests
{
    [Fact]
    public void WalkOutcome_Break_IsSuccessButFlaggedBreak()
    {
        var outcome = WalkOutcome.Break();
        Assert.True(outcome.Success);   // a break is not a failure
        Assert.True(outcome.IsBreak);
    }

    [Fact]
    public void WalkOutcome_Completed_IsNotBreak()
    {
        Assert.False(WalkOutcome.Completed().IsBreak);
    }

    [Fact]
    public void ControlFlowResult_Break_CarriesBreakOutcome()
    {
        var result = ControlFlowResult.Break();
        Assert.True(result.IsBreak);
        Assert.True(result.Outcome.IsBreak);
        Assert.Null(result.Next);
    }

    [Fact]
    public void ControlFlowResult_ContinueAndHalt_AreNotBreak()
    {
        Assert.False(ControlFlowResult.Continue(null).IsBreak);
        Assert.False(ControlFlowResult.Halt(WalkOutcome.Failed("x", System.Guid.NewGuid())).IsBreak);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~BreakSignalTests"`
Expected: FAIL — `WalkOutcome` has no `IsBreak`/`Break`, `ControlFlowResult` has no `IsBreak`/`Break` (compile errors).

- [ ] **Step 3: Add the break state to `WalkOutcome`**

In `AdbCore/Execution/WalkOutcome.cs`, add the property and factory (Success stays true — a break is not a failure):
```csharp
    public bool Success { get; private init; }
    public bool IsBreak { get; private init; }
    public string? ErrorMessage { get; private init; }
    public Guid? FailedActionId { get; private init; }

    public static WalkOutcome Completed() => new() { Success = true };

    public static WalkOutcome Break() => new() { Success = true, IsBreak = true };

    public static WalkOutcome Failed(string? errorMessage, Guid failedActionId)
        => new() { Success = false, ErrorMessage = errorMessage, FailedActionId = failedActionId };
```

- [ ] **Step 4: Add the break signal to `ControlFlowResult`**

In `AdbCore/Execution/ControlFlowResult.cs`, extend the private constructor with an optional `isBreak` flag (default `false`, so `Continue`/`Halt` are unchanged) and add the property + factory:
```csharp
    private ControlFlowResult(WalkOutcome outcome, BotAction? next, bool isBreak = false)
    {
        Outcome = outcome;
        Next = next;
        IsBreak = isBreak;
    }

    public WalkOutcome Outcome { get; }
    public BotAction? Next { get; }

    /// <summary>True only for <see cref="Break"/>: the walker should unwind to the innermost enclosing loop.</summary>
    public bool IsBreak { get; }

    /// <summary>Success; resume the parent walk at <paramref name="next"/> (null ends the path).</summary>
    public static ControlFlowResult Continue(BotAction? next) => new(WalkOutcome.Completed(), next);

    /// <summary>Halt the walk with the given failure outcome.</summary>
    public static ControlFlowResult Halt(WalkOutcome failure) => new(failure, null);

    /// <summary>Exit the innermost enclosing loop early (Loop-Break).</summary>
    public static ControlFlowResult Break() => new(WalkOutcome.Break(), null, isBreak: true);
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~BreakSignalTests"`
Expected: PASS (4 tests).

- [ ] **Step 6: Commit**

```bash
git add AdbCore/Execution/WalkOutcome.cs AdbCore/Execution/ControlFlowResult.cs AdbCore.Tests/Execution/BreakSignalTests.cs
git commit -m "Add Break walk signal (WalkOutcome.Break / ControlFlowResult.Break)"
```

---

## Task 2: Loop-Break node (definition, executor, walker, registration)

**Files:**
- Create: `AdbCore/Actions/BuiltIn/LoopBreakAction.cs`
- Create: `AdbCore/Execution/ControlFlow/LoopBreakControlFlowExecutor.cs`
- Modify: `AdbCore/Execution/BotExecutor.cs:84-85` (inside the control-flow branch of `WalkAsync`)
- Modify: `AdbCore/Execution/ControlFlow/LoopControlFlowExecutor.cs:49-53` (the bounded `for` loop)
- Modify: `AdbCore/Actions/BuiltIn/BuiltInActions.cs:85`
- Modify: `AdbCore/Execution/ControlFlowExecutorRegistry.cs:30-31`
- Modify: `AdbCore.Tests/Execution/ControlFlowExecutorRegistryTests.cs:44-53`
- Modify: `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs:35,41`
- Test: `AdbCore.Tests/Execution/LoopBreakExecutionTests.cs` (create)

- [ ] **Step 1: Write the failing end-to-end test**

Create `AdbCore.Tests/Execution/LoopBreakExecutionTests.cs`:
```csharp
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
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~LoopBreakExecutionTests"`
Expected: FAIL — `LoopBreakAction` does not exist (compile error).

- [ ] **Step 3: Create the Loop-Break definition**

Create `AdbCore/Actions/BuiltIn/LoopBreakAction.cs`:
```csharp
namespace AdbCore.Actions.BuiltIn;

/// <summary>Exits the innermost enclosing loop early, then the loop follows its Done port. Execution is
/// engine-native (see <c>LoopBreakControlFlowExecutor</c> / <c>BotExecutor.WalkAsync</c>); this type supplies
/// palette and properties-panel metadata only and has no executor. Terminal: one input, no outputs.</summary>
public sealed class LoopBreakAction : IActionDefinition
{
    public const string LoopBreakTypeKey = "control.loopBreak";

    public string TypeKey => LoopBreakTypeKey;
    public string DisplayName => "Loop-Break";   // sorts directly under "Loop" in the Control Flow palette
    public string Category => "Control Flow";
    public string Description => "Exits the innermost enclosing loop early (the loop then follows Done).";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public List<PortDefinition> OutputPorts { get; } = new();   // terminal, like End
    public List<ConfigField> ConfigFields { get; } = new();
    public bool SupportsRetry => false;
}
```

- [ ] **Step 4: Create the Loop-Break control-flow executor**

Create `AdbCore/Execution/ControlFlow/LoopBreakControlFlowExecutor.cs`:
```csharp
using AdbCore.Actions.BuiltIn;

namespace AdbCore.Execution.ControlFlow;

/// <summary>Engine-native Loop-Break: signals the current sub-walk to unwind to the innermost enclosing loop,
/// which consumes the break and resumes at its Done port.</summary>
public sealed class LoopBreakControlFlowExecutor : IControlFlowExecutor
{
    public string TypeKey => LoopBreakAction.LoopBreakTypeKey;

    public Task<ControlFlowResult> ExecuteAsync(ControlFlowContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(ControlFlowResult.Break());
    }
}
```

- [ ] **Step 5: Translate a break into an unwinding `WalkOutcome` in `BotExecutor.WalkAsync`**

In `AdbCore/Execution/BotExecutor.cs`, the control-flow branch currently reads:
```csharp
                var cfResult = await controlFlow.ExecuteAsync(cfContext, ct);
                if (!cfResult.Outcome.Success)
                {
                    return cfResult.Outcome;
                }
                current = cfResult.Next;
                continue;
```
Insert the break check between the failure check and `current = cfResult.Next;`:
```csharp
                var cfResult = await controlFlow.ExecuteAsync(cfContext, ct);
                if (!cfResult.Outcome.Success)
                {
                    return cfResult.Outcome;
                }
                if (cfResult.IsBreak)
                {
                    return WalkOutcome.Break(); // unwind this sub-walk; the innermost loop consumes it
                }
                current = cfResult.Next;
                continue;
```

- [ ] **Step 6: Consume the break in the bounded loop**

In `AdbCore/Execution/ControlFlow/LoopControlFlowExecutor.cs`, the `for` loop body currently reads:
```csharp
            var bodyOutcome = await context.WalkAsync(bodyStart, ct);
            if (!bodyOutcome.Success)
            {
                return ControlFlowResult.Halt(bodyOutcome);
            }
```
Add the break check first (a break exits the loop and falls through to the Done return below):
```csharp
            var bodyOutcome = await context.WalkAsync(bodyStart, ct);
            if (bodyOutcome.IsBreak)
            {
                break; // Loop-Break consumed here (innermost loop) -> follow Done
            }
            if (!bodyOutcome.Success)
            {
                return ControlFlowResult.Halt(bodyOutcome);
            }
```

- [ ] **Step 7: Register the definition and executor**

In `AdbCore/Actions/BuiltIn/BuiltInActions.cs`, after the Loop registration line (`definitions.Register(new LoopAction());`):
```csharp
        // Loop is engine-native: register its definition only (no executor).
        definitions.Register(new LoopAction());

        // Loop-Break is engine-native: definition only (no executor).
        definitions.Register(new LoopBreakAction());
```

In `AdbCore/Execution/ControlFlowExecutorRegistry.cs`, `CreateDefault`:
```csharp
        var registry = new ControlFlowExecutorRegistry();
        registry.Register(new LoopControlFlowExecutor());
        registry.Register(new LoopBreakControlFlowExecutor());
        registry.Register(new ParallelControlFlowExecutor());
        return registry;
```

- [ ] **Step 8: Update the two existing registration tests**

In `AdbCore.Tests/Execution/ControlFlowExecutorRegistryTests.cs`, the `CreateDefault_RegistersLoopAndParallel` test asserts `Count == 2`. Update it to expect 3 and assert Loop-Break is present:
```csharp
        Assert.Equal(3, registry.Count);
        Assert.True(registry.TryGet(AdbCore.Actions.BuiltIn.LoopAction.LoopTypeKey, out var loop));
        Assert.IsType<AdbCore.Execution.ControlFlow.LoopControlFlowExecutor>(loop);
        Assert.True(registry.TryGet(AdbCore.Actions.BuiltIn.LoopBreakAction.LoopBreakTypeKey, out var loopBreak));
        Assert.IsType<AdbCore.Execution.ControlFlow.LoopBreakControlFlowExecutor>(loopBreak);
        Assert.True(registry.TryGet(AdbCore.Actions.BuiltIn.RunParallelAction.RunParallelTypeKey, out var parallel));
        Assert.IsType<AdbCore.Execution.ControlFlow.ParallelControlFlowExecutor>(parallel);
```

In `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs`, add `control.loopBreak` to the engine-native (definition-only) set and bump the definition count from 45 to 46 (executor count stays 42 — Loop-Break has no executor):
```csharp
        // Engine-native nodes: definitions only, no executors.
        foreach (var key in new[] { "control.loop", "control.loopBreak", "control.runParallel", "control.join" })
        {
            Assert.True(defs.TryGet(key, out _));
            Assert.False(execs.TryGet(key, out _));
        }

        Assert.Equal(46, defs.Count);
        Assert.Equal(42, execs.Count);
```

- [ ] **Step 9: Run the affected tests to verify they pass**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~LoopBreakExecutionTests|FullyQualifiedName~ControlFlowExecutorRegistryTests|FullyQualifiedName~BuiltInActionsTests"`
Expected: PASS (all). The Count-loop early-exit, registration count (3), and definition count (46) all pass.

- [ ] **Step 10: Commit**

```bash
git add AdbCore/Actions/BuiltIn/LoopBreakAction.cs AdbCore/Execution/ControlFlow/LoopBreakControlFlowExecutor.cs AdbCore/Execution/BotExecutor.cs AdbCore/Execution/ControlFlow/LoopControlFlowExecutor.cs AdbCore/Actions/BuiltIn/BuiltInActions.cs AdbCore/Execution/ControlFlowExecutorRegistry.cs AdbCore.Tests/Execution/LoopBreakExecutionTests.cs AdbCore.Tests/Execution/ControlFlowExecutorRegistryTests.cs AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs
git commit -m "Add Loop-Break node: exits the innermost enclosing loop early"
```

---

## Task 3: Loop "Forever" mode

**Files:**
- Modify: `AdbCore/Actions/BuiltIn/LoopAction.cs`
- Modify: `AdbCore/Execution/ControlFlow/LoopControlFlowExecutor.cs`
- Test: `AdbCore.Tests/Execution/LoopExecutionTests.cs` (add tests)

- [ ] **Step 1: Write the failing tests**

Append to `AdbCore.Tests/Execution/LoopExecutionTests.cs` (inside the class). They reuse the file's existing `Node` / `Edge` helpers:
```csharp
    [Fact]
    public async Task Loop_Forever_IteratesUntilCancelled()
    {
        var loop = Node(LoopAction.LoopTypeKey, out var loopId);
        loop.Config[LoopAction.ModeKey] = LoopAction.ModeForever;
        var body = Node("body", out var bodyId);

        var bot = new Bot { Name = "loop-forever" };
        bot.Actions.AddRange(new[] { loop, body });
        bot.Connections.Add(Edge(loopId, LoopAction.BodyPort, bodyId));

        using var cts = new CancellationTokenSource();
        var calls = 0;
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor
        {
            TypeKey = "body",
            Behavior = c => { if (++calls >= 4) { cts.Cancel(); } return ActionResult.Ok(string.Empty); },
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, cts.Token));
        Assert.Equal(4, calls); // 4th call cancels; the loop's next ct check throws before a 5th body run
    }

    [Fact]
    public async Task Loop_Forever_NoBody_FailsFast()
    {
        var loop = Node(LoopAction.LoopTypeKey, out var loopId);
        loop.Config[LoopAction.ModeKey] = LoopAction.ModeForever;
        var done = Node("done", out var doneId);

        var bot = new Bot { Name = "loop-forever-empty" };
        bot.Actions.AddRange(new[] { loop, done });
        bot.Connections.Add(Edge(loopId, LoopAction.DonePort, doneId)); // no body edge

        var result = await new BotExecutor(new ActionExecutorRegistry()).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.False(result.Success);
        Assert.Contains("Forever", result.ErrorMessage);
        Assert.Equal(loopId, result.FailedActionId);
    }

    [Fact]
    public async Task Loop_Forever_IndexVariableIsLongAndIncrements()
    {
        var loop = Node(LoopAction.LoopTypeKey, out var loopId);
        loop.Config[LoopAction.ModeKey] = LoopAction.ModeForever;
        loop.Config[LoopAction.IndexVariableKey] = "i";
        var body = Node("body", out var bodyId);

        var bot = new Bot { Name = "loop-forever-index" };
        bot.Actions.AddRange(new[] { loop, body });
        bot.Connections.Add(Edge(loopId, LoopAction.BodyPort, bodyId));

        using var cts = new CancellationTokenSource();
        var seen = new List<object>();
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor
        {
            TypeKey = "body",
            Behavior = c => { seen.Add(c.Context.Variables["i"]); if (seen.Count >= 3) { cts.Cancel(); } return ActionResult.Ok(string.Empty); },
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, cts.Token));
        Assert.Equal(new object[] { 0L, 1L, 2L }, seen); // boxed long values, not int
    }

    [Fact]
    public async Task Loop_Forever_LoopBreakExitsViaDone()
    {
        var loop = Node(LoopAction.LoopTypeKey, out var loopId);
        loop.Config[LoopAction.ModeKey] = LoopAction.ModeForever;
        var body = Node("body", out var bodyId);
        var brk = Node(LoopBreakAction.LoopBreakTypeKey, out var brkId);
        var done = Node("done", out var doneId);

        var bot = new Bot { Name = "loop-forever-break" };
        bot.Actions.AddRange(new[] { loop, body, brk, done });
        bot.Connections.Add(Edge(loopId, LoopAction.BodyPort, bodyId));
        bot.Connections.Add(Edge(bodyId, "out", brkId)); // body always routes to Loop-Break
        bot.Connections.Add(Edge(loopId, LoopAction.DonePort, doneId));

        var bodyCalls = 0;
        var doneReached = false;
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "body", Behavior = c => { bodyCalls++; return ActionResult.Ok("out"); } });
        registry.Register(new FakeExecutor { TypeKey = "done", Behavior = c => { doneReached = true; return ActionResult.Ok(string.Empty); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);
        Assert.Equal(1, bodyCalls); // first iteration breaks
        Assert.True(doneReached);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~LoopExecutionTests.Loop_Forever"`
Expected: FAIL — `LoopAction.ModeForever` does not exist (compile error).

- [ ] **Step 3: Add the `Forever` mode constant and option**

In `AdbCore/Actions/BuiltIn/LoopAction.cs`, add the constant next to the existing mode constants:
```csharp
    public const string ModeCount = "Count";
    public const string ModeForEach = "ForEach";
    public const string ModeForever = "Forever";
```
And append it to the `Mode` field's options:
```csharp
            Key = ModeKey, Label = "Mode", Type = ConfigFieldType.Enum,
            DefaultValue = ModeCount, Options = new() { ModeCount, ModeForEach, ModeForever },
```

- [ ] **Step 4: Add the `Forever` branch to the loop executor**

In `AdbCore/Execution/ControlFlow/LoopControlFlowExecutor.cs`, after the `indexVar` / `itemVar` are read (immediately before the `IReadOnlyList<string?> items;` declaration), insert the Forever branch. `bodyStart` is already computed at the top of the method:
```csharp
        if (string.Equals(mode, LoopAction.ModeForever, StringComparison.OrdinalIgnoreCase))
        {
            if (bodyStart is null)
            {
                return ControlFlowResult.Halt(WalkOutcome.Failed(
                    "Loop in Forever mode requires a Body path (an unwired Forever loop would spin forever doing nothing).",
                    loop.Id));
            }

            for (long iteration = 0; ; iteration++)
            {
                ct.ThrowIfCancellationRequested();
                if (!string.IsNullOrEmpty(indexVar))
                {
                    context.RunContext.Variables[indexVar] = iteration; // long — never overflows in a real run
                }

                var bodyOutcome = await context.WalkAsync(bodyStart, ct);
                if (bodyOutcome.IsBreak) { break; }                       // Loop-Break -> exit via Done
                if (!bodyOutcome.Success) { return ControlFlowResult.Halt(bodyOutcome); }
            }

            return ControlFlowResult.Continue(context.Graph.FindNext(loop.Id, LoopAction.DonePort));
        }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~LoopExecutionTests.Loop_Forever"`
Expected: PASS (4 tests).

- [ ] **Step 6: Commit**

```bash
git add AdbCore/Actions/BuiltIn/LoopAction.cs AdbCore/Execution/ControlFlow/LoopControlFlowExecutor.cs AdbCore.Tests/Execution/LoopExecutionTests.cs
git commit -m "Add Loop Forever mode (long iteration counter, empty-body guard)"
```

---

## Task 4: Edge cases — nesting, no-enclosing-loop, ForEach, parallel boundary

**Files:**
- Test: `AdbCore.Tests/Execution/LoopBreakExecutionTests.cs` (add tests)

No production code is expected to change in this task — these cases fall out of Tasks 1–3. If a test reveals a gap, fix the relevant executor and note it in the commit.

- [ ] **Step 1: Write the failing tests**

Append to `AdbCore.Tests/Execution/LoopBreakExecutionTests.cs` (inside the class). The parallel test also needs these `using`s at the top of the file — confirm they are present and add any missing: `using AdbCore.Actions.BuiltIn;` (already present).
```csharp
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
        rp.Config[RunParallelAction.OnBranchFailureKey] = ParallelErrorStrategy.HaltAll.ToString();
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
        Assert.Contains("converge", result.ErrorMessage);
    }
```

- [ ] **Step 2: Run tests to verify they pass (or surface a real gap)**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~LoopBreakExecutionTests"`
Expected: PASS (all, including the Task 2 test). If `LoopBreak_NestedLoops_BreaksInnerOnly` or `LoopBreak_AfterJoin_BreaksEnclosingLoop` fail, the break is propagating too far or not far enough — revisit the consume-break placement in `LoopControlFlowExecutor` (it must `break` the local loop and return a non-break `Continue`).

- [ ] **Step 3: Commit**

```bash
git add AdbCore.Tests/Execution/LoopBreakExecutionTests.cs
git commit -m "Test Loop-Break edge cases: nesting, ForEach, no-loop, parallel boundary"
```

---

## Task 5: Full suite + final verification

**Files:** none (verification only).

- [ ] **Step 1: Run the entire test suite**

Run: `dotnet test ADB.slnx`
Expected: PASS — no regressions. Pay attention to `BuiltInActionsTests` (counts 46/42) and `ControlFlowExecutorRegistryTests` (count 3).

- [ ] **Step 2: Build the full solution (catches any non-test consumers)**

Run: `dotnet build ADB.slnx`
Expected: Build succeeded, 0 warnings related to these changes.

- [ ] **Step 3: Manual spot-check note for the reviewer**

The palette ordering (Loop-Break directly under Loop) and the new "Forever" Mode dropdown option are visual; confirm them in BotBuilder during review. No automated UI test is added (consistent with the existing palette, which is covered only at the definition/registry level).

---

## Self-review notes

- **Spec coverage:** §1 Forever → Task 3; §2 Loop-Break (signal, definition, executor, walker, consume) → Tasks 1–2; §2.3 edge cases → Task 4; §3 long counter → Task 3 (Step 4 + `Loop_Forever_IndexVariableIsLongAndIncrements`); §5 test surface → Tasks 1–4.
- **Type consistency:** `LoopBreakAction.LoopBreakTypeKey` (`"control.loopBreak"`), `WalkOutcome.IsBreak`/`Break()`, `ControlFlowResult.IsBreak`/`Break()`, `LoopAction.ModeForever` are used identically across all tasks.
- **No placeholders:** every step has complete code and exact run commands.
