# Nested-bot logging + global Error Handler node

**Date:** 2026-07-03
**Status:** Approved (design) — user authorized implementation immediately after spec.
**Topics:** two independent features sharing one spec because they overlap in `BotExecutor` and in the run log.

## Problem

Two gaps surfaced while authoring an always-on WIP bot:

1. **Nested bots run silently in the log.** During an F5 Test Run the editor launches `BotRunner.exe` and renders its JSON-lines stdout in the log panel. `BotRunner` wires the top-level `BotExecutor` with a `Log` sink (`"event":"log"` records — the *Log action*) **and** a progress channel (`InlineProgress` → `"event":"action"` records — the automatic `✓/✗ <label>` trace for every action). But `NestedBotExecutor` runs the child with `RunAsync(nestedBot, childOptions, progress: null, ct)`. That `progress: null` means **no per-action trace is emitted for any action inside a nested bot.** (An explicit *Log action* inside a nested bot already appears, because `Log = context.Log` is forwarded — but with no marker saying it came from a nested bot.)

2. **Error handling is per-node only.** In `BotExecutor.WalkAsync`, a failed action follows its own `onFailure` port if wired; otherwise the walk returns `WalkOutcome.Failed` and the run aborts. There is no bot-level "catch" — no way to say "on any otherwise-unhandled error, route to a recovery flow" (e.g. reboot the device, re-initialize, and continue).

## Goals

1. Nested-bot actions emit their execution trace to the run log, **prefixed** so it's clear they ran inside a nested bot, composing for deep nesting.
2. A **global Error Handler node**: a per-bot last-chance catch that, when present, receives any otherwise-unhandled failure and routes execution into a recovery flow the author wires — enabling infinite reboot→reinit→retry loops.

---

## Feature 1 — Nested-bot logging (engine-only)

### Decision (locked)

Emit nested action lines as **message-style** log records (option #1), not structured `action` records. Rationale: nested action ids aren't on the parent canvas (nested actions live on a separate editing surface), so structured records buy nothing on the canvas; and a nested failure that's *handled inside the nested bot* (its own `onFailure`) is expected flow — painting it red would cry wolf. A genuinely fatal nested failure still turns the **Nested Bot card** red at the parent level (the card reports the child run's overall result).

### Change — `AdbCore/Execution/NestedBotExecutor.cs`

Today the child run is built as:

```csharp
var childOptions = new ExecutionOptions { Log = context.Log, /* … */ };
var result = await new BotExecutor(_executors).RunAsync(nestedBot, childOptions, progress: null, ct);
```

Replace with a **prefixed log sink** plus a **progress→log adapter**:

- **Prefix.** `var prefix = $"[{Name}] "` where `Name` is `nestedBot.Name` (fall back to `"Nested"` when blank). Wrap the incoming sink: `void PrefixedLog(string m) => context.Log?.Invoke(prefix + m);`
- **`childOptions.Log = PrefixedLog`** — so explicit Log-action output *inside* the nested bot is also prefixed (it isn't today). Because each nesting level wraps the sink it was handed, prefixes **compose automatically**: a level-2 action arrives as `context.Log("[Outer] [Inner] ✓ Click")`.
- **Progress adapter.** Feed the child a synchronous `IProgress<ExecutionProgress>` whose `Report` formats each executed action into a line and sends it to `PrefixedLog`:
  - success → `"✓ " + (Label ?? "action")`
  - failure → `"✗ " + (Label ?? "action") + (Error is non-empty ? ": " + Error : "")`

  This mirrors the format in `RunLogEntry.Display`'s `Action` branch so nested lines read consistently with top-level lines. (AdbCore can't reference `BotBuilder.Core`, so the format is duplicated with a comment cross-referencing `RunLogEntry.Display`.)
- Pass the adapter as the `progress` argument to `RunAsync` (replacing `progress: null`).

Use a small synchronous progress type (a `sealed class DelegateProgress<T>(Action<T>) : IProgress<T>` in AdbCore, mirroring `RunnerApp.InlineProgress`) so lines stay in deterministic order — **not** `System.Progress<T>`, which posts asynchronously and would reorder log lines.

### Why message-kind is correct here

`LogPanelView.Append` colors a line red only for `Kind == Action && Success == false`. Nested lines arrive via `context.Log` → `logger.Message` → `"event":"log"` → parsed as `RunLogKind.Message` → rendered as their message text (default color), with the `✓`/`✗` glyph baked into the text. `RunStatusTracker` ignores `Message` records, so nested action ids never pollute canvas node state.

### Tests (`AdbCore.Tests`)

- A nested bot with one ordinary action emits a `"[<Name>] ✓ <Label>"` line to a captured log sink.
- A failing nested action (no `onFailure`) emits `"[<Name>] ✗ <Label>: <error>"`.
- **Deep nesting composes:** outer→inner produces `"[Outer] [Inner] ✓ …"`.
- An explicit **Log action** inside a nested bot is prefixed.
- Ordering is deterministic (synchronous progress).

---

## Feature 2 — Global Error Handler node

### The cascade (locked)

For a failure at any node, resolution order is:

1. **Retry** — `ExecuteWithRetryAsync` exhausts all attempts first (unchanged).
2. **Node's own `onFailure`** — followed if wired (unchanged; this is the "OnError connector").
3. **This bot's Error Handler** — NEW. If none, →
4. **Parent's Nested Bot card `onFailure`** — already works (`NestedBotExecutor` returns `ActionResult.Fail`; parent walk checks the card's `onFailure`). If none, →
5. **Parent's Error Handler** — NEW, i.e. step 3 one level up. Fully recursive.

