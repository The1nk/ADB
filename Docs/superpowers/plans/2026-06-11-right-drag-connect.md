# Right-Click-Drag to Connect (Aim Assist) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Right-button drag from anywhere on a node card's body and drop on another card creates the same connection as left-click-dragging from that card's first output port — a bigger, easier grab target for imprecise aim.

**Architecture:** Pure WPF interaction in `MainWindow`. The existing output-port drag (`OutputPort_MouseLeftButtonDown` → capture → `FinishConnectionDrag` → geometric hit-test → `editor.Connect`) is refactored so its finish logic lives in a shared `CompleteConnectionDrag(Point)`. A new `NodeHost`-level right-button handler hit-tests the card under the press, sets the pending source to that card's first output port, and reuses the same completion path. Wiring is at the `NodeHost` element so the per-card template is untouched (no conflict with the parked nested-bot UI). The editor's `ConnectionValidator` already no-ops invalid drops (self, duplicate, occupied source, cycle), so a plain right-click (which resolves to the source card itself) is harmless — no drag threshold needed.

**Tech Stack:** .NET 10 WPF.

User request (2026-06-11). This is a self-contained interaction tweak; no engine/model change. Builds on `main` (independent of the parked nested-bot PRs).

Work in worktree `C:\git\ADB-aimassist` (branch `worktree-aim-assist`). Build/test from the worktree root.

NOTE: WPF mouse interaction is not unit-testable; the reused `editor.Connect`/`ConnectionValidator` path is already covered by existing tests. Verification for the new gesture is a build + manual check.

---

### Task 1: Extract `CompleteConnectionDrag` from the existing left drag

**Files:**
- Modify: `BotBuilder/MainWindow.xaml.cs`

READ the current `OutputPort_MouseLeftButtonDown`, `FinishConnectionDrag`, and `NodeOf` methods first.

- [ ] **Step 1: Refactor `FinishConnectionDrag`**

Replace the existing `FinishConnectionDrag` method body so the node-resolution + connect logic moves into a new shared `CompleteConnectionDrag(Point)`. The result:

```csharp
    private void FinishConnectionDrag(object sender, MouseButtonEventArgs e)
    {
        // Capture the drop position BEFORE releasing capture (while captured, Mouse.DirectlyOver reports the
        // captured element, so the drop target is resolved geometrically in CompleteConnectionDrag).
        var dropPosition = e.GetPosition(NodeHost);

        NodeHost.MouseLeftButtonUp -= FinishConnectionDrag;
        Mouse.Capture(null);

        CompleteConnectionDrag(dropPosition);
    }

    /// <summary>Resolves the node under <paramref name="dropPosition"/> and connects the pending source output to
    /// its input port. Shared by the output-port (left) drag and the right-button card-body drag. Invalid drops
    /// (self, duplicate, occupied source, cycle, or empty canvas) are no-ops via the editor's ConnectionValidator.</summary>
    private void CompleteConnectionDrag(Point dropPosition)
    {
        var source = _connectSourceNode;
        var sourcePort = _connectSourcePort;
        _connectSourceNode = null;
        _connectSourcePort = null;
        if (source is null || sourcePort is null)
        {
            return;
        }

        var hit = System.Windows.Media.VisualTreeHelper.HitTest(NodeHost, dropPosition)?.VisualHit;
        if (hit is { } h && NodeOf(h) is { } targetNode && targetNode.InputPorts.FirstOrDefault() is { } targetPort)
        {
            _editor.Connect(source, sourcePort, targetNode, targetPort);
        }
    }
```

(This preserves the existing left-drag behavior exactly — it now just delegates the finish to the shared method.)

- [ ] **Step 2: Build**

