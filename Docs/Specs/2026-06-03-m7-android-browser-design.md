# M7 — Android + Browser Design

**Status:** Approved
**Date:** 2026-06-03
**Milestone:** M7 — Android + Browser (per `Docs/Design/V1.md` §4.3 / Milestones)

---

## 1. Overview

M7 adds the **Android** (ADB) and **Browser** (Playwright) action categories — the last major automation
surfaces in V1. Both plug into the existing target-resolution seam: a **type-specific binder** fills
`ResolvedTarget.Handle` at run start (mirroring the existing `WindowTargetBinder`), and the **handle is a
bound adapter** the action executors call. This keeps the executors fully unit-testable with fake handles,
while the thin AdvancedSharpAdbClient / Playwright adapter wrappers are verified live (the same split used
for the Win32 / OpenCvSharp adapters).

Delivered in **two slices, each a complete, usable category** including its target-picker discovery:

| Slice | Scope |
|---|---|
| **M7a** | Android: `IAndroidDevice` adapter + `AndroidTargetBinder` + 6 Android actions + live `adb devices` dropdown in the target picker |
| **M7b** | Browser: `IBrowserPage` adapter + `BrowserTargetBinder` (launch-owned) + 5 Browser actions + browser-engine dropdown in the target picker; run-end handle disposal |

**Libraries:** `AdvancedSharpAdbClient` (the maintained .NET fork; talks to the ADB server over TCP) and
`Microsoft.Playwright` (official .NET binding). Both are new package references in `AdbCore`.

**Deferred to M9 (not M7):** the *palette* greying-out of disabled categories ("disabled-dependency UX").
M7's new actions are **always-registered** (like Screen/Input) and fail gracefully at runtime with a
README-pointing message when the dependency is unreachable.

---

## 2. Architecture — the handle-as-bound-adapter pattern

At run start `TargetResolver.Resolve(bot, selectors)` produces `ResolvedTarget { Type, Selector, Handle }`
per target (Handle null). A binder then fills Handle:

- **Window** (existing): `WindowTargetBinder` → `IntPtr` HWND.
- **Android** (M7a): `AndroidTargetBinder` resolves `serial:<device>` → an **`IAndroidDevice`** bound to
  that device over the ADB server.
- **Browser** (M7b): `BrowserTargetBinder` launches Playwright → an **`IBrowserPage`**.

An action executor reads its target's handle and casts:

```csharp
if (context.Context.ResolvedTargets.GetValueOrDefault(targetId)?.Handle is not IAndroidDevice device)
    return ActionResult.Fail("This action requires a connected Android device target.");
device.Tap(x, y);
```

So executors take **no adapter in their constructor** (unlike Screen/Input, whose adapter is injected and
whose handle is a bare HWND) — the bound adapter *is* the handle. The real adapters are constructed by the
binders (in `BotRunner`); the Builder's picker uses the device/engine enumerators directly.

`AndroidActionBase` / `BrowserActionBase` provide the shared "resolve the bound handle for this action's
target, or fail" helper (mirroring `ScreenActionBase`/`InputActionBase`). `${var}` interpolation already
runs engine-wide before leaf dispatch, so all string/number config supports it.

---

## 3. Slice M7a — Android category

### Adapters (`AdbCore/Android/`)
- `IAndroidDevice` — the per-device operations: `Tap(int x, int y)`, `Swipe(int x1, int y1, int x2, int y2,
  int durationMs)`, `byte[] Screenshot()`, `PressBack()`, `LaunchApp(string package)`, `InstallApk(string
  apkPath)`. (Maps to ADB `input tap`, `input swipe`, framebuffer capture, `keyevent 4`, `monkey -p … 1`
  / `am start`, `pm install`.)
- `IAdbDevices` — `IReadOnlyList<AdbDeviceInfo> List()` (serial + state) for resolution and the picker.
- `IAdbDeviceResolver` — `serial:<device>` → a device identity, or none.
- `AdvancedSharpAdbClient*` concrete adapters — thin wrappers over `AdbClient` + the ADB server (no unit
  tests; live-verified). A `serial:` selector parser (`AdbSelector`) IS unit-tested.

