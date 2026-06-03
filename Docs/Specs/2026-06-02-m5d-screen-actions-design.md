# M5d — Screen Actions (OpenCvSharp) Design Spec

**Milestone:** M5d (final slice of M5 — Built-in Actions), per `Docs/Design/V1.md` §4.3 and `Docs/Specs/2026-06-01-m5-built-in-actions-design.md` §4.3.
**Date:** 2026-06-02
**Status:** Approved decisions captured; pending user review before planning.

## 1. Overview & Goal

Add the **Screen** action category: window-targeted image recognition built on OpenCvSharp template matching. Screen actions capture the target window's bitmap, run a template match, and route `onSuccess`/`onFailure` accordingly. This is the first milestone slice to take a **native NuGet dependency** (OpenCvSharp bundles native binaries) and the first real exercise of the M4 **ImagePath** editor, the **RetryPolicy**, and the `.meta.json` confidence sidecar (read side only).

This spec also introduces a foundational primitive the whole tool needs: **`${variable}` interpolation in config values**, so a Find Image result can actually be consumed by a downstream Click/Move/Type.

The four Screen actions (from the M5 spec):

| Action | TypeKey | Retry | Slice |
|---|---|---|---|
| Find Image | `screen.findImage` | yes | **M5d1** |
| Wait for Image | `screen.waitForImage` | yes | M5d2 |
| Screenshot | `screen.screenshot` | no | M5d2 |
| Assert Image Absent | `screen.assertImageAbsent` | yes | M5d2 |

