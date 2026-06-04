# M10 — Android Visual + Coordinate Picker — Design

**Status:** Approved
**Date:** 2026-06-04
**Milestone:** M10 (first post-V1 milestone, priority order: M10 → M11 OCR → M12 Lua → M13 Web&API → M14 Files&System → M9 Polish)

---

## 1. Overview

M10 adds two loosely-coupled capabilities:

1. **Android image-matching actions** — `android.findImage`, `android.waitForImage`, `android.assertImageAbsent`: the Android analogs of the existing Screen template-matching actions, so a bot can locate a UI element on a phone/emulator and then act on it (e.g. `Tap ${matchRandX} ${matchRandY}`).
2. **Coordinate-picker helper** — an in-process BotBuilder dialog that lets the author click a captured screenshot of a target to fill X/Y fields, instead of hand-typing pixel coordinates. It serves both Android (Tap/Swipe) and Windows Input (Click/Right Click/Double Click/Mouse Move) actions.

These split along the testable/visual seam into two slices that ship as independent PRs.

---

## 2. Slices

### M10a — Android image actions (AdbCore only)

No UI; fully unit-testable. Ships first; no dependency on M10b.

### M10b — Coordinate picker (BotBuilder WPF + plumbing)

Depends on nothing in M10a functionally, but sequenced second because it is the visual/UX-heavy half.

**Merge handling:** M10a is pure AdbCore logic covered by unit tests with nothing for the user to visually validate, so per the established backend-only-slice rule it is created **and merged** via the `gh` CLI once green (PR link + test results surfaced to the user). M10b has a WPF dialog and goes to the user for visual verification and merge.

---

## 3. Slice M10a — Android image actions

### 3.1 Actions

| TypeKey | Display | Miss semantics |
|---|---|---|
| `android.findImage` | Find Image (Android) | not found → Fail (drives retry / `onFailure`) |
| `android.waitForImage` | Wait for Image (Android) | polls `timeoutMs` / `pollIntervalMs`; timeout → Fail |
| `android.assertImageAbsent` | Assert Image Absent (Android) | present → Fail (retry = wait-until-gone) |

Category: **Android**. Handle-based (read `IAndroidDevice` from the bound target handle), consistent with the M7a handle-as-bound-adapter model. No constructor-injected capturer (unlike Screen) — capture comes from the handle.

### 3.2 Capture

Capture via the bound `IAndroidDevice.Screenshot` (framebuffer → PNG bytes), decoded to a bitmap, then matched with the existing `ITemplateMatcher` (OpenCV). There is **no `captureMethod` field** — the Android framebuffer has no BitBlt/PrintWindow variants.

### 3.3 Shared match core (refactor)

The match-ROI-and-write-variables logic currently embedded in `ScreenActionBase` is extracted into a shared helper (working name `TemplateMatchCore`) consumed by both `ScreenActionBase` and a new `AndroidImageActionBase`. This guarantees Screen and Android produce **byte-identical output contracts** and prevents drift.

