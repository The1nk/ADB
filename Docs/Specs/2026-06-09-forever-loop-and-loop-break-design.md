# Forever Loop & Loop-Break — Design

**Status:** Approved
**Context:** There is no way to build an "always-on" bot — one that runs *until the user stops it*. The `Loop` node supports only bounded iteration (`Count` / `ForEach`), and there is no GOTO/Jump. The only run terminators are graph dead-ends, `End` (which merely ends a path — inside a loop body it does **not** break the loop, it just lets the loop proceed to its next iteration), and external cancellation. This adds (1) a `Forever` loop mode that repeats until cancelled, and (2) a `Loop-Break` node that exits the enclosing loop early from within its body.

This is a graph-level / engine feature only. It deliberately does **not** expose actions to Lua (a separate, larger effort considered and set aside); a Forever loop wrapping the existing visual actions covers the always-on use case with no scripting.

---

## 1. Loop "Forever" mode

`LoopAction` already exposes a `Mode` enum config field with options `Count` and `ForEach`. Add a third option, `Forever`.

`LoopAction` (metadata only):
- Add `public const string ModeForever = "Forever";`
- Append `ModeForever` to the `Mode` field's `Options` list (order: `Count`, `ForEach`, `Forever`).
- `Count` / `Collection Variable` / `Item Variable` fields stay; they are simply ignored in `Forever` mode, exactly as `Count` is ignored in `ForEach` mode today (no conditional-visibility machinery exists or is added).

`LoopControlFlowExecutor.ExecuteAsync` — add a `Forever` branch alongside the existing `Count`/`ForEach` item-list construction. Because `Forever` has no finite item list, it gets its own loop rather than reusing the `for (i < items.Count)` path:

```csharp
if (string.Equals(mode, LoopAction.ModeForever, StringComparison.OrdinalIgnoreCase))
{
    var bodyStart = context.Graph.FindNext(loop.Id, LoopAction.BodyPort);
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
            context.RunContext.Variables[indexVar] = iteration; // long — see §3
        }

        var bodyOutcome = await context.WalkAsync(bodyStart, ct);
        if (bodyOutcome.IsBreak) { break; }                 // §2 — exit via Done
        if (!bodyOutcome.Success) { return ControlFlowResult.Halt(bodyOutcome); }
    }

    return ControlFlowResult.Continue(context.Graph.FindNext(loop.Id, LoopAction.DonePort));
}
```

Behaviour:
- **Repeats until stopped.** The Stop button (BotBuilder test-run) and BotRunner Ctrl-C trip `ct`, so `ct.ThrowIfCancellationRequested()` raises `OperationCanceledException`, which propagates out as the existing clean-cancel path. This is the "until I stop you" semantic.
- **Stack-safe.** Each iteration awaits a fresh body sub-walk that fully unwinds before the next begins — same shape as the existing bounded loop; no stack growth across iterations.
- **Empty-body guard (Forever-specific).** A `Forever` loop with no wired `Body` would be a tight do-nothing CPU spin, so it fails fast with a clear message. `Count`/`ForEach` with no body remain harmless no-ops and are unchanged.
- **Done port.** Never reached in `Forever` mode except via a `Loop-Break` (§2). Wiring after `Done` is allowed; it simply only runs once the loop is broken.

## 2. Loop-Break node

A new Control-Flow node that exits the innermost enclosing loop early.

### 2.1 Definition

New `LoopBreakAction : IActionDefinition` (metadata only; execution is engine-native, like `Loop`):
- `TypeKey => "control.loopBreak"`
- `DisplayName => "Loop-Break"` — sorts directly under `"Loop"` (palette orders by Category then `DisplayName`; `"Loop"` < `"Loop-Break"`).
- `Category => "Control Flow"`
- One input port `in`; **no output ports** (terminal, like `End`).
- No config fields. `SupportsRetry => false` (required by `IActionDefinition`). It implements `IActionDefinition` only — execution is engine-native via its control-flow executor, not an `IActionExecutor` (mirrors `LoopAction`).
- Register in `BuiltInActions` (definition list) and in `ControlFlowExecutorRegistry.CreateDefault()`.

### 2.2 Mechanism — explicit `Break` walk outcome

`Break` becomes a first-class walk signal rather than a hidden context flag, consistent with how `WalkOutcome` already carries Completed/Failed.

`WalkOutcome` — add a break state (Success stays `true`; a break is not a failure):
```csharp
public bool IsBreak { get; private init; }
public static WalkOutcome Break() => new() { Success = true, IsBreak = true };
```

