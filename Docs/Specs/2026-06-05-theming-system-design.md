# Theming System — Design

**Date:** 2026-06-05
**Status:** Approved (brainstorming complete)
**Milestone:** M9 Polish — theme/dark-mode support (foundation for the disabled-dependency palette greying follow-up)

## 1. Purpose

Give the WPF apps (BotBuilder and BotCapture) a switchable visual theme with **Light**, **Dark**, and **High-Contrast** options. The user can follow the OS theme automatically or pick an explicit theme; the choice persists across restarts.

This is sequenced *before* the disabled-dependency palette greying so that the greyed/disabled palette items reference a theme-aware `DisabledTextBrush` from day one and never need reworking when dark mode lands. Today the apps use hardcoded inline hex colours in XAML and have no theme infrastructure or settings persistence — this milestone introduces both.

## 2. Decisions (captured during brainstorming)

| Decision | Choice |
| --- | --- |
| Themes shipped | Light, Dark, High-Contrast |
| Selection model | `System` (follow OS) · `Light` · `Dark` · `HighContrast`; **default = System** |
| Persistence | Yes — `%AppData%/ADB/settings.json` (a general settings bag, not theme-locked) |
| App scope | Both BotBuilder **and** BotCapture, via a shared WPF theme assembly |
| Control-theming approach | **A — hand-rolled** semantic brushes + implicit control styles; **no new third-party dependency** |
| Palette greying (separate slice) | Consumes `DisabledTextBrush` from this system; **not** built in this milestone |

### Why approach A (hand-rolled, no deps)

WPF's stock controls (TextBox, Button, Menu, ScrollBar, …) keep their default *light* chrome unless explicitly restyled — there is no built-in theme switching. Approach A defines the small set of control styles the apps actually use, all referencing `DynamicResource` brush keys, so they recolour with the active theme. This matches the project's deliberately lean, dependency-light ethos and keeps full control over the look. A third-party UI toolkit (approach B) was rejected as a heavy dependency that imposes its own design language and risks fighting the custom node-canvas visuals. A brush-swap-only approach (C) was rejected because it leaves stock controls light in dark mode (white textboxes/menus), i.e. a half-broken dark theme.

## 3. New project structure

### `AdbUi.Theme` (new WPF class library — `<UseWPF>true</UseWPF>`)

Referenced by both BotBuilder and BotCapture.

**Resource dictionaries** (`Themes/`):

- `Controls.xaml` — **theme-agnostic** implicit (keyless) styles for the standard controls the apps use: `TextBox`, `Button`, `Menu` / `MenuItem`, `ComboBox` / `ComboBoxItem`, `ScrollBar`, `ContextMenu`, `ToolTip`, plus base `TextBlock`/`Window` defaults. Every colour is `{DynamicResource <BrushKey>}`. Written once; never duplicated per theme.
- `Light.xaml`, `Dark.xaml`, `HighContrast.xaml` — **brush definitions only**. Each provides the *identical* set of semantic brush keys (see §4). These are the only dictionaries that differ between themes.

**Types / logic:**

- `AppTheme` enum `{ Light, Dark, HighContrast }` — the *effective* theme actually applied.
- `ThemeSelection` enum `{ System, Light, Dark, HighContrast }` — the user's *choice*.
- `ThemeResolver` — **pure, testable**: `Resolve(ThemeSelection selection, AppTheme osTheme) → AppTheme`. `System` → `osTheme`; any explicit selection → itself.
- `AppSettings` record — currently `{ ThemeSelection Theme }`; shaped as a general bag so future settings have a home. Serialised as JSON.
- `ISettingsStore` + `JsonSettingsStore` — `Load() → AppSettings` / `Save(AppSettings)`. Path injectable (tests use a temp dir). Resilient: missing or corrupt file → defaults, rewritten on next save.
- `SettingsPaths` — resolves `%AppData%/ADB/settings.json`; injectable/overridable for tests.
- `IOsThemeProbe` + `Win32OsThemeProbe` — `Current → AppTheme` and an `OsThemeChanged` event. Live impl reads `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme` and `SystemParameters.HighContrast`; raises change via `Microsoft.Win32.SystemEvents.UserPreferenceChanged`.
- `ThemeManager` — orchestrates everything WPF-bound:
  - `Initialize(ThemeSelection)` at app startup: resolve effective theme (consulting `IOsThemeProbe` when `System`) and merge `Controls.xaml` + the effective brush dictionary into `Application.Current.Resources.MergedDictionaries`.
  - `Apply(ThemeSelection)`: live-swap exactly **one tagged** theme-brush dictionary (so `DynamicResource` bindings update and no duplicate dictionaries leak), persist via `ISettingsStore`.
  - Subscribes to `IOsThemeProbe.OsThemeChanged` **only while** selection is `System`; re-resolves + swaps on OS change. Ignores OS changes when an explicit theme is chosen.
  - Exposes `CurrentSelection` + a change event so menus can show the active radio/checkmark.

### `AdbUi.Theme.Tests` (new test project)

Unit tests for the pure / seam-able parts (see §6).

## 4. Semantic brush-key contract

