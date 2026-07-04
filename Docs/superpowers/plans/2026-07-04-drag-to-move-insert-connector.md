# Drag to move / insert a connector — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dragging from an already-connected output port moves the wire to the dropped node; if that node has one unset output, auto-forward it to the wire's old destination (insert-into-wire). One undoable step.

**Architecture:** New `BotEditorViewModel.ConnectOrMove` decides move-vs-connect and builds one atomic `InsertOrMoveConnectionCommand` (remove old edge + add primary + optional forward), validating via the existing `ConnectionValidator` against the graph minus the old edge. The WPF drag-completion calls `ConnectOrMove` instead of `Connect` (one line). All logic is unit-tested in `BotBuilder.Core.Tests`.

**Tech Stack:** C# / .NET 10, WPF, xUnit, CommunityToolkit.Mvvm.

**Spec:** `Docs/superpowers/specs/2026-07-04-drag-to-move-insert-connector.md`

**Execution note:** The gesture is a canvas drag/drop → **parked** slice (open for the user's visual verification, do NOT self-merge). Core logic is fully unit-tested.

---

## File map

| File | Change |
| --- | --- |
| `BotBuilder.Core/Undo/EditorCommands.cs` | Add `InsertOrMoveConnectionCommand` |
| `BotBuilder.Core/BotEditorViewModel.cs` | Add `ConnectOrMove` method |
| `BotBuilder.Core.Tests/EditorConnectOrMoveTests.cs` | **Create** — all move/insert cases |
| `BotBuilder/MainWindow.xaml.cs` | `CompleteConnectionDrag`: `Connect` → `ConnectOrMove` |
| `CLAUDE.md`, `README.md`, wiki | Docs |

---

## Task 1: ConnectOrMove + InsertOrMoveConnectionCommand (TDD)

**Files:**
- Create: `BotBuilder.Core.Tests/EditorConnectOrMoveTests.cs`
- Modify: `BotBuilder.Core/Undo/EditorCommands.cs`
- Modify: `BotBuilder.Core/BotEditorViewModel.cs`

- [ ] **Step 1: Write the failing tests**

Create `BotBuilder.Core.Tests/EditorConnectOrMoveTests.cs`:

```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using BotBuilder.Core.Connections;
using Xunit;

namespace BotBuilder.Core.Tests;

public class EditorConnectOrMoveTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    // data.log nodes have one input ("in") and one output ("out").
    private static NodeViewModel Log(BotEditorViewModel e, double x = 0) => e.AddNode("data.log", x, 0);

    private static ConnectionViewModel? Edge(BotEditorViewModel e, NodeViewModel from, NodeViewModel to)
        => e.Connections.FirstOrDefault(c => ReferenceEquals(c.Source, from) && ReferenceEquals(c.Target, to));

    [Fact]
    public void Occupied_DropOnSingleUnsetOutNode_InsertsIntoTheWire()
    {
        var e = NewEditor();
        var a = Log(e, 0); var b = Log(e, 200); var c = Log(e, 400);
        e.Connect(a, a.OutputPorts[0], c, c.InputPorts[0]); // A -> C

        var r = e.ConnectOrMove(a, a.OutputPorts[0], b, b.InputPorts[0]); // drag A's out onto B

        Assert.Equal(ConnectionError.None, r);
        Assert.Equal(2, e.Connections.Count);
        Assert.NotNull(Edge(e, a, b)); // A -> B
        Assert.NotNull(Edge(e, b, c)); // B -> C (auto-forwarded)
        Assert.Null(Edge(e, a, c));    // original A -> C gone
    }

    [Fact]
    public void Insert_IsOneUndoableStep()
    {
        var e = NewEditor();
        var a = Log(e, 0); var b = Log(e, 200); var c = Log(e, 400);
        e.Connect(a, a.OutputPorts[0], c, c.InputPorts[0]);

        e.ConnectOrMove(a, a.OutputPorts[0], b, b.InputPorts[0]);
        e.Undo();

        Assert.Single(e.Connections);
        Assert.NotNull(Edge(e, a, c)); // back to A -> C

        e.Redo();
        Assert.Equal(2, e.Connections.Count);
        Assert.NotNull(Edge(e, a, b));
        Assert.NotNull(Edge(e, b, c));
    }

    [Fact]
    public void Occupied_DropOnNodeWhoseOutIsWired_MovesOnly_OrphansOldTarget()
    {
        var e = NewEditor();
        var a = Log(e, 0); var b = Log(e, 200); var c = Log(e, 400); var d = Log(e, 600);
        e.Connect(a, a.OutputPorts[0], c, c.InputPorts[0]); // A -> C
        e.Connect(b, b.OutputPorts[0], d, d.InputPorts[0]); // B -> D (B's out already used)

        var r = e.ConnectOrMove(a, a.OutputPorts[0], b, b.InputPorts[0]);

        Assert.Equal(ConnectionError.None, r);
        Assert.NotNull(Edge(e, a, b)); // moved
        Assert.NotNull(Edge(e, b, d)); // untouched
        Assert.Null(Edge(e, a, c));    // A -> C removed
        Assert.Null(Edge(e, b, c));    // NOT forwarded (B's out was occupied)
        Assert.Equal(2, e.Connections.Count);
    }

    [Fact]
    public void Occupied_DropOnMultiOutNode_MovesOnly_NoForward()
    {
        var e = NewEditor();
        var a = Log(e, 0); var c = Log(e, 400);
        var branch = e.AddNode("control.branch", 200, 0); // two outs: true/false
        e.Connect(a, a.OutputPorts[0], c, c.InputPorts[0]);
        Assert.True(branch.OutputPorts.Count >= 2); // guard the premise

        var r = e.ConnectOrMove(a, a.OutputPorts[0], branch, branch.InputPorts[0]);

        Assert.Equal(ConnectionError.None, r);
        Assert.NotNull(Edge(e, a, branch)); // moved
        Assert.Null(Edge(e, a, c));         // removed
        Assert.Single(e.Connections);       // no auto-forward from a 2-out node
    }

    [Fact]
    public void Occupied_DropOnOldTarget_IsNoOp_Duplicate()
    {
        var e = NewEditor();
        var a = Log(e, 0); var c = Log(e, 400);
        e.Connect(a, a.OutputPorts[0], c, c.InputPorts[0]);

        var r = e.ConnectOrMove(a, a.OutputPorts[0], c, c.InputPorts[0]); // drop back on C

        Assert.Equal(ConnectionError.Duplicate, r);
        Assert.Single(e.Connections);
        Assert.NotNull(Edge(e, a, c)); // unchanged
    }

    [Fact]
    public void Occupied_DropOnSelf_IsNoOp()
    {
        var e = NewEditor();
        var a = Log(e, 0); var c = Log(e, 400);
        e.Connect(a, a.OutputPorts[0], c, c.InputPorts[0]);

        var r = e.ConnectOrMove(a, a.OutputPorts[0], a, a.InputPorts[0]);

        Assert.Equal(ConnectionError.SelfConnection, r);
        Assert.Single(e.Connections);
        Assert.NotNull(Edge(e, a, c));
    }

    [Fact]
    public void Occupied_MoveThatWouldCycle_IsNoOp()
    {
        var e = NewEditor();
        var a = Log(e, 0); var b = Log(e, 200); var c = Log(e, 400);
        e.Connect(b, b.OutputPorts[0], a, a.InputPorts[0]); // B -> A
        e.Connect(a, a.OutputPorts[0], c, c.InputPorts[0]); // A -> C (A's out occupied)

        var r = e.ConnectOrMove(a, a.OutputPorts[0], b, b.InputPorts[0]); // A -> B would cycle (B -> A -> B)

        Assert.Equal(ConnectionError.WouldCreateCycle, r);
        Assert.NotNull(Edge(e, a, c)); // unchanged
        Assert.NotNull(Edge(e, b, a));
        Assert.Equal(2, e.Connections.Count);
    }

    [Fact]
    public void Occupied_AutoForwardSkipped_WhenItWouldCycle()
    {
        var e = NewEditor();
        var a = Log(e, 0); var b = Log(e, 200); var c = Log(e, 400);
        e.Connect(a, a.OutputPorts[0], c, c.InputPorts[0]); // A -> C
        e.Connect(c, c.OutputPorts[0], b, b.InputPorts[0]); // C -> B (so B -> C would cycle)

        var r = e.ConnectOrMove(a, a.OutputPorts[0], b, b.InputPorts[0]);

        Assert.Equal(ConnectionError.None, r);
        Assert.NotNull(Edge(e, a, b)); // move happened
        Assert.NotNull(Edge(e, c, b)); // pre-existing kept
        Assert.Null(Edge(e, b, c));    // forward skipped (would cycle)
        Assert.Equal(2, e.Connections.Count);
    }

    [Fact]
    public void Unoccupied_DelegatesToConnect()
    {
        var e = NewEditor();
        var a = Log(e, 0); var b = Log(e, 200);

        var r = e.ConnectOrMove(a, a.OutputPorts[0], b, b.InputPorts[0]);

        Assert.Equal(ConnectionError.None, r);
        Assert.Single(e.Connections);
        Assert.NotNull(Edge(e, a, b));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test BotBuilder.Core.Tests --filter "FullyQualifiedName~EditorConnectOrMoveTests"`
Expected: FAIL — `ConnectOrMove` doesn't exist (compile error).

- [ ] **Step 3: Add the command**

In `BotBuilder.Core/Undo/EditorCommands.cs`, add (after `DisconnectCommand`):

```csharp
/// <summary>Moves an occupied output edge to a new target and, when given, forwards the dropped node's
/// single output to the old destination — inserting a node into a wire. Atomic: one undo reverses all
/// of remove-old + add-primary (+ add-forward).</summary>
internal sealed class InsertOrMoveConnectionCommand : IUndoableCommand
{
    private readonly BotEditorViewModel _editor;
    private readonly ConnectionViewModel _removed;
    private readonly ConnectionViewModel _primary;
    private readonly ConnectionViewModel? _forward;

    public InsertOrMoveConnectionCommand(
        BotEditorViewModel editor,
        ConnectionViewModel removed,
        ConnectionViewModel primary,
        ConnectionViewModel? forward)
    {
        _editor = editor;
        _removed = removed;
        _primary = primary;
        _forward = forward;
    }

    public void Do()
    {
        _editor.RemoveConnectionCore(_removed);
        _editor.AddConnectionCore(_primary);
        if (_forward is not null) { _editor.AddConnectionCore(_forward); }
    }

    public void Undo()
    {
        if (_forward is not null) { _editor.RemoveConnectionCore(_forward); }
        _editor.RemoveConnectionCore(_primary);
        _editor.AddConnectionCore(_removed);
    }
}
```

- [ ] **Step 4: Add ConnectOrMove to the editor**

In `BotBuilder.Core/BotEditorViewModel.cs`, add this method immediately after the existing `Connect`
method (which ends at the `return ConnectionError.None; }` around line 255). It uses `ConnectionValidator`,
`ConnectionViewModel`, and `InsertOrMoveConnectionCommand` — all already in scope via the file's existing
`using BotBuilder.Core.Connections;` and `using BotBuilder.Core.Undo;` (verify those usings are present;
`Connect` already references `ConnectionValidator` and `ConnectCommand`, so they are):

```csharp
    /// <summary>Connects like <see cref="Connect"/> when the source port is free; when it already drives
    /// an edge, MOVES that edge to <paramref name="target"/> and — if the dropped node has exactly one
    /// unset output — forwards it to the old destination (inserting the node into the wire). One undo.</summary>
    public ConnectionError ConnectOrMove(NodeViewModel source, PortViewModel sourcePort,
                                         NodeViewModel target, PortViewModel targetPort)
    {
        var existing = Connections.FirstOrDefault(c =>
            ReferenceEquals(c.Source, source) && c.SourcePort.Name == sourcePort.Name);
        if (existing is null)
        {
            return Connect(source, sourcePort, target, targetPort);
        }

        // Occupied source: validate the retarget against the graph MINUS the edge we're moving, so the
        // now-freed port doesn't trip SourcePortOccupied. Still rejects self / duplicate (old target) /
        // cycle / not-output-to-input — any of which leaves the wire exactly as it was.
        var others = Connections.Where(c => !ReferenceEquals(c, existing)).ToList();
        var moveError = ConnectionValidator.Validate(others, source, sourcePort, target, targetPort);
        if (moveError != ConnectionError.None)
        {
            return moveError;
        }

        var primary = new ConnectionViewModel(Guid.NewGuid(), source, sourcePort, target, targetPort);

        // Auto-forward only when the dropped node has exactly one output and it's unset.
        ConnectionViewModel? forward = null;
        if (target.OutputPorts.Count == 1)
        {
            var singleOut = target.OutputPorts[0];
            var outOccupied = others.Any(c =>
                ReferenceEquals(c.Source, target) && c.SourcePort.Name == singleOut.Name);
            if (!outOccupied)
            {
                // Validate the forward edge against the post-move graph; skip it if it would cycle.
                var afterMove = others.Append(primary).ToList();
                var fwdError = ConnectionValidator.Validate(
                    afterMove, target, singleOut, existing.Target, existing.TargetPort);
                if (fwdError == ConnectionError.None)
                {
                    forward = new ConnectionViewModel(
                        Guid.NewGuid(), target, singleOut, existing.Target, existing.TargetPort);
                }
            }
        }

        _undo.Execute(new InsertOrMoveConnectionCommand(this, existing, primary, forward));
        AfterEdit();
        return ConnectionError.None;
    }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test BotBuilder.Core.Tests --filter "FullyQualifiedName~EditorConnectOrMoveTests"`
Expected: PASS (all 9 cases).

- [ ] **Step 6: Run the full BotBuilder.Core.Tests suite**

Run: `dotnet test BotBuilder.Core.Tests`
Expected: PASS, 0 failures (no regression in existing connection/undo tests).

- [ ] **Step 7: Commit**

```bash
git add BotBuilder.Core/Undo/EditorCommands.cs BotBuilder.Core/BotEditorViewModel.cs BotBuilder.Core.Tests/EditorConnectOrMoveTests.cs
git commit -m "Editor: ConnectOrMove — drag an occupied output to move/insert a node into the wire"
```
Append the trailer: `Claude-Session: https://claude.ai/code/session_01CmX3RvuoA9B7XJ8WCUoFgU`

---

## Task 2: Wire the canvas drag to ConnectOrMove

**Files:**
- Modify: `BotBuilder/MainWindow.xaml.cs` (in `CompleteConnectionDrag`, ~line 528)

- [ ] **Step 1: Change the one call**

In `CompleteConnectionDrag`, change:
```csharp
            _editor.Connect(source, sourcePort, targetNode, targetPort);
```
to:
```csharp
            _editor.ConnectOrMove(source, sourcePort, targetNode, targetPort);
```

(Confirm this is the only interactive drag-completion call site. `_editor.Connect` may still be called
elsewhere programmatically — leave those; only the drag-completion path routes through `ConnectOrMove`.)

- [ ] **Step 2: Build the solution**

Run: `dotnet build ADB.slnx -clp:ErrorsOnly`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add BotBuilder/MainWindow.xaml.cs
git commit -m "Editor: canvas drag from an occupied output now moves/inserts (ConnectOrMove)"
```
Append the trailer: `Claude-Session: https://claude.ai/code/session_01CmX3RvuoA9B7XJ8WCUoFgU`

---

## Task 3: Documentation

**Files:**
- Modify: `README.md`
- Modify: `CLAUDE.md`
- Modify: `ADB.wiki` (the editor/canvas page — deferred to merge, like other slices)

- [ ] **Step 1: README.md**

In the *arsenal* → *Visual node-graph editor* bullet (which mentions right-drag-to-wire and Tidy Up),
add a clause, goblin voice:

> …**drag a wire's tail onto a node to drop it right into the flow** (hello, retroactive Delays)…

Keep it a phrase within the existing bullet; ground it in the behavior (occupied-output drag moves the
wire; single-out nodes auto-forward).

- [ ] **Step 2: CLAUDE.md**

Under the Canvas VM / editor description (the `BotEditorViewModel` area), add a one-line note:

> Dragging from an **already-connected** output port calls `ConnectOrMove`: it moves that wire to the
> dropped node and, when the dropped node has exactly one unset output, forwards it to the old
> destination (insert-into-wire) — one undoable command.

- [ ] **Step 3: Wiki (deferred to merge)**

Note in the PR/merge summary that the editor/canvas wiki page should gain the drag-to-move/insert
behavior (moving an occupied output; single-out auto-insert). The wiki working copy lives in the main
checkout; do not push it from this unmerged, parked branch — the user will fold it in at merge.

- [ ] **Step 4: Commit main-repo docs**

```bash
git add README.md CLAUDE.md
git commit -m "Docs: drag-to-move/insert connector (ConnectOrMove)"
```
Append the trailer: `Claude-Session: https://claude.ai/code/session_01CmX3RvuoA9B7XJ8WCUoFgU`

---

## Final verification

- [ ] `dotnet test BotBuilder.Core.Tests` → green (9 new + existing).
- [ ] `dotnet build ADB.slnx -clp:ErrorsOnly` → clean.
- [ ] **Park for the user's visual check:** in BotBuilder, wire `A→C`, then drag `A`'s output onto a Delay
  dropped nearby → expect `A→Delay→C`; drag onto a Branch → expect only `A→Branch`; Ctrl+Z restores
  `A→C`. Do NOT self-merge — this is a visual slice for the user to verify and merge.