A handler's **presence** ends the cascade at that level (absence bubbles up one level). Reaching the Error Handler = **handled**: the bot does not bubble to its parent; execution continues from the handler's output.

**Parallel is the exception (unchanged).** A `Run Parallel` branch failure does not follow this cascade past the branch boundary — it unwinds to the **Join**, where `ParallelControlFlowExecutor` aggregates per `On Branch Failure` strategy (`HaltAll`/`WaitThenHalt`/`Continue`) and the Join's `someFailed` port. Only a resulting **halt** re-enters the normal walk as a `Failed` outcome and reaches the bot's Error Handler. `Loop` and `Nested Bot` are the same shape: resolve their own semantics, then a still-unhandled failure bubbles.

### The node — `AdbCore/Actions/BuiltIn/ErrorHandlerAction.cs`

A pure routing/marker node modeled exactly on `StartAction` (`IActionDefinition` + `IActionExecutor`, no-op executor):

- `TypeKey = "control.errorHandler"`, `DisplayName = "Error Handler"`, `Category = "Control Flow"`.
- **No input ports** (like `Start`; it's reached only via the cascade, never a normal edge).
- **One output port:** `{ Name = "out", Label = "On Error Handled" }` — the author wires this back into an earlier node to build the recovery flow.
- No config, `SupportsRetry = false`.
- `ExecuteAsync` returns `ActionResult.Ok("out")`.

Registered in `BuiltInActions.Register` via `Add(new ErrorHandlerAction(), definitions, executors);` (both registries, like `Start`). Because it has a real executor, walking *into* it produces a normal `"event":"action"` record (`✓ Error Handler`) and highlights on the canvas via its own id.

### Graph lookup — `AdbCore/Execution/BotGraph.cs`

Add, alongside `EntryPoint`:

```csharp
public BotAction? ErrorHandler { get; } // = bot.Actions.FirstOrDefault(a => a.TypeKey == ErrorHandlerAction.Key)
```

Engine tolerates duplicates (first wins), matching `EntryPoint`'s tolerance; the editor discourages a second (see UI slice).

### Routing — `AdbCore/Execution/BotExecutor.RunAsync`

Wrap the top-level walk in a loop (the same `RunState` is reused so `error.*` vars and `ActionsExecuted` persist):

```csharp
var outcome = await WalkAsync(state, entry, ct);
while (!outcome.Success && graph.ErrorHandler is not null)
{
    var failed = outcome.FailedActionId is Guid fid ? graph.Find(fid) : null;
    context.Variables["error.message"]  = outcome.ErrorMessage ?? string.Empty;
    context.Variables["error.action"]   = failed?.Label ?? string.Empty;
    context.Variables["error.actionId"] = outcome.FailedActionId?.ToString() ?? string.Empty;
    context.Variables["error.typeKey"]  = failed?.TypeKey ?? string.Empty;
    state.Log($"⚠ unhandled error → Error Handler: {outcome.ErrorMessage}"); // observability marker
    outcome = await WalkAsync(state, graph.ErrorHandler, ct);
}
```

Behavior that falls out of this:

- `WalkOutcome.Break` has `Success == true`, so a stray Loop-Break unwinding to the top is **not** treated as an error (loop won't fire the handler).
- If the recovery flow completes / dead-ends → `Success == true` → loop exits → run succeeds.
- If the recovery flow wires back to an earlier node and the bot later fails again → the walk returns `Failed` → loop re-enters the handler. **Infinite reboot→retry by design**, and safe: the top-level forward walk is an iterative `while` (no stack growth), and it's cancellable via the Stop button (`ct`). Same footgun profile as existing always-on / Loop-Forever bots — a recovery flow whose first hop always fails immediately spins tightly; the author owns that (e.g. add a Delay).
- **No Error Handler → behavior is unchanged** (loop body never runs; the failed outcome bubbles exactly as today). Fully backward compatible.
- **Nested bots inherit this automatically:** `NestedBotExecutor` calls the same `RunAsync`, so a child honors its *own* Error Handler; a child with none returns `Failed` and bubbles to the parent card's `onFailure` (step 4) and then the parent's Error Handler (step 5).

### Error context variables

Exposed as run variables the handler flow can read/branch on via normal `${…}` interpolation: `error.message`, `error.action` (failed node label), `error.actionId`, `error.typeKey`. Stored under those literal keys in `context.Variables`.

### Serialization

Automatic — the Error Handler is just another action with a `typeKey`; `BotSerializer` round-trips it with no schema change (stays `1.0`). Covered by a round-trip test.

### UI slice (BotBuilder + BotBuilder.Core) — *visual, parked for the user*

- **Palette:** appears in *Control Flow* automatically (palette is built from the definition registry). Verify category color / icon read sensibly.
- **Node rendering:** renders with a single output port and no input port (like `Start`, inverted). Confirm the node body and port hit-testing look right with no input port.
- **Single-instance guard:** dropping a second Error Handler is disallowed (surface the existing add-failure affordance / marker). The engine already tolerates duplicates, so this is a should-have guard, not a correctness requirement.
- **Canvas highlight during run:** works via the node's own action id (no work needed — it emits a normal `action` record).

### Tests

`AdbCore.Tests` (engine):

- **Handled:** failing action (no `onFailure`) + Error Handler wired to a recovery flow ending in `End` → run succeeds; `error.message`/`error.action`/`error.actionId`/`error.typeKey` populated.
- **Regression:** same bot with no Error Handler → run fails at the failing action exactly as before.
- **Re-entry:** a fake action that fails N times then succeeds, with the handler wired back into the main flow → run eventually succeeds and the handler was entered N times (bounded — never a true infinite loop in the test).
- **Node `onFailure` still wins:** a failing action *with* `onFailure` wired routes there, not to the Error Handler.
- **Parallel exception:** a branch fails, `someFailed` unwired, `HaltAll` → the resulting halt reaches the Error Handler.
- **Nested cascade:** child with no handler → parent card `onFailure` fires; and child with no handler + no parent card `onFailure` → parent's Error Handler fires.
- `BotGraph.ErrorHandler` returns the node (and first-wins on duplicates).

`AdbCore.Tests` (serialization): Error Handler node round-trips.

`BotBuilder.Core.Tests`: single-instance add guard (if the rule lives in Core).

---

## Delivery & slices

Built via subagent-driven-development. Independent slices → separate PRs:

1. **PR 1 — Nested logging (engine-only).** `NestedBotExecutor` prefix + progress adapter + `DelegateProgress<T>` + tests. Backend-only → **self-merge**.
2. **PR 2 — Error Handler engine.** `ErrorHandlerAction`, `BotGraph.ErrorHandler`, `BotExecutor.RunAsync` loop, `error.*` vars, observability marker, registration, engine + serialization tests. Backend-only → **self-merge**.
3. **PR 3 — Error Handler UI.** Palette presence check, node rendering with no input port, single-instance guard, any Core-level validation + tests. Visual → **park for the user** to verify and merge.

## Docs sync

Per the docs-sync contract, update `CLAUDE.md` (`.bot` control-flow node list, error-handling notes), `README.md` (if it enumerates node types / features — keep the goblin voice), and the ADB.wiki in the same change as the feature PRs.
