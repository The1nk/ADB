# Editor Usability Batch — Design

**Date:** 2026-07-04
**Status:** Approved (design), pending implementation plan

Five editor-usability improvements, brainstormed together, shipping as **two** units of work:

- **Plan A (backend-only, self-merge):** items 1 + 2.
- **Plan B (visual, one PR parked for user review):** items 3 + 4 + 5.

| # | Item | Layer | Ships in |
|---|------|-------|----------|
| 1 | Nested-bot selection-clearing bug fix | `BotBuilder.Core` | Plan A (self-merge) |
| 2 | "Throw Error" action | `AdbCore` | Plan A (self-merge) |
| 3 | Serpentine "Tidy Up" overhaul | `BotBuilder.Core` + WPF + `.bot` schema | Plan B (parked PR) |
| 4 | Collapsible toolbox / properties panels | WPF + settings | Plan B (parked PR) |
| 5 | Unsaved-changes prompt on New/Open/Exit | `BotBuilder.Core` + WPF | Plan B (parked PR) |

Items 3, 4, 5 are independent of each other and of 1/2; they are bundled into a single PR only for
review convenience.

---

## 1 · Nested-bot selection-clearing bug fix

### Problem
Double-click a Nested Bot card to open its child editor, close the child editor, then click a
**different** Nested Bot card — that card's assignment clears to "No bot assigned".

### Root cause
The properties-panel picker binds:

```xml
<ComboBox ItemsSource="{Binding NestedBotEntries}" DisplayMemberPath="Name"
          SelectedValuePath="Id" SelectedValue="{Binding SelectedNestedBotId}" />
```

`NestedBotEntries` returns a fresh `.ToList()` each read. Switching from card A to card B swaps the
`ItemsSource`; WPF normally preserves the selection because A's `Bot` object is still present *by
reference* in the new list. But closing the child editor runs
`NestedBotEditorSession.SyncBack()` → `NestedBotLibrary.Replace()` → `_entries[i] = updated`, a
**brand-new `Bot` instance** (same `Id`). Now A's old reference is absent from the new list, so on the
next selection change WPF resets `SelectedValue` to `null` and writes that null back through the
two-way binding — which is already re-pointed at card B — clearing B's config key. This is exactly why
it only happens after editing a nested bot, and only on the next click to a different card.

### Fix
The picker has no "unassigned" item, so a `null` arriving through the binding is *always* spurious.
In `PropertiesViewModel.SelectedNestedBotId`'s setter, treat `null` as a no-op that snaps the picker
back via `OnPropertyChanged(nameof(SelectedNestedBotId))` — identical to the existing cycle-guard
(`PropertiesViewModel.cs:69`). Because the setter will no longer clear on null,
`RemoveSelectedNestedBot()` is rerouted to remove the config key directly (it legitimately unassigns)
and raise the same change notifications.

Scope note: the sibling target picker (`SelectedTargetId`) is unaffected — targets are not replaced by
reference on edit, so the null-writeback path is never triggered there.

### Testing (`.Core`)
- Setter receiving `null` → config key survives, change notification raised (picker snaps back).
- Setter receiving a valid `Guid` → assigns as before.
- `RemoveSelectedNestedBot()` → clears the key and notifies.

---

## 2 · "Throw Error" action

### Purpose
Force a failure that propagates out of the current flow — escaping an enclosing Loop and returning
control from a nested bot to its parent. `End` cannot do this: it returns `Ok` and merely dead-ends the
current path, so inside a Loop body the loop simply continues to the next iteration.

### Behavior
A terminal Control-Flow leaf: one input port, **no** output ports. Its executor returns
`ActionResult.Fail(message)`. With no `onFailure` port the failure always propagates:

- **Loop:** `LoopControlFlowExecutor` halts the loop on any body failure and returns the outcome up
  (`LoopControlFlowExecutor.cs:39,79`) — the error escapes the loop.
