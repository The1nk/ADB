# Activate Window Action — Design

**Status:** Approved (user green-lit 2026-06-05; previously deferred as "scope with user first")
**Context:** There's no explicit way to bring a target window to the foreground. Input actions (Click/Type) call `SetForegroundWindow` implicitly right before injecting, but there's no standalone "focus this window" step — useful to activate a window at the start of a sequence, switch focus between windows, or ensure the right window is foreground before a burst of actions.

---

## 1. Overview

A new built-in **Activate Window** action (`window.activate`, **Window** category) that resolves its target window's HWND and brings it to the foreground (restoring it first if minimized). It targets a `Window` target exactly like Screen/Input actions (same `TargetResolution.ResolveHandle<IntPtr>` path merged in #36).

## 2. Action

`ActivateWindowAction` — `IActionDefinition` + `IActionExecutor`.
- **TypeKey:** `window.activate`. **DisplayName:** "Activate Window". **Category:** "Window" (new palette category).
- **Ports:** `in` / `onSuccess` / `onFailure`. No window target resolved → `onFailure` ("requires a window target"); otherwise activate → `onSuccess`.
- **Config:** none (v1 always restores-if-minimized + brings to foreground — the sensible default; a `restoreIfMinimized` toggle is YAGNI for now).
- **`SupportsRetry`** = false.
- **Target:** resolves the HWND via `TargetResolution.ResolveHandle<IntPtr>(context)` (explicit TargetId or the lone Window target). `IntPtr.Zero`/none → onFailure.

## 3. Activation mechanism (injectable)

- **`IWindowActivator`** (new, `AdbCore/Window/`): `void Activate(IntPtr handle)`. Injectable so the action is unit-testable with a fake (no real window needed).
- **`Win32WindowActivator`** — the live impl: if the window is minimized (`IsIconic`) call `ShowWindow(handle, SW_RESTORE)`, then `SetForegroundWindow(handle)`. This mirrors the **already-proven** `SetForegroundWindow` call the input senders use (clicks/keys land on the right window), so the mechanism is established in this codebase. P/Invoke declarations mirror `Win32SendInputSender`.

## 4. Auto-target mapping

The editor's `NodeTargetType.For(category)` (merged in #38) currently maps `Screen`/`Input` → `Window`. Extend it with **`"Window"` → `BotTargetType.Window`** so an Activate Window node auto-assigns the lone Window target on add, consistent with the other window-acting nodes.

## 5. Components

- `AdbCore/Window/IWindowActivator.cs` (new) — the interface.
- `AdbCore/Window/Win32WindowActivator.cs` (new) — Win32 impl (IsIconic + ShowWindow + SetForegroundWindow P/Invoke).
- `AdbCore/Actions/BuiltIn/ActivateWindowAction.cs` (new) — the action (ctor takes `IWindowActivator`).
- `AdbCore/Actions/BuiltIn/BuiltInActions.cs` — register `Add(new ActivateWindowAction(new Win32WindowActivator()), definitions, executors)` in the window-acting group (near the Input/Screen registrations).
- `BotBuilder.Core/Targets/NodeTargetType.cs` — add the `"Window"` arm.
- Count bumps: AdbCore registry +1 def / +1 exec; palette gains a **Window** category (1 item); `PaletteViewModelTests` total +1.

No new external deps. No `.bot` schema change. **Conflict-free with parked PRs #37 (canvas/node files + `MainWindow.xaml`) and #39 (`BotEditorViewModel`/`EditorCommands`/`MainWindow.xaml.cs`/`NodeClipboard`)** — none of those files are touched.

## 6. Testing

- **`ActivateWindowActionTests` (AdbCore.Tests):** with a fake `IWindowActivator` capturing the handle —
  - one Window target (boxed `IntPtr`) in the context → `Activate(hwnd)` called with it, result `onSuccess`.
  - no window target → `onFailure` ("requires a window target"), activator not called.
  - `Definition_Metadata`: TypeKey `window.activate`, DisplayName "Activate Window", Category "Window", ports `onSuccess`/`onFailure`, no config fields.
- **Registration test:** `window.activate` resolves in both registries.
- **`NodeTargetTypeTests`:** `"Window"` → `BotTargetType.Window`.
- **Count/palette:** AdbCore `BuiltInActionsTests` +1 def/+1 exec; `PaletteViewModelTests` total +1 and a new Window category (1 item).
- The concrete `Win32WindowActivator` is a thin P/Invoke wrapper (not unit-tested; the `SetForegroundWindow` mechanism is already exercised live by the input actions).

## 7. Out of scope

- A `restoreIfMinimized` config toggle, "minimize"/"close"/"move/resize window" actions (could be future Window-category actions).
- Robust foreground-stealing workarounds (AttachThreadInput, alt-key trick) beyond `ShowWindow`+`SetForegroundWindow` — add only if the simple call proves unreliable in practice.
- Android/Browser equivalents (N/A — this is a desktop-window concept).

## 8. Merge handling

Action logic is fully unit-tested (fake activator) and the concrete activator reuses the codebase's already-proven `SetForegroundWindow` call → built compile-clean + unit-green and **self-merged** via `gh` (per the user's go-ahead). The live focus behavior is worth an eventual hands-on sanity check, but the mechanism is established.
