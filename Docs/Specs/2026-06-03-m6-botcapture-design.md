# M6 — BotCapture Design

**Status:** Approved
**Date:** 2026-06-03
**Milestone:** M6 — BotCapture (per `Docs/Design/V1.md` §7)

---

## 1. Overview

BotCapture is a standalone WPF tool for capturing and validating template images used by
screen-matching actions (Find Image, Wait for Image, Assert Image Absent). It runs in two modes:

- **Standalone** — launched directly (`BotCapture.exe`), stays open for building a template library.
- **Integrated** — launched by BotBuilder with `--output <path>`, captures a single template to that
  path, then exits.

The tool reuses the capture and template-matching infrastructure already in `AdbCore`
(`IWindowCapture`/`Win32WindowCapture`, `ITemplateMatcher`/`OpenCvSharpTemplateMatcher`). The only new
shared primitive is a **window enumerator** (list visible windows for the picker).

The milestone is delivered in **three slices**, each an independently reviewable PR:

| Slice | Scope |
|---|---|
| **M6a** | BotCapture project scaffold; `IWindowEnumerator` in AdbCore; window-picker screen; capture selected window |
| **M6b** | Region drag-select; preview/confirm panel; confidence slider; live Test Match; save PNG + `.meta.json` sidecar |
| **M6c** | Standalone session list (V1 §7.4); integrated `--output` single-shot mode |

> The BotBuilder-side wiring that invokes `--output` and reloads the image field is **M8** (Integration),
> not M6. M6c adds only the `--output` capability to BotCapture itself.

---

## 2. Project Layout

Mirrors the existing BotBuilder split so logic is unit-testable apart from the WPF shell:

```
BotCapture/            # WPF shell — Views, thin code-behind, App bootstrap, CLI arg parsing
BotCapture.Core/       # Testable VM + services: session state, capture orchestration,
                       #   sidecar read/write, filename generation. No WPF dependencies.
BotCapture.Core.Tests/ # xUnit
```

`BotCapture` references `BotCapture.Core` and `AdbCore`. `BotCapture.Core` references `AdbCore`.

Project properties match BotBuilder: `net10.0-windows`, `Nullable=enable`, `ImplicitUsings=enable`.
`BotCapture` is `WinExe` with `UseWPF=true`. All three projects are added to `ADB.sln`.

---

## 3. New Shared Primitive — Window Enumerator (AdbCore.Targets)

The picker needs a *list* of windows; `Win32WindowResolver` only resolves a single selector. Add a
list-shaped sibling using the same Win32 surface (`EnumWindows`, `IsWindowVisible`, `GetWindowText`).
It lives in `AdbCore.Targets` because the BotBuilder target-picker (M8) will reuse it.

```csharp
namespace AdbCore.Targets;

/// <summary>A visible top-level window discovered by enumeration.</summary>
public readonly record struct WindowInfo(IntPtr Handle, string Title, string ProcessName);

/// <summary>Enumerates visible top-level windows suitable for capture/selection.</summary>
public interface IWindowEnumerator
{
    IReadOnlyList<WindowInfo> Enumerate();
}

public sealed class Win32WindowEnumerator : IWindowEnumerator { /* ... */ }
```

**Filtering rules:** include a window only if `IsWindowVisible(hWnd)` is true and `GetWindowTextLength > 0`
(non-empty title). Resolve `ProcessName` via `GetWindowThreadProcessId` → `Process.GetProcessById`
(best-effort; on failure use empty string, never throw — a process can exit mid-enumeration). The tool's
own window is excluded by the caller if needed (its title is known).

---

## 4. Slice M6a — Scaffold + Window Picker + Capture

### Flow
1. App launches → `WindowPickerViewModel` calls `IWindowEnumerator.Enumerate()`.
2. Each `WindowInfo` becomes a row: process name + title + a thumbnail captured **once** via
   `IWindowCapture.Capture(handle, ScreenCaptureMethod.Auto)` (PrintWindow + BitBlt fallback), downscaled for display.
3. A **Refresh** button re-enumerates and re-captures thumbnails. (Continuously-live thumbnails are
   explicitly out of scope — capture-on-load + manual refresh is sufficient.)
4. Selecting a row and confirming captures the window's full client area and stores the bitmap on the
   session VM, ready for M6b's region select. In M6a, "confirm" simply lands on a placeholder
   "captured" state (the region UI arrives in M6b).

### Components
- `BotCapture.Core/WindowPickerViewModel.cs` — holds enumerated rows, selected window, triggers capture.
  Depends on `IWindowEnumerator` + `IWindowCapture` (constructor-injected; fakes in tests).
- `BotCapture.Core/WindowRow.cs` — view-row record: `WindowInfo` + thumbnail bytes/handle.
- `BotCapture/Views/WindowPickerView.xaml` — list of rows with thumbnails, Refresh button, Select action.
- `BotCapture/App.xaml(.cs)` — composition root wiring real `Win32WindowEnumerator` + `Win32WindowCapture`.

### Capture-failure handling
If capture throws or returns a blank frame, the row is still selectable but the captured bitmap is
reported as failed via a user-visible message; selection returns to the picker. (`Win32WindowCapture`
already falls back from a blank PrintWindow frame to screen BitBlt internally.)

### Tests (BotCapture.Core.Tests)
- Enumerator results map to rows in order; empty enumeration yields an empty list.
- Selecting a window invokes capture with the row's handle and the chosen capture method.
- A capture that throws is surfaced as a failure state, not an unhandled exception.

