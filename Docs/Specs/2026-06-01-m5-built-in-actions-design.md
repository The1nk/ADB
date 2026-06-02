# M5 — Built-in Actions: Design

**Status:** Approved — M5a1 + M5a2 (engine v2 + Branch/Loop/Delay + Run Parallel/Join) implemented
**Date:** 2026-06-01
**Milestone:** M5 — Built-in Actions (`Docs/Design/V1.md` §4.3, §9)

---

## 1. Overview

M5 implements the initial built-in action set across four categories — **Control Flow**, **Screen**, **Input**, and **Data** — and reworks the execution engine to support the non-linear control flow those actions require (branching, looping, and concurrent fan-out/join).

This is the first milestone to introduce real external/OS dependencies (OpenCvSharp, Win32) and concurrency into `AdbCore`. It is also where the M4 Properties Panel work (retry section, FilePath/ImagePath field editors, target dropdown) is finally exercised against actions that actually use it.

### Scoping decisions (settled with the user)

1. **Concurrency engine first.** Build the concurrency-capable engine model up front so every action is written against the final engine — no throwaway sequential-only assumptions.
2. **Loop uses Blueprints-style `body`/`done` ports.** No back-edges; the graph stays a DAG. The engine natively re-walks the Body sub-path per iteration, then follows Done.
3. **Control Flow is engine-native.** The engine recognizes Start/End/Branch/Loop/Delay/Run Parallel/Join directly. Only *leaf* actions (Screen/Input/Data) go through `IActionExecutor`.
4. **OpenCvSharp and Win32 are not optional deps.** They ship always-on (OpenCvSharp bundles native binaries via NuGet; Win32 is built into Windows). The "disabled category" UX in the design covers only Android/Browser/Desktop UI (M7+), not M5.
5. **Screen/Input are window-targeted (HWND-relative).** Screen captures the target window's bitmap; Input coordinates are client-relative to the target window. This requires pulling Window→HWND target resolution forward (it was previously deferred to M7).
6. **Full action set** as specced for each category (no trimmed first cut).
7. **Five reviewable slices** (see §8).

---

## 2. Engine v2 — Structured Graph Walker

### 2.1 Why the current engine is insufficient

`BotExecutor` today is a **single-pointer sequential walker**: it finds one entry point, runs one action, follows the one output port its executor returns, and advances a single `current` pointer until the path dead-ends. Retry and `onFailure` fallback are already built in and will be preserved.

This model cannot express:

- **Branch** — needs to choose between `true`/`false` output paths. *(Mostly expressible today via `OutputPort`, but the condition is engine-native.)*
- **Loop** — needs to run a `body` sub-path repeatedly, then continue from `done`.
- **Run Parallel / Join** — needs to fan out N concurrent sub-paths and await them at a synchronization node.

### 2.2 Core abstraction: the sub-walk

The engine is restructured around walking a **sub-path**: starting from a node along a chosen output port, follow connections node-by-node until the path either **dead-ends** (no outgoing connection on the followed port) or **reaches a synchronization boundary** (a Join node, or the end of a Loop body).

```
WalkResult WalkFrom(BotAction start, string port, WalkScope scope, CancellationToken ct)
```

- A **leaf action** is executed via its `IActionExecutor` (unchanged contract); the walker follows `result.OutputPort`.
- A **control-flow node** is handled natively by the engine, which decides how the walk proceeds (fork, repeat, branch, synchronize).
- `WalkScope` carries the boundary that ends a sub-walk early (e.g. "stop when you reach Join node J" for a parallel branch, or "stop at the end of this Loop body").
- Retry (`RetryPolicy`) and `onFailure` fallback continue to apply to **leaf actions** exactly as today.

The walker is designed **concurrency-ready from the start** (sub-walks are independent and re-entrant), so adding Run Parallel/Join in M5a2 is *additive* — no rework of the M5a1 walker.

### 2.3 Variables and outputs

