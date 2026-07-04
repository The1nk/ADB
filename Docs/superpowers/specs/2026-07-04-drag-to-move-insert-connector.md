# Drag to move / insert a connector

**Date:** 2026-07-04
**Status:** Approved — ready for implementation plan

## Problem

Dragging from an output port that is **already connected** is currently a silent no-op: the
`ConnectionValidator` returns `SourcePortOccupied` (`ConnectionValidator.cs:33-36`) and
`BotEditorViewModel.Connect` bails. So to insert a Delay (or any node) between two already-wired nodes,
the user must delete the wire, drag two new wires, and re-aim — tedious for a common edit.

## Goal

Make dragging from an already-connected output port **move** that wire to the node you drop on, and — when
the dropped node has a single free output — **auto-forward** it to the wire's old destination. Dropping a
Delay onto the `A→C` wire then yields `A→Delay→C` in one gesture. One undo restores the original wiring.

## Decisions (locked with the user)

- **Move applies to any already-connected output port** (single-out nodes *and* an `onSuccess`/`onFailure`
  port). Retargeting an occupied port is always allowed (subject to cycle/self checks).
- **Auto-forward fires only when the dropped node has exactly one output port and it is unset.** So
  Delay / Log / Set Variable auto-insert; a Branch or other multi-out node just *receives* the moved wire
  (you wire its outputs yourself). Unambiguous — no port-picker popup.
- When the dropped node's single out **is** already wired (or it has multiple outs), the old destination
  `C` is simply **orphaned** from that path — `A→B` only, `B`'s existing downstream untouched.

## Design

### New editor method — `BotEditorViewModel.ConnectOrMove`

```csharp
public ConnectionError ConnectOrMove(NodeViewModel source, PortViewModel sourcePort,
                                     NodeViewModel target, PortViewModel targetPort)
```

Logic:

1. **Find the existing edge** from the dragged port:
   `existing = Connections.FirstOrDefault(c => ReferenceEquals(c.Source, source) && c.SourcePort.Name == sourcePort.Name)`.
