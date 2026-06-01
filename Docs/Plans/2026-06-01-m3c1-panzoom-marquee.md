# M3c-1 — Pan/Zoom + Marquee Multi-Select Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add canvas pan + zoom-to-cursor and rubber-band marquee multi-selection (with multi-delete) to the Bot Builder. First half of the M3c slice; the Target Bar is the second half.

**Architecture:** Pure, unit-tested math/logic in WPF-free `BotBuilder.Core` — a `CanvasViewport` (scale/offset with zoom-to-cursor + pan), a `MarqueeSelection` rect/intersection helper, and editor multi-select + composite multi-delete. The WPF shell applies the viewport as a `RenderTransform` and adds wheel-zoom, middle-drag-pan, and a rubber-band marquee gesture — all thin delegations to the core. Per `Docs/Specs/2026-06-01-m3-builder-canvas-design.md` (§3.3, M3c slice).

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), WPF, CommunityToolkit.Mvvm, xUnit. Builds on merged M3b.

---

## Verification model
- **Tasks 1–3 (`BotBuilder.Core`)**: strict TDD via `dotnet test`.
- **Tasks 4–5 (`BotBuilder` WPF)**: `dotnet build ADB.slnx` 0 warnings + `dotnet test` green; each ends with a **Manual Verification Checklist**.

## File Structure
```
BotBuilder.Core/
  Canvas/
    CanvasViewport.cs         # NEW: scale + offset, ZoomAt (zoom-to-cursor), Pan, clamping
    MarqueeSelection.cs       # NEW: nodes whose card rect intersects a marquee rect (pure)
  NodeLayout.cs               # MODIFIED: add CardHeight
  BotEditorViewModel.cs       # MODIFIED: Viewport property, SelectNodes (multi), multi-delete
  Undo/EditorCommands.cs      # MODIFIED: DeleteNodeCommand -> DeleteNodesCommand (set-based)
BotBuilder/
  MainWindow.xaml             # MODIFIED: viewport RenderTransform + marquee overlay
  MainWindow.xaml.cs          # MODIFIED: wheel-zoom, middle-drag pan, marquee gesture
BotBuilder.Core.Tests/
  CanvasViewportTests.cs, MarqueeSelectionTests.cs, EditorMultiSelectTests.cs
```

---

### Task 1: `CanvasViewport` (pan / zoom-to-cursor math)

**Files:**
- Create: `BotBuilder.Core/Canvas/CanvasViewport.cs`
- Test: `BotBuilder.Core.Tests/CanvasViewportTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `BotBuilder.Core.Tests/CanvasViewportTests.cs`:
```csharp
using BotBuilder.Core.Canvas;
using Xunit;

namespace BotBuilder.Core.Tests;

public class CanvasViewportTests
{
    [Fact]
    public void Defaults_AreIdentity()
    {
        var v = new CanvasViewport();

        Assert.Equal(1.0, v.Scale);
        Assert.Equal(0.0, v.OffsetX);
        Assert.Equal(0.0, v.OffsetY);
    }

    [Fact]
    public void Pan_AddsToOffset()
    {
        var v = new CanvasViewport();

        v.Pan(15, -20);

        Assert.Equal(15, v.OffsetX);
        Assert.Equal(-20, v.OffsetY);
    }

    [Fact]
    public void ZoomAt_KeepsWorldPointUnderAnchorFixed()
    {
        var v = new CanvasViewport();
        v.Pan(30, 10); // some starting offset

        // world point currently under screen anchor (200, 120)
        const double anchorX = 200, anchorY = 120;
        var worldBeforeX = (anchorX - v.OffsetX) / v.Scale;
        var worldBeforeY = (anchorY - v.OffsetY) / v.Scale;

        v.ZoomAt(anchorX, anchorY, 1.25);

        var worldAfterX = (anchorX - v.OffsetX) / v.Scale;
        var worldAfterY = (anchorY - v.OffsetY) / v.Scale;

        Assert.Equal(worldBeforeX, worldAfterX, 6);
        Assert.Equal(worldBeforeY, worldAfterY, 6);
        Assert.Equal(1.25, v.Scale, 6);
    }

    [Fact]
    public void ZoomAt_ClampsToMaxScale()
    {
        var v = new CanvasViewport();

        for (var i = 0; i < 50; i++)
        {
            v.ZoomAt(0, 0, 2.0);
        }

        Assert.Equal(CanvasViewport.MaxScale, v.Scale, 6);
    }