`BotExecutionContext.Variables` (a `Dictionary<string, object>`) already exists and flows through the whole run. Control-flow and Data actions read/write it (Loop index, Set Variable, Branch condition operands). Leaf-action `ActionResult.Outputs` may be written into variables by convention where a config field names an output variable (e.g. Find Image writes match coordinates).

### 2.4 Cancellation & failure

- The whole run remains async and cancellable (`CancellationToken` threaded everywhere, including `Task.Delay` in Delay and retry).
- Default behavior stays **halt-on-failure** unless an `onFailure` port is wired.
- Parallel branches honor `ParallelErrorStrategy` (see §3.5).

---

## 3. Control Flow nodes (engine-native)

All Control Flow nodes are registered as `IActionDefinition` (so they appear in the palette and Properties Panel) but their *execution* is handled by the engine, not an `IActionExecutor`. The engine dispatches on `TypeKey`.

### 3.1 Start / End

- **Start** (`control.start`) — already exists; entry point, single `out`.
- **End** (`control.end`) — already exists; terminates the run successfully.

### 3.2 Delay (`control.delay`)

- Config: `durationMs` (Number).
- Behavior: `await Task.Delay(durationMs, ct)`, then follow `out`.
- Ports: `in` → `out`.

### 3.3 Branch (`control.branch`)

- Config: a simple condition expressed over a variable. First cut:
  - `variable` (String) — variable name to test.
  - `operator` (Enum) — `Equals`, `NotEquals`, `GreaterThan`, `LessThan`, `IsTrue`, `IsFalse`, `IsEmpty`, `IsNotEmpty`.
  - `value` (String) — comparison operand (unused for unary operators).
- Ports: `in` → `true`, `false`.
- Behavior: evaluate the condition against `Context.Variables`; follow `true` or `false`. A missing/null variable is treated per operator semantics (e.g. `IsEmpty` → true).

### 3.4 Loop (`control.loop`)