2. **Unoccupied source → delegate to `Connect`** (today's behavior, unchanged) and return its result.
3. **Occupied source → move/insert:**
   - `others = Connections` **excluding** `existing`.
   - **Validate the move** `source.sourcePort → target.targetPort` with
     `ConnectionValidator.Validate(others, source, sourcePort, target, targetPort)`. Excluding `existing`
     means `SourcePortOccupied` no longer trips; the check still rejects **self** (drop on `source`),
     **duplicate** (drop back on the old target `C` via the same ports → no-op), **not-output-to-input**,
     and **cycle**. On any error → **no-op**, return that error (the wire stays put).
   - **Auto-forward decision:** `autoForward` is true iff `target.OutputPorts.Count == 1` **and** that lone
     out has no edge in `others` (i.e. it's unset). Let `singleOut = target.OutputPorts[0]` and
     `oldTarget = existing.Target`, `oldTargetPort = existing.TargetPort`.
   - If `autoForward`, still **validate the forward edge** `target.singleOut → oldTarget.oldTargetPort`
     against `others` **plus the new primary edge** (the post-move graph). If it would cycle, **skip the
     forward** and just move. (Duplicate can't occur — `singleOut` was unset.)
   - Build one **`InsertOrMoveConnectionCommand`** capturing: the edge to remove (`existing`), the primary
     new edge (`source→target`), and the optional forward edge (`target→oldTarget`). `_undo.Execute(...)`
     it, then `AfterEdit()`. Return `ConnectionError.None`.

`Connect` is unchanged and still used by tests / programmatic callers; `ConnectOrMove` wraps it for the
unoccupied case so there is exactly one place that builds a fresh connection.

### New undoable command — `InsertOrMoveConnectionCommand` (`BotBuilder.Core/Undo/EditorCommands.cs`)

Mirrors the existing composite commands (`DeleteNodesCommand`, `PasteCommand`) — internal, atomic:

```csharp
internal sealed class InsertOrMoveConnectionCommand : IUndoableCommand
{
    // ctor(editor, ConnectionViewModel removed, ConnectionViewModel primary, ConnectionViewModel? forward)
    public void Do()
    {
        _editor.RemoveConnectionCore(_removed);
        _editor.AddConnectionCore(_primary);
        if (_forward is not null) _editor.AddConnectionCore(_forward);
    }
    public void Undo()
    {
        if (_forward is not null) _editor.RemoveConnectionCore(_forward);
        _editor.RemoveConnectionCore(_primary);
        _editor.AddConnectionCore(_removed);
    }
}
```

The `ConnectionViewModel`s are constructed in `ConnectOrMove` (fresh `Guid`s for the new edges); the
command only wires them in/out via the existing `AddConnectionCore`/`RemoveConnectionCore` (which
`Attach()`/`Detach()` endpoint subscriptions). Redo re-runs `Do()`.

### WPF wiring — one line

`MainWindow.xaml.cs` `CompleteConnectionDrag` currently calls `_editor.Connect(source, sourcePort,
targetNode, targetPort)`. Change that single call to `_editor.ConnectOrMove(...)`. This covers **both**
the left output-port drag and the right-button card-body drag (they share `CompleteConnectionDrag`). No
other WPF change; the empty-canvas / no-input-port drops remain no-ops (the method is only called when a
target node with an input port is hit).

### What stays a no-op (unchanged)

- Drag from an **unoccupied** port onto empty canvas or a no-input node.
- Occupied-source drag that resolves to **self / duplicate (old target) / cycle** — the wire is left
  exactly as it was.

## Tests (`BotBuilder.Core.Tests`)

Build a small graph with `NodeViewModel`s and connect via the editor. Cases:

- **Insert into a wire** — `A(out)→C`; drag `A`'s out onto `B` (a Delay-like node: one unset out) →
  result is exactly `A→B` and `B→C`; the original `A→C` is gone; `Connections.Count == 2`.
- **Undo restores the original** — after the insert, one `Undo()` → back to a single `A→C`; `Redo()` →
  `A→B→C` again.
- **Dropped node's out already wired → move only** — `B` already has `B→D`; dragging `A→C` onto `B` →
  `A→B` added, `A→C` removed, `B→D` untouched, no `B→C` (C orphaned). `Connections` = {A→B, B→D}.
- **Multi-out dropped node → move only, no forward** — `B` has two outs (e.g. a Branch-like node) →
  `A→B` only, no auto-forward.
- **Drop back on the old target → no-op** — dragging `A→C` onto `C` returns `Duplicate`, graph unchanged.
- **Drop on the source → no-op** — returns `SelfConnection`, graph unchanged.
- **Cycle rejected → no-op** — dragging onto a node whose acceptance would cycle returns
  `WouldCreateCycle`, graph unchanged.
- **Unoccupied source delegates to Connect** — dragging from a free port behaves exactly like `Connect`
  (adds one edge; returns its error codes for invalid drops).
- **Auto-forward skipped when it would cycle** — construct a graph where `target.singleOut → oldTarget`
  would cycle; assert the move happens (`A→B`) but no forward edge is added.

Use fake/real `NodeViewModel`s the way existing `BotBuilder.Core.Tests` connection tests do (mirror their
setup helper). No WPF is exercised — all logic is in `ConnectOrMove` + the command.

## Documentation

- **Wiki** — the editor/canvas page (e.g. `Editor` / `Canvas` how-to, wherever right-drag-to-connect is
  documented): add that dragging an already-connected output onto a node **moves** the wire there and
  auto-inserts single-out nodes (Delay/Log/…) into the wire.
- **README** — the *arsenal* mentions right-drag-to-wire; add a half-clause that you can **drag a wire onto
  a node to insert it** (goblin voice), grounded in this behavior.
- **CLAUDE.md** — no per-interaction table; a one-line note under the editor/canvas description that
  `ConnectOrMove` moves an occupied output and auto-forwards single-out nodes is enough.

## Execution

Subagent-driven development after the plan. `BotBuilder.Core` logic + command + one WPF line + tests. The
core is fully unit-tested, but the **gesture itself is visual** (drag/drop on the canvas), so this is a
**parked** slice: open it for the user's visual verification rather than self-merging.