`ControlFlowResult` — add a break signal the walker can translate into a returned `WalkOutcome.Break()`. The existing private constructor gains an optional `isBreak` parameter (default `false`, so `Continue`/`Halt` are unchanged):
```csharp
private ControlFlowResult(WalkOutcome outcome, BotAction? next, bool isBreak = false)
{
    Outcome = outcome; Next = next; IsBreak = isBreak;
}
public bool IsBreak { get; }                 // false for Continue/Halt
public static ControlFlowResult Break() => new(WalkOutcome.Break(), null, isBreak: true);
```

`LoopBreakControlFlowExecutor : IControlFlowExecutor` — returns `ControlFlowResult.Break()`.

`BotExecutor.WalkAsync` — in the control-flow branch, after `controlFlow.ExecuteAsync`, when the result signals break, stop the current sub-walk and return the break upward:
```csharp
var cfResult = await controlFlow.ExecuteAsync(cfContext, ct);
if (!cfResult.Outcome.Success) { return cfResult.Outcome; }   // halt (existing)
if (cfResult.IsBreak) { return WalkOutcome.Break(); }         // NEW — unwind this sub-walk
current = cfResult.Next;                                       // continue (existing)
```

`LoopControlFlowExecutor` — every mode's body loop consumes the break (see §1 for Forever; the bounded `for` loop gets the same `if (bodyOutcome.IsBreak) break;` check). The **innermost** loop is the first caller to receive the break outcome from its body sub-walk, so it consumes it and resumes normally out its `Done` port — giving conventional innermost-`break` semantics for nested loops automatically, via the call stack. The consuming loop returns `ControlFlowResult.Continue(done)` (a normal, non-break outcome), so the break never reaches an outer loop.

### 2.3 Applicability & edge cases

- **All loop modes.** Loop-Break exits `Count`, `ForEach`, and `Forever` loops uniformly — the consume-break check lives in the shared loop executor.
- **No enclosing loop (lenient).** If a `Break` reaches the top-level `RunAsync` walk (no loop ever consumed it), it is treated as normal completion — the path just ends, like `End`. `RunAsync` already reports on `outcome.Success`, which is `true` for a break, so no special handling is required there; the run ends successfully.
- **Inside a Parallel branch (branch-only).** `ParallelControlFlowExecutor` runs each branch as a sub-walk; a branch that returns `WalkOutcome.Break()` has `Success == true`, so it is aggregated as a completed branch — the break ends *that branch* (like `End`) and does **not** cross the parallel boundary to break an outer loop. This keeps concurrent semantics unsurprising and requires no change to the Parallel executor. Documented as intended behaviour.

## 3. Index variable overflow (Forever)

The bounded loop writes its index as `int` (`i`). `Forever` can run for days-to-weeks of continuous iteration, and the project compiles with C#'s default **unchecked** arithmetic (no `CheckForOverflowUnderflow` anywhere), so an `int` counter would not throw at `int.MaxValue` (~2.15e9) — it would silently **wrap to `int.MinValue`**, surfacing a negative iteration count to `${index}` interpolation, the Lua `vars` bridge, and `MathAction`. Silent corruption, not a crash.

Fix: **the Forever iteration counter is a `long`** (`Int64`, max ~9.22e18 — unreachable in any real run, even at 1e6 iterations/sec ≈ 290,000 years). No saturation/guard logic needed. `Count`/`ForEach` stay `int` (bounded by collection size / count). The value boxes into `Variables` as `object` either way, so downstream consumers are unaffected.

## 4. Out of scope

- Exposing built-in actions to Lua (the broader ask; deferred).
- GOTO/Jump, labeled break, and `continue` (YAGNI for the always-on use case).
- A conditional `While` loop mode (no expression-evaluation model exists; not required for "until I stop you").
- Conditional config-field visibility in the properties panel (consistent with current Loop behaviour).

## 5. Test surface

`AdbCore.Tests` (engine):
- Forever iterates repeatedly and halts cleanly on cancellation (`OperationCanceledException`); a bounded run via cancellation after N iterations is observable.
- Forever empty-body guard fails fast with the clear message.
- Index variable increments across Forever iterations and is stored as `long`.
- Loop-Break exits a `Count` loop early (remaining iterations skipped), then continues out `Done`.
- Loop-Break exits a `ForEach` loop early.
- Loop-Break exits a `Forever` loop (the primary always-on stop-from-within path).
- Nested loops: a Loop-Break in the inner body breaks only the inner loop; the outer loop continues.
- Loop-Break with no enclosing loop ends the path and the run completes successfully.
- Loop-Break inside a Parallel branch ends that branch only; the enclosing loop is not broken (documented behaviour).

Registry/metadata:
- `LoopBreakAction` is discoverable via the registry and resolves to its control-flow executor.
- Palette ordering places `Loop-Break` immediately after `Loop` in Control Flow.