All three theme dictionaries MUST define the same `SolidColorBrush` keys. Working set (final list pinned during slice 1):

| Key | Role |
| --- | --- |
| `WindowBackgroundBrush` | Window / root background |
| `PanelBackgroundBrush` | Side panels (palette, properties), toolbars, menu bar |
| `SurfaceBackgroundBrush` | Cards / list items / input backgrounds |
| `CanvasBackgroundBrush` | Node-graph canvas backdrop |
| `PrimaryTextBrush` | Main text |
| `SecondaryTextBrush` | Muted / secondary text |
| `DisabledTextBrush` | Disabled / greyed text — **consumed by the palette-greying follow-up** |
| `BorderBrush` | Default borders / dividers |
| `AccentBrush` | Selection / focus accent |
| `AccentTextBrush` | Text drawn on the accent colour |
| `ControlBackgroundBrush` | TextBox / Button / ComboBox chrome |
| `ControlBorderBrush` | Control borders |
| `ControlHoverBackgroundBrush` | Hover state |
| `MenuBackgroundBrush` | Menus / context menus |
| `ScrollBarThumbBrush` | Scrollbar thumb |
| `SelectionBackgroundBrush` | Selected list row / marquee fill |
| `ErrorBrush` | Run-failure / error status |
| `SuccessBrush` | Run-success status |

**`CategoryColors` stay theme-independent.** They are saturated semantic accents (purple Android, etc.) applied as node-header backgrounds and read acceptably on any background. Left unchanged for v1; revisit only if a specific header reads poorly in a theme.

## 5. Flow

**Startup** (each app's `App.OnStartup`):
1. `JsonSettingsStore.Load()` → `AppSettings` (default `Theme = System` when no file / corrupt).
2. `ThemeManager.Initialize(settings.Theme)` → resolve effective theme (consult `IOsThemeProbe` if `System`) → merge `Controls.xaml` + effective brush dictionary.

**User switches theme:**
- BotBuilder: `View ▸ Theme` radio submenu — `System` / `Light` / `Dark` / `High-Contrast`.
- BotCapture: a compact theme selector (small dropdown/toggle in a corner of its layout — BotCapture has no menu bar, so a full menu bar is not added; exact placement validated during slice 3).
- Selecting an option → `ThemeManager.Apply(selection)` → live dictionary swap (all `DynamicResource` bindings recolour) → persist.

**Live OS change** while selection is `System` → `IOsThemeProbe.OsThemeChanged` fires → `ThemeManager` re-resolves + swaps. Ignored when an explicit theme is active.

**Shared settings file** → both apps read/write the same `settings.json`, so they stay consistent on the chosen theme.

## 6. Error handling

- Missing / corrupt `settings.json` → fall back to `System` default; rewrite on next save. No intrusive error UI.
- `Win32OsThemeProbe` registry read failure → assume `Light`.
- `ThemeManager` swap always removes the previously-applied tagged theme-brush dictionary before adding the new one — never accumulates duplicates.

## 7. Testing strategy

**Unit (`AdbUi.Theme.Tests`):**
- `ThemeResolver` matrix: `System + osDark → Dark`, `System + osLight → Light`, `System + osHighContrast → HighContrast`, each explicit selection → itself.
- `JsonSettingsStore`: round-trip save/load (temp path); missing-file → default; corrupt-file → default (no throw); save after corrupt rewrites valid JSON.
- `AppSettings` JSON shape (forward-compatible bag).
- All using a fake `IOsThemeProbe` and an injected temp settings path.

**Visual (user verifies):**
- All three themes render correctly in BotBuilder and BotCapture.
- Live OS-follow: changing Windows app theme flips the app when on `System`.
- Menu / selector reflects the active theme (radio/checkmark).
- Persistence: chosen theme survives an app restart.
- Disabled / greyed text and run-status colours legible in every theme.

## 8. Suggested build slices (for the plan)

1. **Theme core + infra** — `AdbUi.Theme` assembly (enums, `AppSettings`, `ISettingsStore`/`JsonSettingsStore`, `ThemeResolver`, `IOsThemeProbe`/`Win32OsThemeProbe`, `ThemeManager`, `Controls.xaml` + the three brush dictionaries with the full key set) + `AdbUi.Theme.Tests`. Mostly self-verifiable logic; the dictionaries are visual.
2. **BotBuilder adoption** — startup wiring, `View ▸ Theme` menu, migrate its 5 themed XAML files (MainWindow, CoordinatePickerDialog, RegionPickerDialog, LogPanelView, TargetPickerDialog) from inline hex → `DynamicResource`, confirm implicit control styles. *(User visual-verify: all three themes, switch, persist, OS-follow.)*
3. **BotCapture adoption** — reference the assembly, migrate its XAML, add the compact theme selector. *(User visual-verify.)*

## 9. Out of scope (v1)

- Per-theme `CategoryColors` variants (kept fixed).
- User-customisable / custom-accent themes beyond the three shipped (architecture leaves room: drop in a new brush dictionary with the same keys).
- Theming any non-WPF surface (the BotRunner console child has no UI to theme).
