# Auto-Target on Node-Add — Design

**Status:** Approved (Polish item b, editor half — companion to PR #36's runtime half)
**Context:** When you drop a node onto the canvas it has no target assigned. PR #36 makes the *runtime* default an unassigned node to the lone target of its type, but the *editor* gives no design-time feedback — the node shows no target badge / no pre-fill. This adds the design-time convenience: on node-add, if the node needs a target type and exactly one target of that type exists, pre-assign it.

---

## 1. Behavior

In `BotEditorViewModel.AddNode(typeKey, x, y)`, after building the node from its definition, set `node.TargetId` to the **single** target whose `Type` matches the node's required target type — when exactly one such target exists. Otherwise leave it unassigned (`null`).

- Exactly one matching-type target → assigned.
- Zero, or two-or-more, matching-type targets → unassigned (ambiguous / nothing to pick).
- Target-agnostic node (Control Flow / Data / Scripting) → never assigned.
- Only fires on **add** (palette drop). Load preserves the saved `TargetId`; this does not retro-assign existing nodes or fire when a target is added later.

The assignment happens on the fresh node before it is handed to the existing undoable `AddNodeCommand`, so it is naturally part of the single add/undo step (undo removes the whole node, target and all).

## 2. Node → target-type mapping

Derived from the action's `Category` (the design-time signal; the runtime resolves by handle type per #36):

| Category | Target type |
|---|---|
| `Android` | `BotTargetType.AndroidDevice` |
| `Browser` | `BotTargetType.Browser` |
| `Screen` | `BotTargetType.Window` |
| `Input` | `BotTargetType.Window` |
| `Control Flow` / `Data` / `Scripting` (anything else) | none (target-agnostic) |

Both `Screen` and `Input` actions resolve to a window HWND (`ResolveWindow`), so both map to `Window`. Centralized in a small pure helper `NodeTargetType.For(string category) → BotTargetType?` so it's one place to maintain and unit-test.

## 3. Components

- **`BotBuilder.Core/Targets/NodeTargetType.cs`** (new) — `static BotTargetType? For(string category)` with the table above.
- **`BotBuilder.Core/BotEditorViewModel.cs`** — in `AddNode`, compute and set `node.TargetId` via a private `AutoTargetFor(string category)` that returns the lone matching-type target's `Id` (or `null`). Uses `TargetBar.Targets` (each a `TargetViewModel` with `Id` + `Type`).

No new external deps. No `.bot` schema change (just pre-fills an existing field). Independent of PR #36 (`AdbCore/Execution` + action bases) and PR #37 (`BotBuilder.Core` canvas/node files + `MainWindow.xaml`) — touches neither's files.

## 4. Testing (BotBuilder.Core.Tests — deterministic)

- `NodeTargetType.For`: Android→AndroidDevice, Browser→Browser, Screen→Window, Input→Window, "Control Flow"/"Data"/"Scripting"/unknown→null.
- `AddNode` (build an editor as existing tests do, seed `TargetBar.Targets`):
  - One AndroidDevice target + add an Android-category node → node.TargetId == that target.
  - One Window target + add a Screen node and an Input node → both get that target.
  - Two AndroidDevice targets + add an Android node → TargetId null (ambiguous).
  - One AndroidDevice target + add a Window/Screen node → null (no matching-type target).
  - One target of some type + add a Control Flow node (e.g. Delay/Branch) → null (target-agnostic).
  - No targets + add any node → null (no crash).

## 5. Out of scope

- Re-assigning when a target is added after the node, or retro-assigning existing nodes on load.
- A user setting to disable auto-assign (YAGNI).
- Changing the ambiguous (≥2) case to pick one — intentionally left unassigned (runtime #36 still resolves a single-of-type at run; the editor just doesn't guess among several).

## 6. Merge handling

Pure editor-VM logic in `BotBuilder.Core` with deterministic unit tests, no rendering/visual surface (it sets a `Guid?` field) → built compile-clean + unit-green and **self-merged** via `gh` (backend-slice rule). The badge rendering it feeds is already covered by `RefreshTargetBadges`.