**This spec is implemented in two PRs** (user's choice):
- **M5d1** — config `${var}` interpolation + OpenCvSharp wiring + capture/matcher infra + `ScreenActionBase` + **Find Image** (proves capture→match→retry→variable-output→consume end-to-end).
- **M5d2** — Wait for Image, Screenshot, Assert Image Absent on the proven infra.

## 2. Locked Decisions

1. **Packages:** `OpenCvSharp4` + `OpenCvSharp4.runtime.win` (explicit native runtime; first NuGet dep in `AdbCore`). First plan step proves it builds and the native libs load on `net10.0-windows`.
2. **Match algorithm:** OpenCvSharp `MatchTemplate` with `TM_CCOEFF_NORMED` (clean 0–1 score), single best match (`MinMaxLoc`).
3. **Capture:** `Auto` = PrintWindow(`PW_RENDERFULLCONTENT`) with automatic BitBlt fallback on a blank frame; `BitBlt` = forced screen-region capture. Per-node **Capture Method** enum field (mirrors the Input "method" field).
4. **Find Image output variables** (client-relative integer pixels; default prefix `match`): `matchLeft/matchTop/matchRight/matchBottom` (region edges = x1/y1/x2/y2), `matchCenterX/matchCenterY` (center), `matchRandX/matchRandY` (uniform-random point inside the region — for human-like click jitter), `matchConfidence` (the actual score, as a string).
5. **Consumption:** general `${variableName}` interpolation in config values, applied centrally before leaf dispatch. Folded into M5d1 so the find→random-click flow is demoable.
6. **Confidence default:** `0.8` (pre-fills from a `.meta.json` sidecar when present — read side already shipped in M4b).
7. **Screenshot format:** PNG (M5d2).
8. **Out of scope:** writing the `.meta.json` sidecar + the live "test match" UI (both **M6 BotCapture**); Android/Browser actions (**M7**).

## 3. Architecture

### 3.1 Config Variable Interpolation (`${var}`)

**Problem:** config values are static. `ClickAction` reads X/Y straight from its config via `ConfigValues.GetInt`; nothing resolves a run variable into a config value today (only `Branch` reads a variable by name, `SetVariable` writes one). So a Find Image result can't feed a Click.

**Design:** a pure, tested `ConfigInterpolator` performs `${name}` substitution against the run `Variables`, applied **centrally by the engine** so every action benefits with no per-action change.

- New `AdbCore/Execution/ConfigInterpolator.cs`:
  - `string Interpolate(string template, IReadOnlyDictionary<string, object> variables)` — replaces each `${name}` token (regex `\$\{([^}]+)\}`, name trimmed) with the variable's string form via `ConfigValues.AsString`. **Unknown variable → empty string.** A value containing no `${` is returned unchanged (fast path).
  - `BotAction Resolve(BotAction action, IReadOnlyDictionary<string, object> variables)` — returns the action unchanged if no string config value contains `${`; otherwise returns a clone whose `Config` is a new dictionary with **string** values interpolated (non-string values — bool/number/`JsonElement` — pass through untouched). The original action/config is never mutated.
- `BotAction` gains a small, tested `CloneWithConfig(Dictionary<string, object> config)` returning a shallow copy with the given config (so cloning stays correct if `BotAction` grows fields).
- **Wiring:** in `BotExecutor.ExecuteWithRetryAsync`, immediately before building the `ActionExecutionContext` (currently `BotExecutor.cs:380`), resolve the action: `var resolved = ConfigInterpolator.Resolve(action, state.Context.Variables); var actionContext = new ActionExecutionContext(resolved, state.Context, state.Log);`. Done once per action execution (variables are stable across that action's retry attempts). `action.Retry` is still read from the original.
- **Reserved syntax:** `${` introduces a token; there is no escape sequence in M5d1 (documented; revisit only if a literal `${` is ever needed).

This makes `Click X = ${matchRandX}`, `Type Text = "Hello ${name}"`, and dynamic paths all work, and is exercised by the M5d1 manual verification (find → click the random point).

### 3.2 Window Capture (`AdbCore/Screen/`)

- `IWindowCapture` — `Bitmap Capture(IntPtr hwnd, ScreenCaptureMethod method)` (returns a fresh bitmap of the window's client area; caller disposes). Behind the interface so the engine/tests fake it.
- `enum ScreenCaptureMethod { Auto, BitBlt }`.
- `Win32WindowCapture` (thin adapter, build-only/manually verified):
  - **Auto:** `PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT=2)` into a compatible bitmap sized to the client rect. If the result is blank (all-black / all-same-pixel heuristic), fall back to **BitBlt**.
  - **BitBlt:** `ClientToScreen` the client rect → `BitBlt` from the screen DC into the bitmap (captures whatever is visible, incl. GPU/DirectX surfaces; requires the window unoccluded/foreground).
- Capture targets the **client area** so match coordinates are client-relative (consistent with Input actions' client-relative coordinates).

### 3.3 Template Matching (`AdbCore/Screen/`)

- `record MatchResult(int X, int Y, int Width, int Height, double Score)` — `X,Y` = top-left of the match in haystack (client) pixels.
- `ITemplateMatcher` — `MatchResult? Match(Bitmap haystack, string templatePath, double minConfidence)`. Returns the best match if its score ≥ `minConfidence`, else `null`. Throws a clear exception if the template file is missing/unreadable.
- `OpenCvSharpTemplateMatcher` (thin adapter, manually verified): load template (`Cv2.ImRead`), convert haystack `Bitmap`→`Mat`, `Cv2.MatchTemplate(..., TM_CCOEFF_NORMED)`, `Cv2.MinMaxLoc` → best location + score. `Width/Height` from the template size. Disposes all `Mat`s.

### 3.4 `ScreenActionBase`

Shared base for the Screen leaf actions (`IActionDefinition` + `IActionExecutor`), mirroring `InputActionBase`:

- Holds injected `IWindowCapture` + `ITemplateMatcher`.
- Resolves the target **HWND** from `ResolvedTarget.Handle` exactly like `InputActionBase` (explicit `TargetId`, or the sole target if unset); fails with a clear message when no Window/HWND is resolved.
- Contributes the shared **Capture Method** enum config field (`captureMethod`, default `Auto`), shown after each action's own fields (mirrors `InputActionBase`'s Method field ordering/caching).
- `Category => "Screen"`, ports `in → onSuccess / onFailure`.
- Provides a `protected MatchResult? CaptureAndMatch(context, hwnd, templatePath, confidence)` helper (capture via chosen method → match), reused by Find Image / Wait / Assert Absent.
- `SupportsRetry => true` (Screenshot overrides to `false`).

### 3.5 Find Image (`screen.findImage`) — M5d1

**Config fields:** `templatePath` (ImagePath), `confidence` (Number, default `0.8`), `resultVar` (String, default `match`), `captureMethod` (from base). Retry: yes. Ports: `in → onSuccess / onFailure`.

**Execution:**
1. Resolve HWND (base). Read `templatePath`, `confidence`, `resultVar` (all already interpolated by the engine).
2. `CaptureAndMatch`. If `null` (no match ≥ confidence) → return `ActionResult.Ok("onFailure")`, write nothing.
3. On match, compute (client-relative ints) and write to `context.Context.Variables` under the `resultVar` prefix:
   - `{p}Left=X`, `{p}Top=Y`, `{p}Right=X+W`, `{p}Bottom=Y+H`
   - `{p}CenterX=X+W/2`, `{p}CenterY=Y+H/2`
   - `{p}RandX`, `{p}RandY` — uniform random in `[Left,Right]`×`[Top,Bottom]` (inclusive)
   - `{p}Confidence` = score
   Values stored as strings (consistent with `SetVariable`; readers like `ConfigValues.GetInt` and the new interpolation coerce). Then `ActionResult.Ok("onSuccess")`.

**Randomness seam:** Find Image takes an injected `IRandomSource` (`int Next(int minInclusive, int maxInclusive)`); production impl wraps `Random.Shared`; tests inject a deterministic fake. Registered in `BuiltInActions` alongside the capture/matcher singletons.

**Registration:** `BuiltInActions.Register` constructs `IWindowCapture`/`ITemplateMatcher`/`IRandomSource` once and passes them to the Screen actions (mirrors the shared `InputSenderResolver`).

## 4. Testing Strategy

Per the M5 spec's interface-isolation principle: logic behind interfaces is tested headlessly with fakes; thin OS/native adapters (`Win32WindowCapture`, `OpenCvSharpTemplateMatcher`) are verified by the user via a real run.

- **ConfigInterpolator:** literal passthrough; single/multiple tokens; unknown→empty; non-string values untouched; no-`${` fast path returns same instance; `BotExecutor` resolves before dispatch (an integration test: a variable feeds a fake executor's config).
- **Find Image:** with a fake `IWindowCapture` (returns a stub bitmap) + fake `ITemplateMatcher` (returns a fixed `MatchResult` or `null`) + deterministic `IRandomSource`: assert the exact variables written (Left/Top/Right/Bottom/Center/Rand/Confidence), onSuccess vs onFailure routing, no-target failure, retry interplay (matcher returns null N-1 times then a hit), and that Rand falls within bounds.
- **MatchResult math:** center/edge derivation.

### Manual Verification Checklist (M5d1, user)
1. A bot with a Window target + Find Image (real template captured from the window) routes `onSuccess` when present and `onFailure` when absent.
2. Confidence threshold behaves (raising it past the real score flips success→failure).
3. **End-to-end masking flow:** Find Image → Click at `X=${matchRandX}, Y=${matchRandY}` clicks inside the matched region, and repeated runs land on different points within it.
4. `Auto` capture works for a normal app; `BitBlt` override works for a foreground GPU/DirectX window.

## 5. Out of Scope (later milestones)
- Writing the `.meta.json` confidence sidecar + live "test match" preview — **M6 BotCapture**.
- Android (`android.*`) and Browser (`browser.*`) action categories + their target-handle resolution — **M7**. See `Docs/Design/V1.md` §9.
- `${var}` escaping, expressions/arithmetic, or interpolation of non-string config values — deferred until a concrete need arises.
