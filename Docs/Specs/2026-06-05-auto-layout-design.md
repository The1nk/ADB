# Auto-Layout ("Tidy Up") — Design

**Status:** Approved (user green-lit 2026-06-05; M9 Polish / adb-autolayout-idea)
**Context:** Bots become a tangle of nodes/connectors as they grow. A one-click "Tidy Up" auto-arranges the graph into a clean left-to-right layered layout (which matches the canvas's flow — outputs exit right, inputs enter left).

---

## 1. Behavior

A **Tidy Up** command repositions every node into a layered left-to-right arrangement: nodes flow in columns by their distance from the entry, each column packed top-to-bottom. It's one **undoable** step (a single Ctrl+Z restores the prior positions), invoked from a menu item / button. Connections re-route automatically (they already follow node moves).

## 2. Algorithm (pure, `BotBuilder.Core/Layout/AutoLayout.cs`)

`AutoLayout.Arrange(nodes, edges) → IReadOnlyDictionary<Guid, (double X, double Y)>` where `nodes` = `(Guid Id, double Height)[]` and `edges` = `(Guid Source, Guid Target)[]` (node-to-node, derived from connections). Pure — no view-model dependency — so it's fully unit-testable.

Steps (Sugiyama-style layer assignment, simplified for v1):
1. **Cycle removal:** bot graphs can loop (Loop/Branch back-edges). DFS over the forward edges from each unvisited node; an edge to a node currently on the DFS stack is a **back-edge** and is excluded from the layering DAG (so layering always terminates).
2. **Layer assignment (longest path):** on the back-edge-removed DAG, process nodes in topological order (Kahn); `layer[v] = max(layer[u] + 1)` over forward edges `u→v`; a node with no forward in-edges (a root / isolated node) → layer 0.
3. **Within-layer order:** group nodes by layer; within each layer keep a **stable deterministic order** (by the node's index in the input list) for v1. (Barycenter crossing-minimization is a later refinement.)
4. **Positions:** column `L` is at `X = OriginX + L * ColGap`. Within a column, pack nodes top-to-bottom **accounting for height**: `Y = OriginY + runningOffset`, then `runningOffset += height + RowGap`. So tall nodes (grown Run Parallel cards) don't overlap their neighbors.

Constants: `ColGap = 240`, `RowGap = 30`, `OriginX = 40`, `OriginY = 40` (tunable; chosen so a 160px-wide card has clear horizontal/vertical gaps).

Edge cases: empty graph → empty result; isolated nodes (no edges) → layer 0, packed in the first column; disconnected components → their roots share layer 0 (acceptable). The algorithm must terminate on any graph (cycles handled by step 1).

## 3. Editor wiring

- **`BotEditorViewModel.AutoLayout()`** — derives `nodes` (`Id`, `Height`) + `edges` (from `Connections`: `c.Source.Id → c.Target.Id`), calls `AutoLayout.Arrange`, and applies the result as **one undoable `MoveNodesCommand`** (the same command copy/paste's multi-drag uses, already on main): records each node's old position + the computed new position; no-op if nothing actually moves. Then `AfterEdit()`.
- **WPF trigger** (`BotBuilder/MainWindow.xaml` menu + `MainWindow.xaml.cs`): an "Arrange ▸ Tidy Up" menu item (and/or a toolbar button) whose handler calls `_editor.AutoLayout()`.

## 4. Components

- `BotBuilder.Core/Layout/AutoLayout.cs` (new) — the pure algorithm.
- `BotBuilder.Core/BotEditorViewModel.cs` — `AutoLayout()` method.
- `BotBuilder/MainWindow.xaml` + `MainWindow.xaml.cs` — the menu/button trigger.

No new deps, no `.bot` schema change. **Conflict-free** with everything now on main (PRs #37/#39 merged); reuses the merged `MoveNodesCommand`.

## 5. Testing (`BotBuilder.Core.Tests` — deterministic)

- **AutoLayout (pure):**
  - A linear chain a→b→c → layers 0,1,2 → strictly increasing X; same column has one node.
  - A fan-out (a→b, a→c) → b and c in the same column (layer 1), different Y (packed, non-overlapping; Y gap ≥ height).
  - A diamond (a→b, a→c, b→d, c→d) → d at layer 2 (longest path), not layer 1.
  - A **cycle** (a→b, b→a) → terminates, produces finite layers (assert it returns positions for both, doesn't hang).
  - Height-aware packing: two nodes in a column with heights 70 and 110 → second node's Y ≥ first.Y + 70 + RowGap.
  - Isolated node → placed at the origin column.
- **Editor `AutoLayout()`:** build a small graph, call it, assert nodes moved into layered positions; **one `Undo` restores all** original positions; a no-op (already arranged) pushes nothing.

The WPF menu/button wiring has no unit test → user visual verify.

## 6. Out of scope

- Crossing minimization (barycenter/median ordering), edge routing, vertical centering of columns, "layout selection only", animation. v1 is a clean deterministic layered arrangement.
- Top-to-bottom or radial layouts (left-to-right matches the canvas flow).

## 7. Merge handling

Algorithm fully unit-tested, but the payoff (a tidy canvas) is **visual** → opened as a PR and **user-verified + merged**, not self-merged.