### Actions (`AdbCore/Actions/BuiltIn/Android/`, `AndroidActionBase`)
**Tap** (x, y) · **Swipe** (x1, y1, x2, y2, durationMs) · **Screenshot** (save PNG to a path) · **Press
Back** · **Launch App** (package) · **Install APK** (apkPath). Category `"Android"`. Each resolves the
`IAndroidDevice` from the handle (fail if the target isn't a bound Android device), reads config via
`ConfigValues` (with `${var}` support), calls the device, returns Ok/Fail. Registered always-on in
`BuiltInActions.Register` (handle-based → no new constructor dependencies).

### Run wiring + picker
- `AndroidTargetBinder.Bind(resolvedTargets, …)` in `BotRunner/RunnerApp.RunAsync`, right after
  `WindowTargetBinder.Bind`. A `serial:` that resolves to no device, or an unreachable ADB server, is a
  CLI error (exit 2) with a README-pointing message — not a crash.
- **Picker:** the M8a `TargetPickerDialog` gains an Android dropdown for `AndroidDevice` rows (a new
  `TargetSelectionRow.IsAndroid`), populated from `IAdbDevices.List()`, setting `serial:<serial>`.

### Tests (AdbCore.Tests)
- Each action executor with a `FakeAndroidDevice` (records calls): correct device call + args; `${var}`
  interpolation; "no Android device bound" → Fail. Screenshot save round-trip.
- `AdbSelector` parse (`serial:emulator-5554` → serial; bad input).
- Registration: the Android definitions/executors are registered (count bumps).

---

## 4. Slice M7b — Browser category

### Adapter (`AdbCore/Browser/`)
- `IBrowserPage` — `Task GotoAsync(string url)`, `Task ClickAsync(string selector)`, `Task TypeAsync(string
  selector, string text)`, `Task WaitForSelectorAsync(string selector, int timeoutMs)`, `Task<string>
  GetTextAsync(string selector)`. Over Playwright `IPage`.
- **Launch-owned model:** Playwright launches its own browser rather than attaching to a running one, so a
  Browser target's selector is the **engine**: `browser:chromium` (default) / `firefox` / `webkit`.
  `BrowserTargetBinder` launches that engine → context → page (the handle). "Open URL" then navigates.
  (This is the practical realization of V1 §6.2 for Playwright, which has no notion of pre-existing external
  contexts.) Missing browser binaries → a clear "run `playwright install`, see README" message, not a crash.

### Actions (`AdbCore/Actions/BuiltIn/Browser/`, `BrowserActionBase`)
**Open URL** (url) · **Click Element** (selector) · **Type** (selector, text) · **Wait for Selector**
(selector, timeoutMs) · **Get Text** (selector → result var). Category `"Browser"`. Each resolves the
`IBrowserPage` from the handle and `await`s the page op (executors are already `Task`-based). Get Text
writes the result to a run variable.

### Lifecycle, run wiring + picker
- **Run-end disposal:** `RunnerApp` wraps the run so that, after execution, any `ResolvedTarget.Handle`
  implementing `IAsyncDisposable`/`IDisposable` is disposed — so the launched Playwright browser closes
  cleanly. (`IBrowserPage`'s implementation owns and disposes the browser/context.)
- `BrowserTargetBinder.Bind` in `RunnerApp.RunAsync` alongside the others.
- **Picker:** the Browser dropdown offers the three engines (`chromium`/`firefox`/`webkit`), setting
  `browser:<engine>` (a new `TargetSelectionRow.IsBrowser`).

### Tests (AdbCore.Tests)
- Each action executor with a `FakeBrowserPage` (records awaited calls): correct page call + args; `${var}`;
  "no Browser page bound" → Fail; Get Text writes the variable.
- `BrowserSelector` parse (`browser:chromium` → engine; default; bad input).
- Registration: the Browser definitions/executors are registered.

---

## 5. Cross-Cutting Concerns

**Graceful dependency failure.** No missing dependency crashes a run. An unreachable ADB server / unknown
device, or missing Playwright browsers, surfaces as a CLI error (binder) or action `Fail` with a message
pointing at the README — consistent with V1 §4.3's intent. (Palette disabling is M9.)

**Disposal.** Run-end disposal of `IAsyncDisposable`/`IDisposable` handles (added in M7b) ensures the
Playwright browser process is closed. Android adapters that hold no disposable state are unaffected.

**Async.** Browser actions are genuinely async and fit the engine's `Task`-based `ExecuteAsync`. Android
operations are synchronous shell/ADB calls wrapped to the same signature.

**Testing strategy.** All decision logic (action executors with fake handles, selector parsing,
registration) is unit-tested in `AdbCore.Tests`. The AdvancedSharpAdbClient / Playwright adapters and the
binders require a real device / installed browsers and are verified by the user (a real `adb` device for
M7a; `playwright install` + a site for M7b), consistent with the project's rhythm.

**Reused, not duplicated.** The target-resolution seam (`TargetResolver`/`ResolvedTarget`/binder pattern),
`ConfigValues` + `${var}` interpolation, `ActionRegistry`/`IActionDefinition`/`IActionExecutor`, the M8a
`TargetPickerDialog`. Only the two new adapter namespaces + binders + actions are added.