- Config:
  - `mode` (Enum) — `Count` or `ForEach`.
  - `count` (Number) — iteration count when `mode = Count`.
  - `collectionVariable` (String) — variable holding the items when `mode = ForEach` (items are the variable's enumerable/string-split value; exact source format defined in the M5a1 plan).
  - `indexVariable` (String, optional) — variable name to receive the 0-based index each iteration.
  - `itemVariable` (String, optional) — variable name to receive the current item (ForEach only).
- Ports: `in` → `body`, `done`.
- Behavior: for each iteration, set the index/item variables, then **walk the `body` sub-path to completion** (until it dead-ends). After the configured iterations, follow `done`.
- Body failure: if the body sub-walk fails and is not handled by an `onFailure` inside the body, the Loop fails (halting the run unless the Loop itself has a wired `onFailure`). *(Exact break/continue semantics — e.g. a future Break action — are out of scope for M5.)*
- Nesting: loops may contain branches and other loops. Parallel-inside-loop and loop-inside-parallel are supported by the sub-walk model; the M5a2 plan will include explicit nesting tests.

### 3.5 Run Parallel (`control.runParallel`) + Join (`control.join`)

- **Run Parallel** ports: `in` → N user-configurable named branch ports (`branch1`, `branch2`, …). Config:
  - `branches` (Number) — how many branch output ports (drives the port list).
  - `onBranchFailure` (Enum) — `ParallelErrorStrategy`: `HaltAll`, `WaitThenHalt`, `Continue`.
- Behavior: fan out each wired branch port as an independent concurrent sub-walk (`Task`). Each branch sub-walk is scoped to **stop when it reaches the Join node**. Each branch action may carry its own `TargetId` (the multi-client model).
- **Join** ports: `in` (reached by all branches) → `allSucceeded`, `someFailed`. Behavior: await all branch sub-walks per the error strategy:
  - `HaltAll` — on first branch failure, cancel the others immediately, then proceed to Join.
  - `WaitThenHalt` — let in-flight branches finish, then halt.
  - `Continue` — failures become warnings; always proceed.
  - Join follows `allSucceeded` if every branch succeeded, else `someFailed`.
- **Pairing/validation:** a Run Parallel's branches must converge on exactly one Join. Graph validation (entry-point/Join-reachability checks) is part of M5a2; malformed graphs fail fast with a clear error.

> Note: with window-targeted Input via `SendInput` (foreground-bound), true OS-level concurrency at the input layer is constrained; concurrency is real at the engine/Screen-capture layer. This is acceptable for M5 and revisited if/when background input (PostMessage) proves reliable per-target.

---

## 4. Leaf action categories

Leaf actions implement `IActionDefinition` + `IActionExecutor` (the existing one-class pattern, like `LogAction`) and are registered via `BuiltInActions.Register`.

### 4.1 Data (`data.*`) — engine-native data, no external deps

| Action | TypeKey | Config | Notes |
|---|---|---|---|
| Set Variable | `data.setVariable` | `name` (String), `value` (String) | Writes `Context.Variables[name] = value`. |
| Log | `data.log` | `message` (String) | **Already implemented.** |
| Comment | `data.comment` | `text` (Multiline) | No-op at runtime; documentation node. Single `in`→`out` (pass-through). |

### 4.2 Input (`input.*`) — Win32, window-targeted

A P/Invoke layer (`SendInput`, `PostMessage`/`SetCursorPos` as appropriate, virtual-key mapping) is introduced here. All coordinates are **client-relative to the target window's HWND**. All Input actions `SupportRetry = false` (deterministic) unless noted; ports are `in`→`onSuccess`/`onFailure`.

| Action | TypeKey | Config |
|---|---|---|
| Click | `input.click` | `x`, `y` (Number, client-relative) |
| Right Click | `input.rightClick` | `x`, `y` |
| Double Click | `input.doubleClick` | `x`, `y` |
| Type Text | `input.typeText` | `text` (String) |
| Key Press | `input.keyPress` | `key` (Enum/String — virtual key or combo) |
| Mouse Move | `input.mouseMove` | `x`, `y` |

Resolving the target HWND and converting client→screen coordinates is shared infrastructure (see §5).

### 4.3 Screen (`screen.*`) — OpenCvSharp, window-targeted

Screen actions capture the target window via `PrintWindow`/BitBlt into a bitmap, then run OpenCvSharp template matching. These are the primary consumers of `RetryPolicy` and the M4 ImagePath field + `.meta.json` confidence sidecar.

| Action | TypeKey | Config | Ports | Retry |
|---|---|---|---|---|
| Find Image | `screen.findImage` | `templatePath` (ImagePath), `confidence` (Number 0–1), output vars for match X/Y | `in`→`onSuccess`/`onFailure` | yes |
| Wait for Image | `screen.waitForImage` | `templatePath`, `confidence`, `timeoutMs`, `pollIntervalMs` | `in`→`onSuccess`/`onFailure` | yes |
| Screenshot | `screen.screenshot` | `outputPath` (FilePath) | `in`→`out` | no |
| Assert Image Absent | `screen.assertImageAbsent` | `templatePath`, `confidence` | `in`→`onSuccess`/`onFailure` | yes |

- **Confidence sidecar:** when an ImagePath has a `<image>.png.meta.json` sidecar, its `confidence` pre-fills the field (read-side already lands in M4b's editor; Screen actions honor the configured value at runtime).
- The live OpenCV "test match" UI and capture-time sidecar *writing* belong to BotCapture (M6) — out of scope here.

---

## 5. Target resolution (Window → HWND)

Screen/Input require a live HWND, but `ResolvedTarget.Handle` is currently null (`ResolvedTarget.cs` defers handle-opening to a later milestone). M5 pulls **Window-target resolution forward**:

- A Window selector (e.g. `process:BlueStacks`, `title:...`, `hwnd:...`) resolves to an HWND stored in `ResolvedTarget.Handle`.
- Resolution happens at run start (in `BotRunner`'s target resolver and/or a shared `AdbCore` resolver — placement decided in the M5c plan), keyed by `BotTarget.Id`, exactly as `ExecutionOptions.ResolvedTargets` already expects.
- Android/Browser handle resolution remains deferred to M7.

Leaf executors read their HWND via `Context.Targets[action.TargetId ?? defaultTargetId].Handle`.

---

## 6. Architecture & file layout

Following the existing structure:

- `AdbCore/Execution/` — engine v2 (`BotExecutor` rework + new walker types: `WalkScope`, `WalkResult`, parallel coordination). Engine-native control-flow handling dispatches on `TypeKey`.
- `AdbCore/Actions/BuiltIn/` — control-flow definitions (Branch, Loop, Delay, RunParallel, Join definitions for palette/panel metadata) and leaf executors (Data, Input, Screen).
- `AdbCore/Input/` (new) — Win32 P/Invoke wrapper, isolated and unit-testable behind an interface where practical.
- `AdbCore/Screen/` (new) — window capture + OpenCvSharp template matching, behind an interface so the engine/tests can fake it.
- `ParallelErrorStrategy` enum lives in `AdbCore/Models/` (referenced by serialization).

**Isolation principle:** Win32 and OpenCvSharp access sit behind narrow interfaces (`IWindowCapture`, `IInputSender`, `ITemplateMatcher` or similar) so the engine and the bulk of action logic remain headlessly testable; only thin adapters touch the OS/native libs.

---

## 7. Testing strategy

- **Engine (M5a1/M5a2):** fully TDD with fake leaf executors. Cover sub-walk dead-ending, Branch true/false, Loop count/for-each + index/item vars + nested loops, Delay cancellation, retry/`onFailure` preservation, Run Parallel fan-out, Join with each `ParallelErrorStrategy`, branch cancellation, and malformed-graph validation.
- **Data:** straightforward unit tests (variable writes, Comment pass-through).
- **Input/Screen:** test logic behind the interfaces with fakes (coordinate math, condition evaluation, confidence thresholds, retry interplay); the thin OS/native adapters are verified by the user via manual run (consistent with the WPF-verification pattern). Each OS-touching slice ends with a **Manual Verification Checklist**.
- Gate per slice: `dotnet build ADB.slnx` (0 warnings) + `dotnet test` green.

---

## 8. Slicing plan (5 reviewable PRs)

Each slice = its own worktree, plan, subagent-driven implementation, and PR the user reviews/merges (per the established rhythm).

| Slice | Scope | External deps |
|---|---|---|
| **M5a1** | Engine v2 structured walker (concurrency-ready) + Branch, Loop, Delay. Start/End preserved. Full TDD with fake leaves. **This design doc lands with this PR.** | none |
| **M5a2** | Run Parallel + Join (additive concurrency: fan-out sub-walks, Join sync, `ParallelErrorStrategy`, branch cancellation, graph validation/Join-pairing). | none |
| **M5b** | Data actions: Set Variable, Comment (Log exists). | none |
| **M5c** | Input (Win32): Click/Right/Double Click, Type Text, Key Press, Mouse Move + Window→HWND resolution + client-coordinate infra. | Win32 |
| **M5d** | Screen (OpenCvSharp): Find Image, Wait for Image, Screenshot, Assert Image Absent + window capture; exercises retry + ImagePath + confidence sidecar. | OpenCvSharp |

Order is strict (a1 → a2 → b → c → d); a1 establishes the engine the rest build on.

---

## 9. Out of scope / deferred

- **Android, Browser, Desktop UI, Web/API, Files/System, Scripting** action categories (M7+ and beyond).
- **Disabled-category UX** for optional deps (M9 / when Android/Browser arrive).
- **BotCapture** (M6): live OpenCV test-match UI, region capture, and *writing* the `.meta.json` confidence sidecar. M5 only *reads* an existing sidecar.
- **Break/Continue** actions inside loops, and arbitrary expression languages for Branch (M5 ships a fixed operator set).
- **Background per-target input** via PostMessage if `SendInput` proves insufficient — revisited post-M5 if needed.
- **Builder integration** of test-run/log-tailing (M8).
