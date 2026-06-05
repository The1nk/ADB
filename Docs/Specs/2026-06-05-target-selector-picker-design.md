# Target Selector Picker (chip QOL) — Design

**Status:** Approved (user green-lit 2026-06-05; Polish QOL item a)
**Context:** To fill a target chip's **Selector** today, the user runs F5 → the Test-Run target picker → picks a window/device → copies the selector → pastes it into the chip. This adds a **"Pick…" button on the target chip itself** that opens a small chooser (window list / device list / browser engines, per the chip's Type) and writes the selector straight into the chip — no F5/copy/paste.

---

## 1. Behavior

Each target chip (Type combo + Selector textbox + remove "x") gains a **"Pick…"** button. Clicking it opens a modal `SelectorPickerDialog` populated for the chip's current `Type`:
- **Window** → a list of currently-open windows (`ProcessName — Title`); choosing one writes `process:<ProcessName>` (or `title:<Title>` when the process name is empty).
- **AndroidDevice** → a list of connected ADB devices (`Serial (State)`); choosing writes `serial:<Serial>`. (Empty list if no ADB server/devices — the user can still type manually.)
- **Browser** → the available browser engines; choosing writes `browser:<engine>`.

On **OK**, the chosen selector is written into that chip's `Selector` (and the doc marked dirty). On **Cancel**, nothing changes. The user can still edit the Selector textbox by hand afterward.

This reuses the exact enumeration + selector-format logic the Test-Run `TargetPickerDialog` already uses (`IWindowEnumerator`, `IAdbDevices`, `BrowserSelector.Engines`).

## 2. Selector formatting (pure, testable)

Extract the selector-string logic into a pure helper `BotBuilder.Core/Targets/SelectorFormat.cs` so it's unit-testable and DRY:
```csharp
public static class SelectorFormat
{
    public static string Window(string processName, string title)
        => string.IsNullOrEmpty(processName) ? $"title:{title}" : $"process:{processName}";
    public static string Android(string serial) => $"serial:{serial}";
    public static string Browser(string engine) => $"browser:{engine}";
}
```
(Matches `TargetPickerDialog`'s existing inline logic. The new dialog uses this helper; the existing dialog is left untouched to avoid churn.)

## 3. Components

- `BotBuilder.Core/Targets/SelectorFormat.cs` (new, pure) + tests.
- `BotBuilder/SelectorPickerDialog.xaml` / `.xaml.cs` (new WPF modal): a single chooser (`ComboBox`/`ListBox`) populated for the given `BotTargetType`, plus OK/Cancel. Ctor takes the type + an `IWindowEnumerator` + an `IAdbDevices` (mirror how `TargetPickerDialog` constructs `AdvancedSharpAdbDevices`); exposes `ChosenSelector` (string?) after a true result. Browser engines come from `BrowserSelector.Engines`.
- `BotBuilder/MainWindow.xaml` — a "Pick…" `Button` in the target-chip `DataTemplate`, next to the Selector `TextBox` (`Click="PickSelector_Click"`).
- `BotBuilder/MainWindow.xaml.cs` — `PickSelector_Click`: resolves the chip's `TargetViewModel` from the button's `DataContext`, opens `SelectorPickerDialog` for `target.Type` (passing the window enumerator already available to the window / a fresh `Win32WindowEnumerator`), and on OK sets `target.Selector = dialog.ChosenSelector` + marks the editor dirty.

No new deps, no `.bot` schema change. Conflict-free with everything on main (the parked PRs #37/#39/#41 all merged or independent — this touches the TargetBar UI + a new dialog + a new Core helper, none of which collide).

## 4. Testing

- **`SelectorFormatTests` (BotBuilder.Core.Tests):** Window with process → `process:X`; Window with empty process → `title:Y`; Android → `serial:S`; Browser → `browser:chromium`.
- The dialog + button + enumeration are WPF → **user visual verify** (pick a real window/device and confirm the chip's Selector fills in).

## 5. Out of scope

- Live re-enumeration / refresh button in the dialog (enumerate once on open).
- Choosing `hwnd:`/`title:` variants in the UI (defaults to `process:` for windows; the user can hand-edit to `title:`/`hwnd:`). A radio for selector kind could follow.
- Refactoring `TargetPickerDialog` to use the shared `SelectorFormat` helper (left as-is to avoid touching the test-run flow).

## 6. Merge handling

Pure `SelectorFormat` is unit-tested, but the feature is a WPF interaction → opened as a PR and **user-verified + merged**, not self-merged.