---

## 5. Slice M6b — Region Select + Preview/Confirm + Test Match + Save

### Flow
1. The captured bitmap is shown on a region-select surface. The user drags a rectangle; the selected
   region is highlighted and everything outside is dimmed.
2. Releasing the drag crops the region. The preview panel shows the crop at **1×** and **2×**.
3. A **filename** field is pre-filled with the next auto-generated name (`capture_NNN.png`, where `NNN`
   is the lowest integer not colliding with existing files in the save folder). User may rename.
4. A **confidence** slider (range 0.0–1.0, default 0.9) sets the match threshold.
5. **Test Match**: re-capture the *source* window, run `ITemplateMatcher.Match(freshFrame, savedCropOrTempPath,
   sliderValue)`. Overlay a 🟢 green box at the match location with the numeric score if matched at the
   threshold, or 🔴 red "no match" if below. The user can adjust the slider and re-test until satisfied.
   (Test Match operates on the cropped template; it may write the crop to a temp file to feed the
   path-based matcher, or the matcher is called with an in-memory overload if added — see note.)
6. **Save** writes `<name>.png` and `<name>.png.meta.json` (`{ "confidence": <value> }`) to the save
   folder. **Retake** returns to region select.

> **Matcher input note:** `ITemplateMatcher.Match` currently takes a *templatePath*. M6b's Test Match
> needs to match the in-memory crop before it is saved. Resolve by writing the crop to a temp PNG and
> passing its path (simplest, no AdbCore change), which the plan will specify. No new matcher overload is
> required for M6.

### Components
- `BotCapture.Core/RegionSelectionViewModel.cs` — source bitmap, current selection rectangle, crop.
- `BotCapture.Core/PreviewConfirmViewModel.cs` — crop, filename, confidence, last test-match result.
- `BotCapture.Core/TemplateSink.cs` (or `CaptureSaver.cs`) — writes PNG + sidecar; owns filename
  auto-increment. Depends only on the filesystem (injectable abstraction for tests).
- `BotCapture.Core/ConfidenceSidecar.cs` — read/write `<image>.png.meta.json`; tolerant of
  missing/corrupt files (returns default on read failure, never throws on read).
- `BotCapture/Views/RegionSelectView.xaml`, `PreviewConfirmView.xaml`.

### Tests
- Filename auto-increment: skips existing files, picks lowest free `capture_NNN`.
- Sidecar round-trip: written confidence reads back; missing sidecar → default; corrupt JSON → default.
- Confidence parse/clamp: out-of-range slider/typed values clamp to [0,1].
- Region crop math: selection rectangle maps to correct pixel bounds within the source bitmap.
- Test Match wiring: invokes the matcher with the confidence value and reports match/no-match + score.

---

## 6. Slice M6c — Session List + Integrated Mode

### Standalone session panel (V1 §7.4)
- **Source Window** selector (reuses the M6a picker) and **Save Folder** selector at the top.
- A list of captures saved in the current session, each row showing filename, saved confidence, a
  re-test 🟢/🔴 indicator, and a delete (🗑) action. Clicking a row re-opens the preview/confirm panel
  for that template (re-edit without recapturing).
- **+ New Capture** starts a fresh capture flow.
- The re-test indicator runs the saved template against a fresh capture of the source window at its
  saved confidence and shows green/red.

### Integrated mode
- `BotCapture.exe --output "C:\...\attack-btn.png"` parses the flag at startup.
- In this mode the session panel is skipped: the app runs a single capture → region → preview/confirm
  flow, **Save** writes to exactly that path (+ `<path>.meta.json`), then the process **exits** with
  code 0. Cancelling exits non-zero so BotBuilder (M8) can detect abandonment.
- Standalone mode (no `--output`) is unchanged.

### Components
- `BotCapture.Core/SessionViewModel.cs` — session rows, source window, save folder, add/remove/re-edit.
- `BotCapture.Core/SessionRow.cs` — saved capture: path, confidence, last re-test result.
- `BotCapture.Core/CommandLineArgs.cs` — parse `--output`; mirror BotRunner's arg-parsing style.
- `BotCapture/Views/SessionView.xaml`.

### Tests
- Arg parsing: `--output <path>` sets integrated mode + path; absent → standalone; missing value → error.
- Session add appends a row with the saved confidence; delete removes it; re-edit selects it.
- Re-test indicator reflects matcher result (green when matched at saved confidence, red otherwise).
- Integrated save targets the exact `--output` path; exit code reflects save vs cancel.

---

## 7. Cross-Cutting Concerns

**Error handling.** No capture or sidecar issue crashes the app. Capture failures return to the picker
with a message. Sidecar reads fall back to default confidence on any failure. Sidecar writes are
best-effort and logged, not fatal to the save.

**DPI.** BotCapture must share one pixel space with capture/match, matching the runner's fix
(commit `3e4dd40`): opt into Per-Monitor-V2 DPI awareness via the app manifest so captured bitmaps and
on-screen overlays use the same coordinates.

**Testing strategy.** All decision logic lives in `BotCapture.Core` and is unit-tested with fakes for
the enumerator/capture/matcher/filesystem. WPF view interaction (drag-select feel, overlay rendering)
is verified visually by the user, consistent with the project's established rhythm.

**Reused, not duplicated.** `IWindowCapture`, `ITemplateMatcher`, `ScreenCaptureMethod`, and
`MatchResult` come from `AdbCore`. Only `IWindowEnumerator`/`WindowInfo` are added there.
