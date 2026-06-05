# Run Parallel — Dynamic Branch Ports — Design

**Status:** Approved
**Date:** 2026-06-04
**Type:** Bug fix (focused slice; not a milestone)

---

## 1. Problem

The Run Parallel node's `Branches` config field has no effect. The node always renders exactly two output ports (`branch1`/"Branch 1", `branch2`/"Branch 2"), hardcoded in `RunParallelAction.OutputPorts`. Because each output port accepts only one connection (`ConnectionValidator` rejects an occupied source), the author can wire at most two parallel branches regardless of the Branches value, so >2-client parallel bots are impossible to build.

**The engine is already correct:** `BotExecutor.ExecuteParallelAsync` reads `branchCount = Max(1, GetInt(config, "branches", 2))` and fans out to wired ports `branch1..branchN`. The defect is entirely in the node's static port list and the surrounding editor/load/undo plumbing.

## 2. Goal

The Run Parallel node renders `branch1..branchN` output ports driven by its per-node `Branches` config (minimum 2), so the author can wire N parallel branches. No engine change.

## 3. Approach

Config-driven output ports, **scoped to Run Parallel only** (it is the sole node needing this — no general dynamic-port framework, per YAGNI), implemented through a clean static port builder rather than scattered TypeKey checks.

### 3.1 Port source
`RunParallelAction.OutputPortsForBranches(int count) → List<PortDefinition>` returns `branch1..branchN` (labels "Branch 1".."Branch N"), reusing the existing `BranchPort(i)` helper. The static `OutputPorts` property is replaced by (or delegates to) `OutputPortsForBranches(DefaultBranchCount)` so the default is still 2.

### 3.2 Observable, reconcilable ports
`NodeViewModel.OutputPorts` changes from get-only `IReadOnlyList<PortViewModel>` to `ObservableCollection<PortViewModel>` (the canvas already binds it via an `ItemsControl` at `MainWindow.xaml:245`, so add/remove re-renders the port dots automatically). A method `SyncOutputPorts(IReadOnlyList<PortDefinition> desired)` reconciles **by delta**:
- Keeps the existing `PortViewModel` instances for ports that remain (so existing connections' `SourcePort` references and `AnchorOffset`s stay valid).
- Appends new `PortViewModel`s (with `NodeLayout.OutputAnchor(index)`) when growing.
- Removes trailing `PortViewModel`s when shrinking.

A newly created Run Parallel node starts at the default (2 ports) — unchanged behavior for every other node.

### 3.3 Undoable branch-count change
A `SetBranchCountCommand` (BotBuilder.Core undo stack) makes a Branches change atomic and reversible. It captures the old count, the new count (clamped ≥ 2), and the connections removed by a shrink.
- **Apply:** write `branches` into the node config, `SyncOutputPorts` to N, and detach+remove every connection whose `SourcePort` is a dropped branch port.
- **Undo:** restore the old count (re-sync ports) and re-add the removed connections.
- **Redo:** re-apply.

This realizes the chosen "auto-delete orphaned wires, undoable" behavior and keeps the model self-consistent under Ctrl+Z/Y.

### 3.4 Properties-panel routing
Only the Run Parallel `branches` field routes its committed edit through `SetBranchCountCommand` (via the editor). The Number field commits on `LostFocus` (existing binding), so one command per committed edit. All other config fields and all other actions keep the existing direct-write path.

### 3.5 Load path
`DocumentMapper` (load) syncs a Run Parallel node's output ports to its saved `branches` count **after** the node's config is restored and **before** connections are wired. Without this, loading a saved bot with >2 branches would silently drop the `branch3+` connections (their source ports wouldn't exist yet).

### 3.6 Clamp
Branches is clamped to a minimum of 2 in the command (a single-branch "parallel" is just a sequential step; the engine clamps ≥1, but the UI floor is 2). No hard maximum.

## 4. Components

- `AdbCore/Actions/BuiltIn/RunParallelAction.cs` — `OutputPortsForBranches(int)`; default `OutputPorts` = 2 via that helper.
- `BotBuilder.Core/NodeViewModel.cs` — `OutputPorts` → `ObservableCollection<PortViewModel>`; `SyncOutputPorts(...)`.
- `BotBuilder.Core/Undo/EditorCommands.cs` (or a new file) — `SetBranchCountCommand`.
- `BotBuilder.Core/BotEditorViewModel.cs` — entry point that applies a branch-count change via the command and exposes the orphaned-connection removal.
- `BotBuilder.Core/Properties/PropertiesViewModel.cs` (and/or `ConfigFieldViewModel`) — route the Run Parallel `branches` field through the command.
- `BotBuilder.Core/DocumentMapper.cs` — sync branch ports on load before wiring connections.

## 5. Testing

Nearly all logic is BotBuilder.Core / AdbCore unit-testable:
- `OutputPortsForBranches(n)` returns n correctly named ports; default OutputPorts = 2.
- `SyncOutputPorts` grow (appends, preserves instances 1..old), shrink (drops trailing, preserves survivors).
- `SetBranchCountCommand` apply/undo/redo: count value, port count, and orphaned-connection removal + restoration (including endpoint detach/attach).
- Branches clamps to ≥ 2.
- Load: a serialized Run Parallel with `branches=5` and 5 branch connections reconstructs 5 ports and re-links all 5 connections.

Visual verification (user, before merge): the canvas shows N ports; wiring `branch3+` works; lowering Branches deletes those wires; Ctrl+Z restores count + wires; save→reload round-trips a 5-branch bot.

## 6. Out of scope

- Generalizing dynamic ports to other node types (none require it).
- Any change to `BotExecutor` / the parallel engine (already correct).

## 7. Merge handling

Has a canvas/visual surface → **not** self-merged. Built to compile-clean + unit-green, opened as a PR, and **parked for the user** to visually verify and merge on their own schedule (see [[adb-park-pr-move-on]]). Work proceeds to the next independent item (M11 OCR) meanwhile.