- **Nested bot:** `NestedBotExecutor` converts the child's failed `ExecutionResult` into the card's
  `ActionResult.Fail`, so control returns to the parent's failure handling.
- **Error Handler:** if the enclosing bot has an Error Handler node, the thrown error is caught there
  (consistent with the existing error model); otherwise it bubbles. This is the intended "exit the
  nested bot" for the common case (nested bot with no local Error Handler).

### Shape
- `TypeKey`: `control.throwError`
- `DisplayName`: `Throw Error`
- `Category`: `Control Flow`
- `InputPorts`: one `in`; `OutputPorts`: none
- `ConfigFields`: one `message` (string, default `"Bot threw an error."`). Interpolation of `${var}`
  tokens is automatic (`BotExecutor.ExecuteWithRetryAsync` resolves config before execution).
- `SupportsRetry`: false
- Implemented as `IActionDefinition` + `IActionExecutor` (like `EndAction`), registered in
  `BuiltInActions.Register`.

### Testing (`.Core`) + docs
- Executor returns `Fail` with the (interpolated) message.
- Registration test: present in the registry under `control.throwError`.
- Docs sync: CLAUDE.md action table, README, wiki `Control-Flow.md` + `Actions-Reference.md`.

---

## 3 · Serpentine "Tidy Up" overhaul

### Problem
"Tidy Up" output is too wide and its band-to-band return wires fly the full width and hide behind
cards; some sibling wires cross. Today `AutoLayout` wraps a long flow into stacked bands that all run
left→right, so each band's end must connect back across the whole width to the next band's start.

### Design
Four coordinated changes.

**a) Serpentine banding.** `AutoLayout` alternates band direction: band 0 left→right, band 1
right→left, band 2 left→right, … The connector between consecutive bands becomes a short vertical drop
instead of a full-width return wire.

**b) Per-band port flip.** For a right→left band to render cleanly, its nodes must draw
**output-left / input-right** (otherwise every forward step reads as backward in X and the renderer
routes it as an ugly gutter detour). This is captured as a new per-node boolean `PortsFlipped`, set by
`AutoLayout` for reversed-band nodes. `PortViewModel.Edge` becomes a function of `PortsFlipped` + port
kind: unflipped → inputs Left / outputs Right / failures Bottom (today's behavior); flipped → inputs
Right / outputs Left / failures Bottom.

`PortsFlipped` is **persisted** in the `.bot` file as an optional per-action field (default `false`,
back-compatible; only one reader/writer exists) so a saved-then-reloaded tidy graph stays clean without
re-running Tidy Up. Serializer stays camelCase (`portsFlipped`).

**c) Direction-aware routing.** `ConnectionGeometry` (and `ConnectionViewModel.PathData`) decide
clean-curve vs. gutter back-route by the source port's **outward direction** relative to the target,
not raw X. A flipped-band forward edge (output-left, target to the left) then curves cleanly; only true
back-edges (loop-backs, error-returns) take the existing laned gutter route
(`BuildLanedBackRoute` / `BackRoutePlanner`), unchanged.

**d) Compactness + fit-to-width.** Tighten column pitch (`ColGap` ~240→~200) and the inter-band gutter
(still wide enough for the short serpentine connector plus any back-edge lane). `AutoLayout.Arrange`
gains an optional `targetWidth`: when supplied, band width (columns-per-band `k`) is the largest that
fits `targetWidth` (minimum 1); when null, it falls back to today's `TargetAspect` heuristic (keeps
existing tests and any headless caller working). `MainWindow` passes the canvas host's `ActualWidth`
(already the region between the side panels) when invoking Tidy Up, so re-running after a window resize
or a panel collapse re-wraps to the new budget. Layout works in world coordinates, so "fits the width"
means fits at 100% zoom.

Barycenter crossing-reduction is retained and adjusted so flipped-port bands do not *introduce* new
crossings. Layered layout cannot guarantee zero crossings in dense sub-graphs, but the flagged case
should resolve.