    [Fact]
    public void ZoomAt_ClampsToMinScale()
    {
        var v = new CanvasViewport();

        for (var i = 0; i < 50; i++)
        {
            v.ZoomAt(0, 0, 0.5);
        }

        Assert.Equal(CanvasViewport.MinScale, v.Scale, 6);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test BotBuilder.Core.Tests`
Expected: build FAILS — `CanvasViewport` doesn't exist.

- [ ] **Step 3: Implement `CanvasViewport`**

Create `BotBuilder.Core/Canvas/CanvasViewport.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotBuilder.Core.Canvas;

/// <summary>The pan/zoom state of the editor canvas. Maps world coordinates to screen as
/// <c>screen = world * Scale + Offset</c>. Pure, view-framework-free.</summary>
public partial class CanvasViewport : ObservableObject
{
    public const double MinScale = 0.2;
    public const double MaxScale = 4.0;

    [ObservableProperty] private double _scale = 1.0;
    [ObservableProperty] private double _offsetX;
    [ObservableProperty] private double _offsetY;

    /// <summary>Pans by a screen-space delta.</summary>
    public void Pan(double dx, double dy)
    {
        OffsetX += dx;
        OffsetY += dy;
    }

    /// <summary>Zooms by <paramref name="factor"/> about the screen point
    /// (<paramref name="anchorX"/>, <paramref name="anchorY"/>), keeping the world point under
    /// that anchor fixed. Scale is clamped to [<see cref="MinScale"/>, <see cref="MaxScale"/>].</summary>
    public void ZoomAt(double anchorX, double anchorY, double factor)
    {
        var newScale = Math.Clamp(Scale * factor, MinScale, MaxScale);

        var worldX = (anchorX - OffsetX) / Scale;
        var worldY = (anchorY - OffsetY) / Scale;

        Scale = newScale;
        OffsetX = anchorX - worldX * newScale;
        OffsetY = anchorY - worldY * newScale;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all pass (104 prior + 5 new = 109), 0 failures.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(builder): add canvas viewport (pan + zoom-to-cursor math)"
```

---

### Task 2: `NodeLayout.CardHeight` + `MarqueeSelection`

**Files:**
- Modify: `BotBuilder.Core/NodeLayout.cs`
- Create: `BotBuilder.Core/Canvas/MarqueeSelection.cs`
- Test: `BotBuilder.Core.Tests/MarqueeSelectionTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `BotBuilder.Core.Tests/MarqueeSelectionTests.cs`:
```csharp
using AdbCore.Actions.BuiltIn;
using BotBuilder.Core;
using BotBuilder.Core.Canvas;
using Xunit;

namespace BotBuilder.Core.Tests;

public class MarqueeSelectionTests
{
    private static NodeViewModel NodeAt(double x, double y)
        => NodeViewModel.FromDefinition(new LogAction(), Guid.NewGuid(), "", x, y);

    [Fact]
    public void NodesInRect_IncludesNodesWhoseCardIntersects()
    {
        var inside = NodeAt(50, 50);
        var far = NodeAt(1000, 1000);

        var hit = MarqueeSelection.NodesInRect(new[] { inside, far }, 0, 0, 200, 200).ToList();

        Assert.Contains(inside, hit);
        Assert.DoesNotContain(far, hit);
    }

    [Fact]
    public void NodesInRect_IncludesPartialOverlap()
    {
        // node spans [40..40+CardWidth] x [40..40+CardHeight]; rect just clips its top-left
        var node = NodeAt(40, 40);

        var hit = MarqueeSelection.NodesInRect(new[] { node }, 0, 0, 50, 50).ToList();

        Assert.Single(hit);
    }

    [Fact]
    public void NodesInRect_ExcludesNodeOutsideRect()
    {
        var node = NodeAt(300, 300);

        var hit = MarqueeSelection.NodesInRect(new[] { node }, 0, 0, 100, 100).ToList();

        Assert.Empty(hit);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test BotBuilder.Core.Tests`
Expected: build FAILS — `MarqueeSelection` (and `NodeLayout.CardHeight`) don't exist.

- [ ] **Step 3: Add `CardHeight` and implement `MarqueeSelection`**

In `BotBuilder.Core/NodeLayout.cs`, add a `CardHeight` constant alongside `CardWidth` (place it right after the `CardWidth` line):
```csharp
    public const double CardHeight = 70;
```

Create `BotBuilder.Core/Canvas/MarqueeSelection.cs`:
```csharp
namespace BotBuilder.Core.Canvas;

/// <summary>Selects the nodes whose card rectangle intersects a marquee rectangle (all in
/// world coordinates). The marquee rect is expected normalized (non-negative width/height).</summary>
public static class MarqueeSelection
{
    public static IEnumerable<NodeViewModel> NodesInRect(
        IEnumerable<NodeViewModel> nodes, double x, double y, double width, double height)
    {
        var right = x + width;
        var bottom = y + height;

        return nodes.Where(n =>
            n.X < right && n.X + NodeLayout.CardWidth > x &&
            n.Y < bottom && n.Y + NodeLayout.CardHeight > y);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all pass (109 prior + 3 new = 112), 0 failures.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(builder): add card height and marquee rect hit-testing"
```

---

### Task 3: Editor — viewport, multi-select, multi-delete

Adds `Viewport` to the editor, a `SelectNodes` multi-select, and reworks deletion so a multi-node delete is a single undoable operation (replacing the single `DeleteNodeCommand` with a set-based `DeleteNodesCommand`).

**Files:**
- Modify: `BotBuilder.Core/Undo/EditorCommands.cs` (replace `DeleteNodeCommand` with `DeleteNodesCommand`)
- Modify: `BotBuilder.Core/BotEditorViewModel.cs`
- Test: `BotBuilder.Core.Tests/EditorMultiSelectTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `BotBuilder.Core.Tests/EditorMultiSelectTests.cs`:
```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class EditorMultiSelectTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    [Fact]
    public void Editor_ExposesAViewport()
    {
        Assert.NotNull(NewEditor().Viewport);
    }

    [Fact]
    public void SelectNodes_MarksOnlyThoseSelected()
    {
        var e = NewEditor();
        var a = e.AddNode("control.start", 0, 0);
        var b = e.AddNode("data.log", 0, 0);
        var c = e.AddNode("control.end", 0, 0);

        e.SelectNodes(new[] { a, c });

        Assert.True(a.IsSelected);
        Assert.False(b.IsSelected);
        Assert.True(c.IsSelected);
    }

    [Fact]
    public void SelectNodes_ClearsConnectionSelection()
    {
        var e = NewEditor();
        var a = e.AddNode("control.start", 0, 0);
        var b = e.AddNode("data.log", 0, 0);
        e.Connect(a, a.OutputPorts[0], b, b.InputPorts[0]);
        e.SelectConnection(e.Connections[0]);

        e.SelectNodes(new[] { a });

        Assert.Null(e.SelectedConnection);
        Assert.False(e.Connections[0].IsSelected);
    }

    [Fact]
    public void DeleteSelection_DeletesAllSelectedNodes_AsOneUndo()
    {
        var e = NewEditor();
        var a = e.AddNode("control.start", 0, 0);
        var b = e.AddNode("data.log", 0, 0);
        var c = e.AddNode("control.end", 0, 0);
        e.Connect(a, a.OutputPorts[0], b, b.InputPorts[0]); // a->b will be cascaded

        e.SelectNodes(new[] { a, b });
        e.DeleteSelection();

        Assert.DoesNotContain(a, e.Nodes);
        Assert.DoesNotContain(b, e.Nodes);
        Assert.Contains(c, e.Nodes);
        Assert.Empty(e.Connections);

        e.Undo(); // single undo restores both nodes and the connection
        Assert.Contains(a, e.Nodes);
        Assert.Contains(b, e.Nodes);
        Assert.Single(e.Connections);
    }

    [Fact]
    public void DeleteSelection_NothingSelected_IsNoOp()
    {
        var e = NewEditor();
        e.AddNode("control.start", 0, 0);

        e.DeleteSelection();

        Assert.Single(e.Nodes);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test BotBuilder.Core.Tests`
Expected: build FAILS — `Viewport`, `SelectNodes` don't exist.

- [ ] **Step 3: Replace `DeleteNodeCommand` with `DeleteNodesCommand`**

In `BotBuilder.Core/Undo/EditorCommands.cs`, replace the entire `DeleteNodeCommand` class with:
```csharp
/// <summary>Removes a set of nodes and a set of connections; undo restores all of them.</summary>
internal sealed class DeleteNodesCommand : IUndoableCommand
{
    private readonly BotEditorViewModel _editor;
    private readonly IReadOnlyList<NodeViewModel> _nodes;
    private readonly IReadOnlyList<ConnectionViewModel> _connections;

    public DeleteNodesCommand(
        BotEditorViewModel editor,
        IReadOnlyList<NodeViewModel> nodes,
        IReadOnlyList<ConnectionViewModel> connections)
    {
        _editor = editor;
        _nodes = nodes;
        _connections = connections;
    }

    public void Do()
    {
        foreach (var c in _connections) { _editor.RemoveConnectionCore(c); }
        foreach (var n in _nodes) { _editor.RemoveNodeCore(n); }
    }

    public void Undo()
    {
        foreach (var n in _nodes) { _editor.AddNodeCore(n); }
        foreach (var c in _connections) { _editor.AddConnectionCore(c); }
    }
}
```
(Keep `using BotBuilder.Core.Connections;` at the top — it's already there for `ConnectionViewModel`.)

- [ ] **Step 4: Update `BotEditorViewModel`**

In `BotBuilder.Core/BotEditorViewModel.cs`:

(a) Add the using and a `Viewport` property. Add to the using block:
```csharp
using BotBuilder.Core.Canvas;
```
Add the property initialization in the constructor (after `Connections = ...`):
```csharp
        Viewport = new CanvasViewport();
```
And the property (next to `Palette`):
```csharp
    public CanvasViewport Viewport { get; }
```

(b) Replace the existing `DeleteNode` method so it routes through `DeleteNodesCommand`:
```csharp
    public void DeleteNode(NodeViewModel node)
    {
        var incident = IncidentConnections(new[] { node });
        _undo.Execute(new DeleteNodesCommand(this, new[] { node }, incident));
        AfterEdit();
    }
```

(c) Replace the existing `DeleteSelection` method with one that deletes all selected nodes as a single undoable op:
```csharp
    public void DeleteSelection()
    {
        if (SelectedConnection is { } connection)
        {
            Disconnect(connection);
            SelectedConnection = null;
            return;
        }

        var nodes = Nodes.Where(n => n.IsSelected).ToList();
        if (nodes.Count == 0)
        {
            return;
        }

        _undo.Execute(new DeleteNodesCommand(this, nodes, IncidentConnections(nodes)));
        SelectedNode = null;
        AfterEdit();
    }
```

(d) Add a `SelectNodes` method (next to `Select`):
```csharp
    public void SelectNodes(IEnumerable<NodeViewModel> nodes)
    {
        var selected = nodes.ToList();
        var set = new HashSet<NodeViewModel>(selected);

        foreach (var n in Nodes) { n.IsSelected = set.Contains(n); }
        foreach (var c in Connections) { c.IsSelected = false; }

        SelectedConnection = null;
        SelectedNode = selected.Count == 1 ? selected[0] : null;
    }
```

(e) Add a private helper used by (b) and (c):
```csharp
    private List<ConnectionViewModel> IncidentConnections(IReadOnlyCollection<NodeViewModel> nodes)
        => Connections
            .Where(c => nodes.Any(n => ReferenceEquals(c.Source, n) || ReferenceEquals(c.Target, n)))
            .Distinct()
            .ToList();
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test`
Expected: all pass (112 prior + 5 new = 117), 0 failures. The M3b deletion tests (`DeleteNode_CascadesConnections_AndUndoRestoresThem`, `DeleteSelection_RemovesSelectedConnectionElseSelectedNode`) must still pass (single-node delete now flows through the set-based command).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(builder): editor viewport, multi-select, and single-undo multi-delete"
```

---

### Task 4: WPF — viewport transform, wheel-zoom, middle-drag pan

> **Verification:** clean `dotnet build` (0 warnings) + `dotnet test` (117) + Manual Verification Checklist.

**Files:**
- Modify: `BotBuilder/MainWindow.xaml`, `BotBuilder/MainWindow.xaml.cs`

- [ ] **Step 1: Apply the viewport transform + wheel/pan hooks in XAML**

In `BotBuilder/MainWindow.xaml`:

(a) On the canvas `Border` (the `Grid.Column="1"` border with `ClipToBounds="True"`), add a name and the wheel + mouse-down hooks. Change its opening tag to:
```xml
            <Border Grid.Column="1" x:Name="ViewportHost" Background="#FAFAFA" BorderBrush="#CCC" BorderThickness="1,0,1,0"
                    ClipToBounds="True" MouseWheel="Viewport_MouseWheel" MouseDown="Viewport_MouseDown">
```

(b) Give `CanvasRoot` the viewport `RenderTransform`. Change the `<Grid x:Name="CanvasRoot" ...>` opening so it includes a render transform bound to the editor's viewport. Replace the line:
```xml
                <Grid x:Name="CanvasRoot" Background="Transparent" AllowDrop="True" Drop="Canvas_Drop">
```
with:
```xml
                <Grid x:Name="CanvasRoot" Background="Transparent" AllowDrop="True" Drop="Canvas_Drop">
                    <Grid.RenderTransform>
                        <TransformGroup>
                            <ScaleTransform ScaleX="{Binding Viewport.Scale}" ScaleY="{Binding Viewport.Scale}" />
                            <TranslateTransform X="{Binding Viewport.OffsetX}" Y="{Binding Viewport.OffsetY}" />
                        </TransformGroup>
                    </Grid.RenderTransform>
```

- [ ] **Step 2: Implement wheel-zoom and middle-drag pan in code-behind**

In `BotBuilder/MainWindow.xaml.cs`, add panning state fields near the other fields:
```csharp
    private bool _isPanning;
    private Point _panLastPoint;
```
Add these handlers to the class:
```csharp
    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var factor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
        var anchor = e.GetPosition(ViewportHost);
        _editor.Viewport.ZoomAt(anchor.X, anchor.Y, factor);
        e.Handled = true;
    }

    private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle)
        {
            return;
        }

        _isPanning = true;
        _panLastPoint = e.GetPosition(ViewportHost);
        ViewportHost.CaptureMouse();
        ViewportHost.MouseMove += Viewport_PanMouseMove;
        ViewportHost.MouseUp += Viewport_PanMouseUp;
        e.Handled = true;
    }

    private void Viewport_PanMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        var p = e.GetPosition(ViewportHost);
        _editor.Viewport.Pan(p.X - _panLastPoint.X, p.Y - _panLastPoint.Y);
        _panLastPoint = p;
    }

    private void Viewport_PanMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle)
        {
            return;
        }

        _isPanning = false;
        ViewportHost.ReleaseMouseCapture();
        ViewportHost.MouseMove -= Viewport_PanMouseMove;
        ViewportHost.MouseUp -= Viewport_PanMouseUp;
        e.Handled = true;
    }
```

- [ ] **Step 3: Build and test**

Run: `dotnet build ADB.slnx`  → expect 0 warnings, 0 errors.
Run: `dotnet test` → 117 pass.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(builder): wheel zoom-to-cursor and middle-drag pan"
```

**Manual Verification Checklist (`dotnet run --project BotBuilder`):**
- Scroll wheel zooms in/out centered on the cursor; zoom stops at sensible min/max.
- Holding the middle mouse button and dragging pans the canvas; node/connection positions track correctly.
- After zoom/pan, dragging a node and connecting ports still land where expected (operations remain in world coordinates).

---

### Task 5: WPF — rubber-band marquee multi-select

> **Verification:** clean `dotnet build` (0 warnings) + `dotnet test` (117) + Manual Verification Checklist.

**Files:**
- Modify: `BotBuilder/MainWindow.xaml`, `BotBuilder/MainWindow.xaml.cs`

- [ ] **Step 1: Add a marquee overlay to the canvas**

In `BotBuilder/MainWindow.xaml`, add a marquee layer as the LAST child inside `CanvasRoot` (after the node-layer `ItemsControl`, before `</Grid>` that closes `CanvasRoot`), plus a mouse-down hook on `CanvasRoot` to start the marquee. First, change the `CanvasRoot` opening tag (already edited in Task 4) to also hook `MouseLeftButtonDown`:
```xml
                <Grid x:Name="CanvasRoot" Background="Transparent" AllowDrop="True" Drop="Canvas_Drop"
                      MouseLeftButtonDown="Canvas_MarqueeStart">
```
Then add, immediately before the `</Grid>` that closes `CanvasRoot`:
```xml
                    <Canvas IsHitTestVisible="False">
                        <Rectangle x:Name="MarqueeRect" Visibility="Collapsed"
                                   Fill="#332962D6" Stroke="#2962D6" StrokeThickness="1" />
                    </Canvas>
```

- [ ] **Step 2: Implement the marquee gesture in code-behind**

In `BotBuilder/MainWindow.xaml.cs`, add marquee state fields:
```csharp
    private bool _isMarqueeing;
    private Point _marqueeStartWorld;
```
Add these handlers. `Canvas_MarqueeStart` only fires for clicks on empty canvas (node/port/connection handlers set `e.Handled = true`, so they don't bubble here):
```csharp
    private void Canvas_MarqueeStart(object sender, MouseButtonEventArgs e)
    {
        _isMarqueeing = true;
        _marqueeStartWorld = e.GetPosition(NodeHost);

        UpdateMarqueeRect(_marqueeStartWorld, _marqueeStartWorld);
        MarqueeRect.Visibility = Visibility.Visible;

        CanvasRoot.CaptureMouse();
        CanvasRoot.MouseMove += Canvas_MarqueeMove;
        CanvasRoot.MouseLeftButtonUp += Canvas_MarqueeEnd;
        e.Handled = true;
    }

    private void Canvas_MarqueeMove(object sender, MouseEventArgs e)
    {
        if (!_isMarqueeing)
        {
            return;
        }
        UpdateMarqueeRect(_marqueeStartWorld, e.GetPosition(NodeHost));
    }

    private void Canvas_MarqueeEnd(object sender, MouseButtonEventArgs e)
    {
        if (!_isMarqueeing)
        {
            return;
        }

        _isMarqueeing = false;
        CanvasRoot.ReleaseMouseCapture();
        CanvasRoot.MouseMove -= Canvas_MarqueeMove;
        CanvasRoot.MouseLeftButtonUp -= Canvas_MarqueeEnd;
        MarqueeRect.Visibility = Visibility.Collapsed;

        var end = e.GetPosition(NodeHost);
        var x = Math.Min(_marqueeStartWorld.X, end.X);
        var y = Math.Min(_marqueeStartWorld.Y, end.Y);
        var w = Math.Abs(end.X - _marqueeStartWorld.X);
        var h = Math.Abs(end.Y - _marqueeStartWorld.Y);

        _editor.SelectNodes(BotBuilder.Core.Canvas.MarqueeSelection.NodesInRect(_editor.Nodes, x, y, w, h));
        e.Handled = true;
    }

    private void UpdateMarqueeRect(Point a, Point b)
    {
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        Canvas.SetLeft(MarqueeRect, x);
        Canvas.SetTop(MarqueeRect, y);
        MarqueeRect.Width = Math.Abs(b.X - a.X);
        MarqueeRect.Height = Math.Abs(b.Y - a.Y);
    }
```

Note: a click on empty space with no drag produces a zero-size marquee, so `SelectNodes([])` runs and clears the selection — i.e. clicking empty canvas deselects. `Canvas` here refers to `System.Windows.Controls.Canvas` (already available via `using System.Windows.Controls;`).

- [ ] **Step 3: Build and test**

Run: `dotnet build ADB.slnx` → expect 0 warnings, 0 errors.
Run: `dotnet test` → 117 pass.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(builder): rubber-band marquee multi-select"
```

**Manual Verification Checklist (`dotnet run --project BotBuilder`):**
- Dragging on empty canvas draws a translucent selection rectangle; nodes touched by it become selected (multiple at once).
- Pressing Delete with several nodes selected removes them all (and their connections) in one action; Ctrl+Z restores them all at once.
- Clicking empty canvas (no drag) clears the selection.
- Marquee works correctly after zooming/panning (selection matches what the rectangle visually covers).

---

## Self-Review

**Spec coverage (M3c-1 portion of the spec's M3c slice):**
- Pan + zoom-to-cursor (transform math) — Task 1 (`CanvasViewport`) + Task 4 (transform/handlers). ✓
- Marquee multi-select — Task 2 (`MarqueeSelection`) + Task 3 (`SelectNodes`) + Task 5 (rubber-band gesture). ✓
- Multi-delete with single undo — Task 3 (`DeleteNodesCommand`). ✓
- (Target Bar + assignment + badges are M3c-2, deliberately deferred.)

**Placeholder scan:** No TBD/placeholder steps; all code complete.

**Type consistency:** `CanvasViewport` (Scale/OffsetX/OffsetY/Pan/ZoomAt/MinScale/MaxScale), `NodeLayout.CardHeight`, `MarqueeSelection.NodesInRect(nodes,x,y,w,h)`, editor `Viewport`/`SelectNodes`/`DeleteSelection`/`DeleteNode`/`IncidentConnections`, `DeleteNodesCommand(editor, nodes, connections)` — names match across tasks and the XAML bindings (`Viewport.Scale`, `Viewport.OffsetX/Y`, `Nodes`, `NodeHost`, `MarqueeRect`). The `DeleteNodeCommand`→`DeleteNodesCommand` replacement is consistently applied (editor `DeleteNode` and `DeleteSelection` both use the new command). ✓

**Scope:** No Target Bar, no target assignment, no Properties form. Pan is middle-drag (Space+drag deferred as polish). ✓
