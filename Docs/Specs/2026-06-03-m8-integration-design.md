# M8 — Integration Design

**Status:** Approved
**Date:** 2026-06-03
**Milestone:** M8 — Integration (per `Docs/Design/V1.md` §8)

---

## 1. Overview

M8 connects the three executables into one usable loop: from inside **BotBuilder** the user can run the
current bot through **BotRunner** (with a target-picker dialog and the equivalent CLI command), watch the
run's log stream, see per-action progress/errors highlighted on the canvas, and capture template images
for image-path fields by launching **BotCapture** in its integrated `--output` mode.

Nothing new is needed in the engine: the runner already emits a **JSON-lines** log protocol (`run-start`,
per-`action` with `actionId`/`success`/`error`, `run-end`) to stdout, and `ExecutionProgress` already
carries `ActionId`/`FailedActionId`. M8 is glue in the Builder layer plus a small amount of reused infra.

Delivered in **three independently-reviewable slices**:

| Slice | Scope |
|---|---|
| **M8a** | Run menu → Test Run: target-picker dialog (candidates + CLI-command preview), spawn BotRunner on a temp `.bot`, tail the JSON-lines log in a new bottom panel + Stop |
| **M8b** | Canvas run-status: map the log stream's per-action events to node highlighting (green succeeded / red failed) + a run status indicator |
| **M8c** | BotCapture "Capture" button on image fields: launch `--output`, reload the field, pre-fill confidence from the `.meta.json` sidecar |

### Tracked deferred item (post-M7)
The M8 target picker offers **live** candidate discovery only for **Window** targets (via the existing
`IWindowEnumerator`). **Android** (`adb devices`) and **Browser** (Playwright contexts) get a **manual
selector text box** in M8. Once **M7** lands SharpAdbClient + Playwright, return and give those target
types the same live-dropdown UX (this is a deliberate placeholder, not the final design).

---

## 2. Architecture — where things live

All decision logic lives in **`BotBuilder.Core`** (WPF-free, unit-tested). The WPF shell holds only
process spawning, the dialog, the log panel, and canvas rendering (verified visually).

**`BotBuilder.Core` (new, tested):**
- `Integration/RunCommandBuilder.cs` — builds the runner argument list/string from a bot path + target
  selectors (handles quoting and the `Name=selector` form).
- `Integration/RunnerLogParser.cs` — parses one JSON-lines log record into a display model
  (`RunLogEntry`); skips/marks malformed lines without throwing.
- `Integration/RunStatusTracker.cs` — folds parsed log events into a node-id → `NodeRunState` map and an
  overall run status (used by M8b).
- `Integration/ExeLocator.cs` — locates a sibling executable (`BotRunner.exe`, `BotCapture.exe`) from a
  set of candidate paths relative to the app base directory; returns null if none exist.
- `Integration/ConfidenceSidecarReader.cs` — reads `<image>.png.meta.json` → confidence (tolerant;
  returns null when absent/corrupt). (Mirrors BotCapture's writer; lives here so the Builder needn't
  reference BotCapture.)

**`BotBuilder` (WPF shell):** Run menu items + handlers, `TargetPickerDialog`, the bottom `LogPanel`,
`RunSession` (process spawn + async stdout pump + cancellation), the image-field Capture button, and the
node-highlight binding.

**Reused:** the runner's JSON-lines protocol + exit codes; `IWindowEnumerator`/`Win32WindowEnumerator`;
`ConfigFieldType.ImagePath` + `BrowseField_Click`; `ExecutionProgress.ActionId`/`FailedActionId`;
BotCapture's integrated `--output` mode + `.meta.json` sidecar.

---

## 3. Slice M8a — Run menu + Test Run + target picker + log panel

### Flow
1. **Run → Test Run.** The current editor state is serialized to a **temp `.bot`** (e.g.
   `%TEMP%\adb-testrun\<botname>.bot`) so a run never depends on a saved/clean file.
2. A **target-picker dialog** lists the bot's declared `BotTarget`s. Per target:
   - **Window** → an editable **selector** field plus a **live dropdown** of running windows (process +
     title) from `IWindowEnumerator`; choosing a candidate fills the selector, defaulting to
     `process:<ProcessName>` (reusable in the CLI command), editable to `title:`/`hwnd:`.
   - **Android / Browser** → a **manual selector** text box with a format hint (`serial:emulator-5554`,
     `url:https://…`). *(Live discovery deferred to post-M7 — see §1.)*
3. The dialog shows the **equivalent CLI command** live and updates as selectors change:
   `BotRunner.exe --bot "…" --target "Name=selector" …`, with a **Copy** button (V1 §6.2 — teach the CLI).
4. **Run** locates `BotRunner.exe` via `ExeLocator` and spawns it with stdout redirected. A new
   collapsible **bottom log panel** opens and tails the JSON-lines stream as friendly entries
   (`▶ run started`, `✓ <label>`, `✗ <label>: <error>`, `■ run succeeded/failed`). A **Stop** button kills
   the process (cancellation). The process exit code drives the final status line.
5. **Cancel** aborts without running.