### Components touched
- `BotBuilder.Core/Layout/AutoLayout.cs` — serpentine banding, `PortsFlipped` output, `targetWidth`,
  tighter constants.
- `NodeViewModel` / model — `PortsFlipped` property; `PortViewModel.Edge` derives from it.
- `AdbCore` model + `BotSerializer` — persist `portsFlipped`.
- `BotBuilder.Core/Connections/ConnectionGeometry.cs` + `ConnectionViewModel` — direction-aware
  forward/back decision.
- `BotEditorViewModel.AutoLayout()` — accept `double? availableWidth`; `MainWindow` supplies canvas
  `ActualWidth`.

### Testing
- `AutoLayout` (`.Core`, pure): band parity/direction, `PortsFlipped` set on reversed bands only,
  fit-to-width band selection (largest k ≤ width, min 1), aspect fallback when width null.
- `ConnectionGeometry` (`.Core`, pure): forward curve vs. back-route chosen by port direction for both
  flipped and unflipped cases.
- Serializer round-trip of `portsFlipped`.
- Visual result (WPF) parked for user sign-off.

---

## 4 · Collapsible toolbox / properties panels

### Design
The editor is a 3-column grid (palette 220px | canvas `*` | properties 240px). Each side panel gets a
collapse toggle. Collapsed → the column shrinks to a ~20px labelled **rail** with a chevron to
re-expand; the `*` canvas column absorbs the freed width. Chosen style: thin rail (always-visible,
discoverable affordance).

- **Persistence:** add `ToolboxCollapsed` and `PropertiesCollapsed` bools to `AppSettings`,
  round-tripped through `JsonSettingsStore`. Both writers (BotBuilder, BotCapture) must preserve each
  other's fields (`Load() with { … }`), per the settings contract.
- **Shortcuts:** a keyboard toggle for each panel (proposed `Ctrl+[` toolbox / `Ctrl+]` properties;
  finalized in the plan) plus View-menu items.
- **Child windows:** nested-bot editor windows are the same `MainWindow`, so they inherit collapse.
- **Synergy:** collapsing widens the canvas; a fit-to-width Tidy Up re-run (item 3) uses the extra
  room automatically.

### Testing
- Settings round-trip of the two new bools (`.Core`).
- Collapse UI (WPF) parked for user sign-off.

---

## 5 · Unsaved-changes prompt on New / Open / Exit

### Problem
`New_Click` and `Open_Click` act immediately, and there is no window-closing handler, so New/Open and
closing the window (X / Alt+F4) silently discard unsaved work.

### Design
Guard the three entry points — `New`, `Open`, and window close — on the **root** window only. `Ctrl+N`
/ `Ctrl+O` already route through `New_Click` / `Open_Click`. Child (nested-bot) editor windows
auto-`SyncBack()` into the parent library on close (never lost within the session), so they do not
prompt.

When `_editor.IsDirty`, show a standard 3-button dialog:
- **Save** → run existing Save (prompts for a path if the bot was never saved). If it succeeds,
  proceed; if the path prompt is cancelled, abort.
- **Don't Save** → proceed, discarding.
- **Cancel** → abort (on close, set `e.Cancel = true`).

### Structure (testable-core pattern)
A small `UnsavedChangesGuard` in `BotBuilder.Core` orchestrates the outcome from three injected
collaborators: an `IsDirty` check, a "which button?" delegate returning Save/DontSave/Cancel, and a
`Save`-returns-bool delegate. It returns proceed vs. abort. `MainWindow` supplies the real `MessageBox`
and Save action and calls the guard from `New_Click`, `Open_Click`, and the new `OnClosing` override.

### Testing
- Guard (`.Core`): not-dirty → proceed without prompting; Save→succeeds → proceed; Save→cancelled →
  abort; Don't-Save → proceed; Cancel → abort.
- Dialog wiring (WPF) parked for user sign-off.