`TemplateMatchCore` owns: ROI crop (`regionX/Y/Width/Height`, coords offset back to full-frame), the `ITemplateMatcher` call at the configured confidence, and writing the output variables. It takes a captured bitmap + the config + an `IRandomSource`; it does **not** know how the bitmap was captured (that is the per-category base's job).

### 3.4 Output contract (identical to Screen)

Written to `Context.Variables`, lowercase + case-sensitive, stored as strings:

`matchLeft`, `matchTop`, `matchRight`, `matchBottom`, `matchCenterX`, `matchCenterY`, `matchRandX`, `matchRandY` (random in-region point via injected `IRandomSource`, for bot-masking), `matchConfidence`.

Config fields (mirroring Screen, minus `captureMethod`): `templatePath`, `confidence` (default **0.8**), `regionX/Y/Width/Height` (optional ROI), retry support. `${var}` interpolation applies as everywhere else.

### 3.5 Testing

xUnit in `AdbCore.Tests`: executors driven by a **fake `IAndroidDevice`** returning a canned framebuffer + a **fake `ITemplateMatcher`** + a deterministic `IRandomSource`, asserting the output-variable contract, ROI offset-back, confidence thresholding, and the three miss semantics. `TemplateMatchCore` gets direct unit tests. All existing Screen tests must stay green after the re-point. Palette/registry count tests bumped (defs +3, execs +3). The concrete framebuffer-decode path is live-verified against a real device (consistent with M7a adapter handling).

---

## 4. Slice M10b — Coordinate picker

### 4.1 Entry point

A **"Pick…" button** appears in the Properties panel for any action that declares coordinate fields. Clicking it opens the picker dialog for the action's bound target.

### 4.2 Coordinate-field metadata

Action definitions that take coordinates declare their point(s) as a small spec list of `(xKey, yKey, label)`:

| Action | Points |
|---|---|
| `android.tap` | 1: `(x, y, "Target")` |
| `android.swipe` | 2: `(startX, startY, "Start")`, `(endX, endY, "End")` |
| `input.click` / `input.rightClick` / `input.doubleClick` / `input.mouseMove` | 1: `(x, y, "Target")` |

The Properties panel shows the Pick button when the selected action exposes this metadata. The picker reads it to know 1-point vs 2-point mode and which field keys to write.

(Exact field keys for the Input actions are taken from the existing `PointerActionBase` config field definitions during implementation; the table above is the intent.)

### 4.3 Target resolution & capture

The picker reads the action's `TargetId` → `BotTarget` (type + selector; `null` → first target), then resolves and captures at author time:

- **Window** → `Win32WindowResolver` (selector → HWND) + `IWindowCapture` (client-relative capture).
- **Android** → `IAdbDevices` (selector → device) + `IAndroidDevice.Screenshot` (framebuffer).

Orchestration lives in **BotBuilder.Core** (WPF-free); the dialog is a thin WPF shell. Capture is a **single snapshot** (not a live mirror).

### 4.4 Pick flow

1. Dialog opens, captures the bound target, shows the screenshot scaled-to-fit.
2. User clicks. For a 2-point pick, they click **start** then **end**; the first click drops a small on-image marker for reference.
3. Reported coordinates are **source-pixel** — client-relative for Windows, device-pixel for Android — via the M6b `Stretch=Uniform` display→source mapping, so a scaled-down image does not lose precision. These are exactly the coordinate spaces the runtime actions consume.
4. On confirm, the X/Y value(s) are written back into the corresponding `ConfigFieldViewModel`(s).

**Precision aid:** the floor is scaled-to-fit click + source-pixel mapping. If small-target precision proves insufficient during implementation, add a magnifier / zoom-on-click — **not** an always-on dual 1×/2× panel (that BotCapture pattern is a non-interactive preview of an already-chosen crop and does not fit an interactive picker).

### 4.5 Edge cases & DPI

- Target not running/connected, or capture fails → friendly status message in the dialog, no exception; dialog stays cancellable (mirrors BotCapture behaviour).
- **DPI:** the BotBuilder picker must be **Per-Monitor-V2 aware** so captured pixels map 1:1 to runtime click space (the same DPI requirement already flagged for in-app test-run). Confirm BotBuilder's manifest/awareness covers the dialog.

### 4.6 Testing

- **BotBuilder.Core (unit):** the picker view-model, coordinate-field-metadata resolution, and the display→source coordinate-mapping math (pure function, tested like `CanvasViewport.ScreenToWorld`), including 1-point vs 2-point write-back to the right field keys, and the not-resolvable / capture-failure paths.
- **WPF dialog:** thin; user-verified.

---

## 5. Out of scope

- **OCR** (M11) — though it will reuse the same Android framebuffer + Screen capture paths.
- Live video streaming / device mirroring in the picker (snapshot only).
- Any new target types.
- Desktop UI / FlaUI (dropped from the roadmap).

---

## 6. Watch-items for user visual verify (M10b)

1. Pick button appears on Tap/Swipe (Android) and Click/Right/Double/Mouse Move (Windows); absent on actions without coordinate fields.
2. Picker captures the **correct bound target** (the one set on the action) for both a Windows window and an Android device.
3. Clicking a point writes coordinates that, when the bot runs, land where expected (validates the coordinate-space + DPI mapping end-to-end).
4. Swipe two-point flow: start marker, then end; both pairs written.
5. Target-not-connected → friendly message, no crash.
