# Node Copy / Paste — Design

**Status:** Approved (M9 Polish — editor productivity)
**Context:** The editor has no way to duplicate work. This adds copy/paste of the selected node(s) — with the connections *between* them — via an in-app clipboard, undoable as a single step, triggered by Ctrl+C / Ctrl+V.

---

## 1. Behavior

- **Copy (Ctrl+C):** snapshots the currently selected nodes (`Nodes.Where(IsSelected)`, falling back to `SelectedNode` if the set is empty) into an in-app clipboard, plus every connection whose *both* endpoints are in the selection (internal connections). Connections to non-selected nodes are not copied. Copy with nothing selected is a no-op.
- **Paste (Ctrl+V):** creates fresh nodes (new `Id`s) from the clipboard, offset by a small delta (`+24,+24`) from the originals, re-creates the internal connections among them (matched by port name), adds it all as one undoable step, and selects the pasted nodes. Paste with an empty clipboard is a no-op. Paste survives across `Copy` of a different selection (clipboard is replaced on each copy).
- **Undo:** a single Ctrl+Z removes all pasted nodes + connections; redo re-adds them (mirrors `DeleteNodesCommand`).

Within-document, in-app clipboard (not the OS clipboard) — simple, lossless, and sufficient for v1. Cross-process paste / OS-clipboard serialization is out of scope.

## 2. Snapshot model (in `BotBuilder.Core`)

```csharp
internal sealed record NodeClip(string TypeKey, string Label, Guid? TargetId,
    int RetryMaxAttempts, int RetryDelayMs, Dictionary<string, object> Config, double X, double Y);
internal sealed record ConnectionClip(int SourceIndex, string SourcePort, int TargetIndex, string TargetPort);
internal sealed record NodeClipboard(IReadOnlyList<NodeClip> Nodes, IReadOnlyList<ConnectionClip> Connections);
```
A `NodeClipboard?` field on `BotEditorViewModel` holds the last copy. `ConnectionClip` stores endpoints as **indices into the snapshot's node list** (so they rebind to the pasted nodes) plus the port **names**.

## 3. Cloning rules

A pasted node = `NodeViewModel.FromDefinition(def, newId, clip.Label, clip.X+24, clip.Y+24)`, then copy `TargetId`, `RetryMaxAttempts`, `RetryDelayMs`, and a fresh copy of `Config`. For a Run Parallel node, after restoring `Config` call `SetBranchPortCount(branches)` so its branch output ports match (port names `branch1..N`) for connection re-binding. Connections are re-created by looking up the pasted source node's **output** port and target node's **input** port by name (`FromDefinition`/`SetBranchPortCount` reproduce identical port names, so the lookup always resolves for a faithful copy).

## 4. Components

- **`BotBuilder.Core/NodeClipboard.cs`** (new) — the three snapshot records.
- **`BotBuilder.Core/BotEditorViewModel.cs`** — `CopySelection()` and `Paste()` methods + the `_clipboard` field. `Paste()` wraps the new nodes/connections in a new `PasteCommand` and calls `SelectNodes(pasted)`.
- **`BotBuilder.Core/Undo/EditorCommands.cs`** — `PasteCommand` (Do: add nodes then connections; Undo: remove connections then nodes — mirrors `DeleteNodesCommand`).
- **`BotBuilder/MainWindow.xaml.cs`** — in the existing `Window_KeyDown`, add `Ctrl+C → _editor.CopySelection()` and `Ctrl+V → _editor.Paste()` (alongside the existing Ctrl+Z/Ctrl+Y/Delete cases).

No new external deps, no `.bot` schema change. **Conflict-free with PR #36** (`AdbCore/Execution` + action bases) **and PR #37** (`BotBuilder.Core` canvas/node files: `NodeViewModel`/`NodeLayout`/`ConnectionGeometry`/`ConnectionViewModel`/`MarqueeSelection`/`PortViewModel`/`PortEdge`, and `MainWindow.xaml`) — paste only *calls* `FromDefinition`/`SetBranchPortCount`/`ConnectionViewModel` (doesn't edit those files), and the keybinding lives in `MainWindow.xaml.cs` (not `.xaml`).

## 5. Testing (BotBuilder.Core.Tests — deterministic)

- Copy one selected node → Paste → a new node exists with same `TypeKey`/`Config`/`TargetId`/retry, a new `Id`, position offset by (24,24); original untouched; `Nodes.Count` +1.
- Copy two connected selected nodes → Paste → two new nodes + one new connection **between the pasted pair** (not touching the originals); the pasted connection's ports match by name.
- A connection from a selected node to a *non-selected* node is **not** copied.
- Copy/paste a Run Parallel node (branches=3) → pasted node has 3 branch output ports.
- Paste with empty clipboard → no-op (no nodes added, no throw).
- Pasted nodes are selected (`IsSelected`), original deselected; Paste is a single undo step (one Undo removes all pasted nodes+connections).
- Config copy is a deep copy (mutating the pasted node's Config doesn't affect the original).

## 6. Out of scope

- OS-clipboard / cross-process paste; paste-at-cursor (fixed offset for v1); duplicate-in-place shortcut (Ctrl+D) — can follow.
- Menu items (Edit ▸ Copy/Paste) — deferred so the slice doesn't touch `MainWindow.xaml` (a PR #37 file); keyboard shortcuts only for now.

## 7. Merge handling

Core logic unit-tested in `BotBuilder.Core.Tests`, but the payoff (the keyboard interaction + canvas result) is an editor UX behavior the user will want to exercise → opened as a PR and **user-verified + merged**, not self-merged. Independent of PRs #36/#37 — no shared files.