### Components
- `BotBuilder.Core/Integration/RunCommandBuilder.cs`, `RunnerLogParser.cs` (`RunLogEntry` model),
  `ExeLocator.cs`.
- `BotBuilder/TargetPickerDialog.xaml(.cs)` — the dialog + live window dropdowns + CLI preview.
- `BotBuilder/RunSession.cs` — wraps the `Process`, async stdout reader (raises parsed entries on the UI
  thread), and `Stop()`.
- `BotBuilder/LogPanel.xaml(.cs)` + a bottom row in `MainWindow`.
- `MainWindow` Run menu + `TestRun_Click`; temp-bot serialization via the existing serializer.

### Tests (BotBuilder.Core.Tests)
- `RunCommandBuilder`: single/multi target arg construction; selector with spaces is quoted; empty
  selector handling.
- `RunnerLogParser`: each event type maps to the right display model; a malformed/non-JSON line yields a
  skip/error marker rather than throwing.
- `ExeLocator`: returns the first existing candidate; null when none exist.

---

## 4. Slice M8b — Canvas run-status

### Flow
As `action` events stream from the runner, a `RunStatusTracker` maps each `actionId` to its node and sets
a `NodeRunState`: nodes turn **green as they succeed** (near-real-time, since the runner emits each event
right after the action executes); on failure the node (`FailedActionId`) turns **red** and stays. A status
indicator shows **Running… / Succeeded / Failed at `<label>`**. Highlights **reset** when the next run
starts or the graph is edited.

> *Design note:* the runner reports per-action events **after** each action executes, so this is
> green-as-completed rather than a "currently-running" pulse. A true running pulse would require a new
> engine `action-start` progress event; that's intentionally **out of M8** to keep this milestone in the
> Builder/Runner layer.

### Components
- `BotBuilder.Core/Integration/RunStatusTracker.cs` (+ `NodeRunState` enum: `None`/`Succeeded`/`Failed`).
- `NodeViewModel` gains a `RunState` (observable) → a border-colour binding on the node card.
- `MainWindow`/`RunSession` feed parsed events into the tracker and apply states to node VMs; a run-status
  text element in the status bar.

### Tests (BotBuilder.Core.Tests)
- `RunStatusTracker`: a sequence of success events marks those nodes `Succeeded`; a failure event marks
  that node `Failed`; `run-start`/reset clears all states; unknown `actionId` is ignored.

---

## 5. Slice M8c — BotCapture "Capture" button on image fields

### Flow
Image-path fields (`ConfigFieldType.ImagePath`) get a **Capture** button beside **Browse**. It:
1. Determines an output path — the field's current value if set, else a **Save dialog** to pick where the
   PNG goes.
2. Locates `BotCapture.exe` (`ExeLocator`) and launches it with `--output "<path>"` (the M6c integrated
   mode).
3. Watches for process exit: on **exit 0** (saved), sets the field to that path and reads the
   `<path>.meta.json` sidecar via `ConfidenceSidecarReader` to **pre-fill the action's confidence** field
   (V1 §7.3); on a non-zero exit (cancelled), leaves the field unchanged.

The sidecar → confidence pre-fill is also applied on the existing **Browse** path (selecting an image with
a sidecar fills confidence).

### Components
- `BotBuilder.Core/Integration/ConfidenceSidecarReader.cs`.
- `BotBuilder` image-field template gains a Capture button + `CaptureField_Click`; a small
  `CaptureSession` (spawn + exit watch) mirroring `RunSession`.
- Confidence pre-fill wired into both Capture and Browse, locating the sibling confidence
  `ConfigFieldViewModel` for the selected action.

### Tests (BotBuilder.Core.Tests)
- `ConfidenceSidecarReader`: reads a written sidecar; returns null for missing/corrupt.
- The confidence-prefill helper sets the matching field's value from a sidecar value (and leaves it when
  none).

---

## 6. Cross-Cutting Concerns

**Missing executable (end-user-facing).** If `ExeLocator` can't find `BotRunner.exe`/`BotCapture.exe`, the
user sees an **end-user-appropriate** message — *"BotRunner couldn't be found. Try reinstalling ADB, and
check whether your antivirus quarantined it."* — never a developer "build the solution" message. The
actual candidate paths probed are written to the log/diagnostics quietly for our debugging.

**Process lifetime.** `RunSession`/`CaptureSession` own their `Process`; Stop/cancel kills the runner;
the Builder never blocks the UI thread (stdout is pumped asynchronously and marshalled to the UI thread).
Closing the Builder kills any child run.

**Error handling.** A runner non-zero exit or crash surfaces in the log panel + status line (not a crash).
Malformed log lines are skipped. A capture cancel leaves fields untouched. Sidecar reads never throw.

**Testing strategy.** All decision logic (command building, log parsing, run-status transitions, exe
location, sidecar reading) lives in `BotBuilder.Core` and is unit-tested with fakes/temp files. Process
spawning, the dialog, the log panel, and canvas highlighting are verified by the user visually, per the
project's established rhythm.

**Reused, not duplicated.** The runner's JSON-lines protocol and exit codes; `IWindowEnumerator`;
`ConfigFieldType.ImagePath`; `ExecutionProgress`; BotCapture's `--output` mode and sidecar format.