Run: `dotnet build ADB.slnx`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add BotBuilder/MainWindow.xaml.cs
git commit -m "Extract CompleteConnectionDrag from FinishConnectionDrag"
```

---

### Task 2: Right-button card-body drag

**Files:**
- Modify: `BotBuilder/MainWindow.xaml.cs`
- Modify: `BotBuilder/MainWindow.xaml`

- [ ] **Step 1: Add the right-button handlers**

In `BotBuilder/MainWindow.xaml.cs`, add these two methods (near the other connection handlers):

```csharp
    // Right-button drag from a card's BODY behaves like dragging from the card's first output port — a larger,
    // easier grab target than the small port dot, for imprecise aim. Starts only when the press lands on a card;
    // a plain right-click (no real move) resolves back to the same card and is rejected as a self-connection by
    // the ConnectionValidator, so it does nothing.
    private void NodeHost_RightConnectStart(object sender, MouseButtonEventArgs e)
    {
        var hit = System.Windows.Media.VisualTreeHelper.HitTest(NodeHost, e.GetPosition(NodeHost))?.VisualHit;
        if (hit is { } h && NodeOf(h) is { } node && node.OutputPorts.FirstOrDefault() is { } port)
        {
            _connectSourceNode = node;
            _connectSourcePort = port;
            NodeHost.CaptureMouse();
            NodeHost.MouseRightButtonUp += FinishRightConnectionDrag;
            e.Handled = true; // claim the gesture so it doesn't fall through to selection / a future context menu
        }
    }

    private void FinishRightConnectionDrag(object sender, MouseButtonEventArgs e)
    {
        var dropPosition = e.GetPosition(NodeHost);
        NodeHost.MouseRightButtonUp -= FinishRightConnectionDrag;
        Mouse.Capture(null);
        CompleteConnectionDrag(dropPosition);
        e.Handled = true;
    }
```

(`NodeOf` walks the visual tree up to the `NodeViewModel`, so a press anywhere on the card — header, body, label — resolves to the node. A node with no output ports, e.g. End, has `OutputPorts.FirstOrDefault() == null` and is skipped.)

- [ ] **Step 2: Wire it on `NodeHost`**

In `BotBuilder/MainWindow.xaml`, find the node-layer items control opening tag `<ItemsControl x:Name="NodeHost" ItemsSource="{Binding Nodes}">` and add the tunneling handler so the gesture is claimed at the host before child elements:
```xml
                    <ItemsControl x:Name="NodeHost" ItemsSource="{Binding Nodes}"
                                  PreviewMouseRightButtonDown="NodeHost_RightConnectStart">
```
(Only add the `PreviewMouseRightButtonDown` attribute; leave the rest of the element unchanged.)

- [ ] **Step 3: Build + manual verification**

Run: `dotnet build ADB.slnx`
Expected: Build succeeded.

Launch `dotnet run --project BotBuilder`. Add two nodes (e.g. Start → and a Click). **Right-button press on the body of the first card, drag to the second card, release** → a connection appears from the first card's first output to the second card's input (identical to dragging from the output dot). Verify: a plain right-click on a card does nothing; right-dragging from an End node (no outputs) does nothing; dropping on empty canvas does nothing; dropping back on the same card does nothing; an already-connected output still respects the validator (no duplicate). Left-drag from the output dot still works unchanged.

- [ ] **Step 4: Commit**

```bash
git add BotBuilder/MainWindow.xaml.cs BotBuilder/MainWindow.xaml
git commit -m "Right-button drag from card body to connect (aim assist)"
```

---

### Task 3: Full suite green

- [ ] **Step 1: Run the whole suite**

Run: `dotnet test ADB.slnx`
Expected: PASS, no regressions (this change adds no tests — it reuses the already-tested `editor.Connect` path).

---

## Self-Review

- **Coverage:** right-button card-body drag → first output port → target input, reusing the exact existing connect path (Tasks 1-2). Invalid cases (no outputs, self, empty, duplicate) handled by `OutputPorts.FirstOrDefault()` + the existing `ConnectionValidator` — no special-casing or threshold needed. ✓
- **No regression:** the left output-port drag is unchanged (now delegates its finish to the shared `CompleteConnectionDrag`). ✓
- **Conflict-avoidance:** wiring is on the `NodeHost` element + new code-behind methods, NOT the per-card `DataTemplate` — so this doesn't collide with the parked nested-bot UI's card-template changes. ✓
- **Placeholders:** none. ✓
- **Note for executor:** read the current `FinishConnectionDrag`/`NodeOf` first; `_connectSourceNode`/`_connectSourcePort` fields already exist; confirm `Point` and `MouseButtonEventArgs` usings are present (they are — the file already uses them).
