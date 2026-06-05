# Disabled-Dependency Palette Greying — Design

**Date:** 2026-06-05
**Status:** Approved interaction model (soft-grey + tooltip, user-chosen); detection details are sensible defaults documented here for sign-off at PR review.
**Milestone:** M9 Polish — the original "grey out Android/Browser palette categories when their deps are unreachable" ask. Unblocked now that theming shipped (`DisabledTextBrush` exists).

## 1. Purpose

When the tooling a palette category depends on isn't present, grey that category (header + items) and show a tooltip explaining why — so the user understands those actions won't run here, without blocking them from building a bot they'll run elsewhere or after installing the dependency.

Gated categories: **Android** (needs `adb`) and **Browser** (needs Playwright browsers). All other categories are always available.

## 2. Decisions

| Decision | Choice |
| --- | --- |
| Interaction | **Soft-grey + tooltip, still usable** (USER-CHOSEN). Greyed items remain draggable/double-clickable — purely advisory. |
| Gated categories | Android, Browser (per the roadmap; FlaUI/Desktop-UI is dropped). |
| Android "available" | `adb` executable resolvable on the `PATH`. Reason when not: `"adb not found on PATH"`. |
| Browser "available" | At least one Playwright browser engine present in the Playwright browsers cache. Reason when not: `"No browser engine found — run 'playwright install'"`. |
| Detection timing | Once, at `PaletteViewModel` construction (both checks are cheap — a PATH scan + a directory check). A manual refresh is out of scope for v1 (noted as future). |
| Rendering | Greyed via `DisabledTextBrush`; tooltip = the reason. Items stay interactive. |

## 3. Architecture

### `BotBuilder.Core` (testable)

- `DependencyStatus` — `record(bool IsAvailable, string? Reason)`.
- `IDependencyProbe` — `DependencyStatus ForCategory(string category)`. Unknown categories → available.
- `DependencyProbe : IDependencyProbe` — live implementation, with **injectable seams** for testability:
  - ctor `DependencyProbe(Func<bool>? androidAvailable = null, Func<bool>? browserAvailable = null)`; defaults wire the real environment checks.
  - `ForCategory("Android")` → available if `androidAvailable()` else `(false, "adb not found on PATH")`.
  - `ForCategory("Browser")` → available if `browserAvailable()` else `(false, "No browser engine found — run 'playwright install'")`.
  - `ForCategory(other)` → `(true, null)`.
  - **Default Android check:** scan each `PATH` entry for `adb.exe` (case-insensitive); available if found. (Mirrors how `AdvancedSharpAdbDevices` relies on `adb` being on PATH.)
  - **Default Browser check:** look in the Playwright browsers cache — `PLAYWRIGHT_BROWSERS_PATH` if set, else `%USERPROFILE%\AppData\Local\ms-playwright` — for any subdirectory whose name starts with `chromium`, `firefox`, or `webkit`; available if at least one exists. (Playwright downloads engines there; absent ⇒ browser actions fail at runtime.)
  - The two default environment checks are environment-bound (not unit-tested, like `Win32OsThemeProbe`); the category→status mapping + reason strings ARE unit-tested via the seams.

- `PaletteCategory` — gains `bool IsAvailable` and `string? DisabledReason` (ctor params, default available).
- `PaletteItem` — gains `bool IsAvailable` and `string? DisabledReason` (copied from its category, so the item template can bind directly).
- `PaletteViewModel` — ctor gains optional `IDependencyProbe? probe = null` (default `new DependencyProbe()`); existing call sites are unchanged. In `Rebuild`, each category's status is resolved via `probe.ForCategory(group.Key)` and propagated to the category and its items.

### `BotBuilder` (WPF)

In `MainWindow.xaml`, the palette templates bind the new flags:
- Category header `TextBlock`: greys to `DisabledTextBrush` when `IsAvailable` is false (via a `DataTrigger`); `ToolTip="{Binding DisabledReason}"`.
- Item `Border`: `ToolTip="{Binding DisabledReason}"`; its `TextBlock` greys to `DisabledTextBrush` when `IsAvailable` is false.
- The existing `MouseMove` / `MouseLeftButtonDown` drag handlers are unchanged — greyed items still add nodes (soft model).

`DisabledReason` is null for available items, so WPF shows no tooltip on them.

## 4. Flow

At palette build (app start, and on every search re-filter), `PaletteViewModel.Rebuild` groups definitions by category and asks the probe for each category's status, stamping `IsAvailable`/`DisabledReason` onto the category and its items. The view renders greyed + tooltipped where unavailable. Detection runs once per `DependencyProbe` instance (the defaults compute on each `ForCategory` call, which is cheap; if needed the live probe may memoise — not required for v1).

## 5. Error handling

The environment checks are defensive: a missing/empty `PATH` or a missing Playwright cache directory simply yields "not available" (never throws). Any unexpected IO exception in a default check is swallowed and treated as "not available" (the category greys rather than crashing the palette).

## 6. Testing

- **Unit (`DependencyProbeTests`):** `ForCategory` with injected `androidAvailable`/`browserAvailable` Funcs — true→available/no-reason; false→unavailable with the exact reason string; unknown category→available. 
- **Unit (`PaletteViewModelTests`, existing file):** with a fake `IDependencyProbe` marking Android unavailable, the Android category and all its items carry `IsAvailable=false` + the reason, while other categories stay available.
- **Visual (user verify):** on a machine without adb and/or without Playwright browsers, the Android/Browser categories render greyed with the reason tooltip; items still drag onto the canvas; categories whose deps ARE present look normal.

## 7. Out of scope (v1)

- Manual "refresh availability" affordance (detect-once at startup is the v1 behavior).
- Hard-blocking greyed items (the chosen model is advisory/soft).
- Per-action (vs per-category) granularity — gating is at the category level.
