# Editor Usability — Plan B (Visual) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Serpentine "Tidy Up" layout, collapsible toolbox/properties panels, and an unsaved-changes prompt — bundled into one PR for user review.

**Architecture:** Three independent parts. **Part 3** makes `AutoLayout` lay long flows out as a serpentine (alternating band direction), flips ports on reversed bands so wires stay clean, makes connection routing decide forward-vs-back by port direction (not raw X), persists the flip in the `.bot` file, and wraps bands to a passed-in canvas width. **Part 4** adds collapsible side panels persisted in `AppSettings`. **Part 5** adds an unsaved-changes guard on New/Open/close via a testable core helper.

**Tech Stack:** C# / .NET 10, WPF, xUnit, CommunityToolkit.Mvvm. Source spec: `Docs/superpowers/specs/2026-07-04-editor-usability-batch-design.md` (items 3, 4, 5).

**Commit convention:** end every commit message body with `Claude-Session: https://claude.ai/code/session_01UvcnQr4NvWncn1DeKnC38a`.

**Branch:** create one feature branch for the whole plan (e.g. `feat/editor-usability-visual`); open a single PR at the end (do not merge — park for user review).

---

## File Structure

Part 3 (serpentine):
- `BotBuilder.Core/Connections/ConnectionGeometry.cs` — add `IsBackward`; use it in `BuildPath`.
- `BotBuilder.Core/PortViewModel.cs` — make `Edge` settable; add `Reposition`.
- `BotBuilder.Core/NodeViewModel.cs` — add `PortsFlipped` + `SetPortsFlipped`.
- `AdbCore/Models/BotAction.cs` — add `PortsFlipped` field.
- `BotBuilder.Core/DocumentMapper.cs` — round-trip `PortsFlipped`.
- `BotBuilder.Core/Layout/AutoLayout.cs` — `NodePlacement`, serpentine, `targetWidth`, tighter constants.
- `BotBuilder.Core/Connections/BackRoutePlanner.cs` — direction-aware back-edge filter.
- `BotBuilder.Core/Connections/ConnectionViewModel.cs` — direction-aware lane gate.
- `BotBuilder.Core/Undo/EditorCommands.cs` — `LayoutNodesCommand` (position + flip).
- `BotBuilder.Core/BotEditorViewModel.cs` — apply flip in `AutoLayout`, pass source edge in `RerouteBackEdges`.
- `BotBuilder/MainWindow.xaml.cs` — pass canvas width to Tidy Up.

Part 4 (collapsible panels):
- `AdbUi.Theme/AppSettings.cs` — two bools.
- `BotBuilder/MainWindow.xaml` — rails + chevrons + named columns.
- `BotBuilder/MainWindow.xaml.cs` — toggle/persist/shortcuts.

Part 5 (unsaved-changes guard):
- `BotBuilder.Core/UnsavedChangesGuard.cs` — new helper.
- `BotBuilder/MainWindow.xaml.cs` — wire into New/Open/OnClosing.

Docs (end of plan): `CLAUDE.md`, `README.md`, `ADB.wiki/*`.

---

# PART 3 — Serpentine "Tidy Up"

## Task 3.1: Direction-aware backward-edge detection

**Files:**
- Modify: `BotBuilder.Core/Connections/ConnectionGeometry.cs`
- Test: `BotBuilder.Core.Tests/Connections/ConnectionGeometryTests.cs` (create if absent)

- [ ] **Step 1: Write failing tests**

Create/append `BotBuilder.Core.Tests/Connections/ConnectionGeometryTests.cs`:

```csharp
using BotBuilder.Core;
using BotBuilder.Core.Connections;
using Xunit;

namespace BotBuilder.Core.Tests.Connections;

public class ConnectionGeometryTests
{
    [Fact]
    public void RightOutput_TargetToRight_IsForward()
        => Assert.False(ConnectionGeometry.IsBackward(new CanvasPoint(0, 0), PortEdge.Right, new CanvasPoint(200, 0)));

    [Fact]
    public void RightOutput_TargetToLeft_IsBackward()
        => Assert.True(ConnectionGeometry.IsBackward(new CanvasPoint(200, 0), PortEdge.Right, new CanvasPoint(0, 0)));

    [Fact]
    public void LeftOutput_TargetToLeft_IsForward()  // flipped (serpentine) band
        => Assert.False(ConnectionGeometry.IsBackward(new CanvasPoint(200, 0), PortEdge.Left, new CanvasPoint(0, 0)));

    [Fact]
    public void LeftOutput_TargetToRight_IsBackward()
        => Assert.True(ConnectionGeometry.IsBackward(new CanvasPoint(0, 0), PortEdge.Left, new CanvasPoint(200, 0)));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~ConnectionGeometryTests"`
Expected: FAIL — `IsBackward` does not exist (compile error).

- [ ] **Step 3: Add `IsBackward` and use it in `BuildPath`**

In `ConnectionGeometry.cs`, add this public method (near `BuildPath`):

```csharp
    /// <summary>Whether a connection runs "backwards" relative to its source port's facing direction — i.e.
    /// a loop-back / return wire that must be routed through a gutter rather than drawn as a forward curve.
    /// A Right/Bottom output faces right, so a target to its left is backward; a Left output (a flipped
    /// serpentine band) faces left, so a target to its right is backward.</summary>
    public static bool IsBackward(CanvasPoint start, PortEdge startEdge, CanvasPoint end) => startEdge switch
    {
        PortEdge.Left => end.X > start.X + BackRouteMargin,
        _ => end.X < start.X - BackRouteMargin,
    };
```

Then in `BuildPath`, replace the branch condition:

```csharp
        if (end.X < start.X - BackRouteMargin)
            return BuildBackRoute(start, startEdge, end, endEdge);
```

with:

```csharp
        if (IsBackward(start, startEdge, end))
            return BuildBackRoute(start, startEdge, end, endEdge);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~ConnectionGeometryTests"`
Expected: PASS (all four).

- [ ] **Step 5: Commit**

```bash
git add BotBuilder.Core/Connections/ConnectionGeometry.cs BotBuilder.Core.Tests/Connections/ConnectionGeometryTests.cs
git commit -m "Route connections by port direction, not raw X (serpentine-ready)"
```

---

## Task 3.2: Flippable ports on the node view-model

**Files:**
- Modify: `BotBuilder.Core/PortViewModel.cs`
- Modify: `BotBuilder.Core/NodeViewModel.cs`
- Test: `BotBuilder.Core.Tests/NodeViewModelFlipTests.cs` (create)

- [ ] **Step 1: Write failing tests**

Create `BotBuilder.Core.Tests/NodeViewModelFlipTests.cs`:

```csharp
using System.Linq;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class NodeViewModelFlipTests
{
    private static NodeViewModel DelayNode()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        defs.TryGet("control.delay", out var def);
        return NodeViewModel.FromDefinition(def!, System.Guid.NewGuid(), "Delay", 0, 0);
    }

    [Fact]
    public void Default_InputsLeft_OutputsRight()
    {
        var node = DelayNode();
        Assert.All(node.InputPorts, p => Assert.Equal(PortEdge.Left, p.Edge));
        Assert.All(node.OutputPorts, p => Assert.Equal(PortEdge.Right, p.Edge));
        Assert.False(node.PortsFlipped);
    }

    [Fact]
    public void Flipped_InputsRight_OutputsLeft()
    {
        var node = DelayNode();
        node.SetPortsFlipped(true);

        Assert.True(node.PortsFlipped);
        Assert.All(node.InputPorts, p => Assert.Equal(PortEdge.Right, p.Edge));
        Assert.All(node.OutputPorts, p => Assert.Equal(PortEdge.Left, p.Edge));
        // Right-edge input anchor sits at the card's right edge (x == CardWidth).
        Assert.Equal(NodeLayout.CardWidth, node.InputPorts[0].AnchorOffset.X);
        Assert.Equal(0, node.OutputPorts[0].AnchorOffset.X);
    }

    [Fact]
    public void Flip_Then_Unflip_RestoresEdges()
    {
        var node = DelayNode();
        node.SetPortsFlipped(true);
        node.SetPortsFlipped(false);
        Assert.False(node.PortsFlipped);
        Assert.Equal(PortEdge.Left, node.InputPorts[0].Edge);
        Assert.Equal(PortEdge.Right, node.OutputPorts[0].Edge);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~NodeViewModelFlipTests"`
Expected: FAIL — `SetPortsFlipped` / `PortsFlipped` / `Reposition` do not exist.

- [ ] **Step 3: Make `PortViewModel.Edge` settable + add `Reposition`**

In `PortViewModel.cs`, change the `Edge` property and add a method:

```csharp
    /// <summary>Which edge of the card this port sits on (drives its anchor and connector direction).
    /// Mutable so a node can flip its ports for a reversed serpentine band.</summary>
    public PortEdge Edge { get; private set; }

    /// <summary>Move this port to a new edge + anchor at once (used when a node flips its ports). Raises the
    /// anchor change notification so the bound canvas port + its connectors re-route.</summary>
    public void Reposition(PortEdge edge, CanvasPoint anchorOffset)
    {
        Edge = edge;
        AnchorOffset = anchorOffset;
    }
```

(Leave the constructor assigning `Edge = edge;` as-is.)

- [ ] **Step 4: Add `PortsFlipped` + `SetPortsFlipped` to `NodeViewModel`**

In `NodeViewModel.cs`, add a property (below the other auto-properties, e.g. after `public string Category { get; }`):

```csharp
    /// <summary>True when this node's ports are flipped for a right-to-left serpentine band: inputs on the
    /// Right edge, non-failure outputs on the Left edge. Persisted so a saved-then-reloaded tidy graph stays
    /// clean. Failure (bottom-edge) ports are unaffected.</summary>
    public bool PortsFlipped { get; private set; }
```

and this method (e.g. after `ReanchorRightOutputsAndInputs`):

```csharp
    /// <summary>Flips (or restores) port sides for a serpentine reversed band. Preserves port instances so
    /// wired connections keep their endpoint identity; only failure/bottom ports are left in place.</summary>
    public void SetPortsFlipped(bool flipped)
    {
        PortsFlipped = flipped;
        var inputEdge = flipped ? PortEdge.Right : PortEdge.Left;
        var outEdge = flipped ? PortEdge.Left : PortEdge.Right;

        for (var i = 0; i < InputPorts.Count; i++)
        {
            var anchor = flipped
                ? NodeLayout.RightAnchor(i, InputPorts.Count, Height)
                : NodeLayout.LeftAnchor(i, InputPorts.Count, Height);
            InputPorts[i].Reposition(inputEdge, anchor);
        }

        var sideOutputs = OutputPorts.Where(p => p.Edge is PortEdge.Left or PortEdge.Right).ToList();
        for (var i = 0; i < sideOutputs.Count; i++)
        {
            var anchor = flipped
                ? NodeLayout.LeftAnchor(i, sideOutputs.Count, Height)
                : NodeLayout.RightAnchor(i, sideOutputs.Count, Height);
            sideOutputs[i].Reposition(outEdge, anchor);
        }
    }
```

Add `using System.Linq;` to the top of `NodeViewModel.cs` if not already present.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~NodeViewModelFlipTests"`
Expected: PASS (all three).

- [ ] **Step 6: Commit**

```bash
git add BotBuilder.Core/PortViewModel.cs BotBuilder.Core/NodeViewModel.cs BotBuilder.Core.Tests/NodeViewModelFlipTests.cs
git commit -m "Add flippable ports to node view-model for serpentine bands"
```

---

## Task 3.3: Persist PortsFlipped in the .bot file

**Files:**
- Modify: `AdbCore/Models/BotAction.cs`
- Modify: `BotBuilder.Core/DocumentMapper.cs`
- Test: `BotBuilder.Core.Tests/DocumentMapperFlipTests.cs` (create)

- [ ] **Step 1: Write failing test**

Create `BotBuilder.Core.Tests/DocumentMapperFlipTests.cs`:

```csharp
using System.Linq;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class DocumentMapperFlipTests
{
    private static (BotEditorViewModel editor, ActionRegistry defs) NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return (new BotEditorViewModel(defs), defs);
    }

    [Fact]
    public void ToBot_Then_Populate_RoundTripsPortsFlipped()
    {
        var (editor, defs) = NewEditor();
        var node = editor.AddNode("control.delay", 0, 0);
        node.SetPortsFlipped(true);

        var bot = DocumentMapper.ToBot(editor);
        Assert.True(bot.Actions.Single().PortsFlipped);

        var (editor2, defs2) = NewEditor();
        DocumentMapper.Populate(editor2, bot, defs2);
        Assert.True(editor2.Nodes.Single().PortsFlipped);
        Assert.Equal(PortEdge.Left, editor2.Nodes.Single().OutputPorts[0].Edge);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~DocumentMapperFlipTests"`
Expected: FAIL — `BotAction.PortsFlipped` does not exist.

- [ ] **Step 3: Add the model field**

In `AdbCore/Models/BotAction.cs`, add after the `Retry` property:

```csharp
    /// <summary>True when the editor flipped this node's ports for a right-to-left serpentine band
    /// (inputs on the right, outputs on the left). Optional; defaults false for older files.</summary>
    public bool PortsFlipped { get; set; }
```

Also add it to the `CloneWithConfig` initializer so interpolated clones keep it:

```csharp
    public BotAction CloneWithConfig(Dictionary<string, object> config) => new()
    {
        Id = Id,
        TypeKey = TypeKey,
        Label = Label,
        TargetId = TargetId,
        Config = config,
        Retry = Retry,
        CanvasPosition = CanvasPosition,
        PortsFlipped = PortsFlipped,
    };
```

- [ ] **Step 4: Round-trip it in DocumentMapper**

In `DocumentMapper.ToBot`, add `PortsFlipped = node.PortsFlipped,` to the `new BotAction { ... }` initializer.

In `DocumentMapper.BuildNode`, add this just before `return node;` (after the retry lines, so it runs after any RunParallel branch rebuild):

```csharp
        if (action.PortsFlipped) { node.SetPortsFlipped(true); }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~DocumentMapperFlipTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add AdbCore/Models/BotAction.cs BotBuilder.Core/DocumentMapper.cs BotBuilder.Core.Tests/DocumentMapperFlipTests.cs
git commit -m "Persist PortsFlipped through the .bot round-trip"
```

---

## Task 3.4: Serpentine layout with fit-to-width in AutoLayout

**Files:**
- Modify: `BotBuilder.Core/Layout/AutoLayout.cs`
- Test: `BotBuilder.Core.Tests/AutoLayoutTests.cs` (add tests; existing tests keep compiling — `NodePlacement` exposes `.X`/`.Y`)

- [ ] **Step 1: Write failing tests**

Append to `AutoLayoutTests.cs`:

```csharp
    [Fact]
    public void LongChain_Serpentine_AlternatesFlip()
    {
        var ids = LinearChain(12, out var edges);
        var pos = AutoLayout.Arrange(ids.Select(id => N(id)).ToArray(), edges);

        Assert.False(pos[ids[0]].Flipped);              // band 0 is left-to-right
        Assert.Contains(pos.Values, p => p.Flipped);     // at least one reversed band
        Assert.Contains(pos.Values, p => !p.Flipped);
    }

    [Fact]
    public void FitToWidth_LimitsColumnsPerBand()
    {
        var ids = LinearChain(10, out var edges);
        var portEdges = edges.Select(e => (e.Item1, e.Item2, 0.0, 0.0)).ToArray();
        // Room for 4 columns: (4-1)*ColGap + card width.
        var target = 3 * AutoLayout.ColGap + 160;
        var pos = AutoLayout.Arrange(ids.Select(id => N(id)).ToArray(), portEdges, targetWidth: target);

        Assert.True(pos.Values.Select(p => p.X).Distinct().Count() <= 4);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AutoLayoutTests.LongChain_Serpentine_AlternatesFlip|FullyQualifiedName~AutoLayoutTests.FitToWidth_LimitsColumnsPerBand"`
Expected: FAIL — `NodePlacement.Flipped` / `targetWidth` parameter do not exist.

- [ ] **Step 3: Introduce `NodePlacement` and change the return type**

At the top of `AutoLayout.cs` (inside the namespace, above the class), add:

```csharp
/// <summary>A node's computed layout position plus whether its band runs right-to-left (ports flipped).</summary>
public readonly record struct NodePlacement(double X, double Y, bool Flipped);
```

Change both `Arrange` overload signatures to return placements and accept an optional width, and tighten the constants:

```csharp
    public const double ColGap = 200;
    public const double RowGap = 30;
    public const double BandGap = 48;     // vertical gap between wrapped row-bands
```

Back-compat overload:

```csharp
    public static IReadOnlyDictionary<Guid, NodePlacement> Arrange(
        IReadOnlyList<(Guid Id, double Height)> nodes,
        IReadOnlyList<(Guid Source, Guid Target)> edges,
        double? targetWidth = null)
        => Arrange(nodes, edges.Select(e => (e.Source, e.Target, 0.0, 0.0)).ToList(), targetWidth);
```

Port-aware overload signature:

```csharp
    public static IReadOnlyDictionary<Guid, NodePlacement> Arrange(
        IReadOnlyList<(Guid Id, double Height)> nodes,
        IReadOnlyList<(Guid Source, Guid Target, double SourcePortY, double TargetPortY)> edges,
        double? targetWidth = null)
    {
        var result = new Dictionary<Guid, NodePlacement>();
```

- [ ] **Step 4: Choose band width by width when supplied, and lay out serpentine**

Replace the band-width selection line (currently `var k = L <= NoWrapMaxLayers ? L : ChooseBandWidth(L, colHeight);`) with:

```csharp
        var k = targetWidth is double tw
            ? ChooseBandWidthForWidth(L, tw)
            : (L <= NoWrapMaxLayers ? L : ChooseBandWidth(L, colHeight));
```

Replace the final positioning loop (the `for (var l = 0; l < L; l++)` block that builds `result[id] = (x, y);`) with a serpentine version:

```csharp
        for (var l = 0; l < L; l++)
        {
            var band = l / k;
            var localCol = l % k;
            var colsInBand = Math.Min(k, L - band * k);
            var flipped = band % 2 == 1;
            var effectiveCol = flipped ? colsInBand - 1 - localCol : localCol;
            var x = OriginX + effectiveCol * ColGap;
            var y = bandTop[band];
            foreach (var id in layers[l])
            {
                result[id] = new NodePlacement(x, y, flipped);
                y += height[id] + RowGap;
            }
        }
        return result;
```

Add the width-based chooser (next to `ChooseBandWidth`):

```csharp
    /// <summary>Largest columns-per-band whose band width (<c>(k-1)*ColGap + NodeWidth</c>) still fits
    /// <paramref name="targetWidth"/>; at least 1, at most the layer count.</summary>
    private static int ChooseBandWidthForWidth(int layerCount, double targetWidth)
    {
        var best = 1;
        for (var k = 1; k <= layerCount; k++)
        {
            var width = (k - 1) * ColGap + NodeWidth;
            if (width <= targetWidth) { best = k; } else { break; }
        }
        return best;
    }
```

- [ ] **Step 5: Run the full AutoLayout test class**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AutoLayoutTests"`
Expected: PASS — the two new tests pass and the existing ones still pass (they read `.X`/`.Y`, which `NodePlacement` provides). If `LongChain_WrapsIntoStackedRows`'s `Count(p => p.X == OriginX) >= 2` fails due to the tighter `ColGap`, it should still hold because band 0 starts at `OriginX` and every even band restarts there; do not weaken the assertion without confirming a real regression.

- [ ] **Step 6: Commit**

```bash
git add BotBuilder.Core/Layout/AutoLayout.cs BotBuilder.Core.Tests/AutoLayoutTests.cs
git commit -m "Serpentine layout with fit-to-width band wrapping"
```

---

## Task 3.5: Apply flip in the editor (undoable) + direction-aware lane routing

**Files:**
- Modify: `BotBuilder.Core/Undo/EditorCommands.cs`
- Modify: `BotBuilder.Core/BotEditorViewModel.cs` (`AutoLayout`, `RerouteBackEdges`)
- Modify: `BotBuilder.Core/Connections/BackRoutePlanner.cs`
- Modify: `BotBuilder.Core/Connections/ConnectionViewModel.cs`
- Test: `BotBuilder.Core.Tests/AutoLayoutEditorTests.cs` (add a flip+undo test)

- [ ] **Step 1: Write failing test**

Append to `AutoLayoutEditorTests.cs` (use its existing editor-construction helper; if it builds an editor inline, mirror that). A self-contained version:

```csharp
    [Fact]
    public void AutoLayout_FlipsReversedBand_AndUndoRestoresFlip()
    {
        var defs = new AdbCore.Actions.ActionRegistry();
        AdbCore.Actions.BuiltIn.BuiltInActions.Register(defs, new AdbCore.Execution.ActionExecutorRegistry());
        var editor = new BotEditorViewModel(defs);

        // A 12-node chain wraps into serpentine bands; at least one node ends up flipped.
        BotBuilder.Core.NodeViewModel? prev = null;
        for (var i = 0; i < 12; i++)
        {
            var n = editor.AddNode("control.delay", 0, 0);
            if (prev is not null) { editor.Connect(prev, prev.OutputPorts[0], n, n.InputPorts[0]); }
            prev = n;
        }

        editor.AutoLayout();
        Assert.Contains(editor.Nodes, n => n.PortsFlipped);

        editor.Undo();
        Assert.All(editor.Nodes, n => Assert.False(n.PortsFlipped));
    }
```

Note: confirm the connect API name — if `Connect` differs, use the editor's existing connect method (grep `public.*Connect` in `BotEditorViewModel.cs`). The assertion set does not depend on the exact wiring beyond producing >4 layers.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AutoLayoutEditorTests.AutoLayout_FlipsReversedBand_AndUndoRestoresFlip"`
Expected: FAIL — `AutoLayout()` does not flip / undo does not restore flip.

- [ ] **Step 3: Add an undoable layout command**

In `EditorCommands.cs`, add below `MoveNodesCommand`:

```csharp
/// <summary>Applies a Tidy-Up layout (position + serpentine port-flip) as one undoable step.</summary>
internal sealed class LayoutNodesCommand : IUndoableCommand
{
    private readonly IReadOnlyList<(NodeViewModel Node, double OldX, double OldY, bool OldFlip, double NewX, double NewY, bool NewFlip)> _items;
    public LayoutNodesCommand(IReadOnlyList<(NodeViewModel, double, double, bool, double, double, bool)> items) { _items = items; }
    public void Do()   { foreach (var m in _items) { m.Node.X = m.NewX; m.Node.Y = m.NewY; m.Node.SetPortsFlipped(m.NewFlip); } }
    public void Undo() { foreach (var m in _items) { m.Node.X = m.OldX; m.Node.Y = m.OldY; m.Node.SetPortsFlipped(m.OldFlip); } }
}
```

- [ ] **Step 4: Rewrite `BotEditorViewModel.AutoLayout` to take a width and apply flip**

Replace the whole `AutoLayout()` method:

```csharp
    /// <summary>Re-arranges all nodes into a tidy serpentine layout, as one undoable step. When
    /// <paramref name="availableWidth"/> is given, bands wrap to fit that canvas width; otherwise a balanced
    /// aspect ratio is used.</summary>
    public void AutoLayout(double? availableWidth = null)
    {
        if (Nodes.Count == 0) return;
        var nodes = Nodes.Select(n => (n.Id, n.Height)).ToList();
        var edges = Connections
            .Select(c => (c.Source.Id, c.Target.Id, c.SourcePort.AnchorOffset.Y, c.TargetPort.AnchorOffset.Y))
            .ToList();
        var placements = BotBuilder.Core.Layout.AutoLayout.Arrange(nodes, edges, availableWidth);

        var items = new List<(NodeViewModel, double, double, bool, double, double, bool)>();
        foreach (var node in Nodes)
        {
            if (!placements.TryGetValue(node.Id, out var p)) continue;
            if (node.X == p.X && node.Y == p.Y && node.PortsFlipped == p.Flipped) continue;
            items.Add((node, node.X, node.Y, node.PortsFlipped, p.X, p.Y, p.Flipped));
        }
        if (items.Count == 0) return;

        foreach (var m in items) { m.Item1.X = m.Item5; m.Item1.Y = m.Item6; m.Item1.SetPortsFlipped(m.Item7); }
        _undo.PushExecuted(new LayoutNodesCommand(items));
        AfterEdit();
    }
```

(`AfterEdit()` already calls `RerouteBackEdges()`, so lanes recompute after the flip.)

- [ ] **Step 5: Make back-edge planning direction-aware**

In `BackRoutePlanner.cs`, add the source edge to the input record and filter by it:

Change the record:

```csharp
public readonly record struct BackRouteInput(Guid Id, double StartX, double StartY, double EndX, double EndY, PortEdge SourceEdge);
```

Change the filter in `Plan` (the `.Where(r => r.EndX < r.StartX)`):

```csharp
        var back = routes
            .Where(r => ConnectionGeometry.IsBackward(new CanvasPoint(r.StartX, r.StartY), r.SourceEdge, new CanvasPoint(r.EndX, r.EndY)))
            .OrderBy(r => Math.Abs(r.StartX - r.EndX))
            .ThenBy(r => r.StartY)
            .ThenBy(r => r.Id)
            .ToList();
```

The corridor math uses `nodesRightX + Margin + i*LaneGap` / `nodesLeftX - Margin - i*LaneGap`, which stays correct for both orientations. Add `using BotBuilder.Core;` at the top of `BackRoutePlanner.cs` if `CanvasPoint`/`PortEdge` aren't already in scope (they are in `BotBuilder.Core`; this file is `BotBuilder.Core.Connections`).

In `BotEditorViewModel.RerouteBackEdges`, pass the source edge when building inputs:

```csharp
            return new BackRouteInput(c.Id, s.Item1, s.Item2, t.Item1, t.Item2, c.SourcePort.Edge);
```

- [ ] **Step 6: Make the ConnectionViewModel lane gate direction-aware**

In `ConnectionViewModel.cs`, change the `PathData` getter's lane condition from `end.X < start.X` to the shared test:

```csharp
            if (_lane is { } lane && ConnectionGeometry.IsBackward(start, SourcePort.Edge, end))
                return ConnectionGeometry.BuildLanedBackRoute(
                    start, SourcePort.Edge, end, TargetPort.Edge,
                    lane.RightCornerX, lane.LeftCornerX, lane.GutterY);
```

- [ ] **Step 7: Run the affected tests**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AutoLayoutEditorTests|FullyQualifiedName~BackRoute|FullyQualifiedName~ConnectionGeometryTests"`
Expected: PASS — the new flip+undo test passes; existing back-route tests still pass (a normal Right-output back edge is still `IsBackward`).

- [ ] **Step 8: Run the whole suite**

Run: `dotnet test ADB.slnx`
Expected: PASS (nothing else regressed).

- [ ] **Step 9: Commit**

```bash
git add BotBuilder.Core/Undo/EditorCommands.cs BotBuilder.Core/BotEditorViewModel.cs BotBuilder.Core/Connections/BackRoutePlanner.cs BotBuilder.Core/Connections/ConnectionViewModel.cs BotBuilder.Core.Tests/AutoLayoutEditorTests.cs
git commit -m "Apply serpentine flip in the editor (undoable) with direction-aware lane routing"
```

---

## Task 3.6: Pass the canvas width into Tidy Up (WPF)

**Files:**
- Modify: `BotBuilder/MainWindow.xaml.cs` (the Tidy Up handler)

- [ ] **Step 1: Find the Tidy Up invocation**

Run: `rg -n "AutoLayout\(\)|Tidy" BotBuilder/MainWindow.xaml.cs`
Expected: a click/command handler calling `_editor.AutoLayout()`.

- [ ] **Step 2: Pass the canvas host's actual width**

Change that call to pass the canvas viewport width (the `ViewportHost` border spans exactly the region between the side panels):

```csharp
        _editor.AutoLayout(ViewportHost.ActualWidth > 0 ? ViewportHost.ActualWidth : (double?)null);
```

- [ ] **Step 3: Build & smoke-run**

Run: `dotnet build ADB.slnx`
Expected: builds. Then `dotnet run --project BotBuilder`, drop a long chain of nodes, press Tidy Up: the flow should snake (alternate rows reverse direction), stay within the canvas width, and re-wrap after collapsing a panel (Part 4) or resizing and re-running.

- [ ] **Step 4: Commit**

```bash
git add BotBuilder/MainWindow.xaml.cs
git commit -m "Tidy Up wraps to the canvas width"
```

---

# PART 4 — Collapsible toolbox / properties panels

## Task 4.1: Persist collapse state in AppSettings

**Files:**
- Modify: `AdbUi.Theme/AppSettings.cs`
- Test: `AdbUi.Theme.Tests/AppSettingsCollapseTests.cs` (create)

- [ ] **Step 1: Write failing test**

Create `AdbUi.Theme.Tests/AppSettingsCollapseTests.cs`:

```csharp
using System.IO;
using AdbUi.Theme;
using Xunit;

namespace AdbUi.Theme.Tests;

public class AppSettingsCollapseTests
{
    [Fact]
    public void RoundTrips_PanelCollapseFlags_AndPreservesOtherFields()
    {
        var path = Path.Combine(Path.GetTempPath(), "adb-settings-" + System.Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new JsonSettingsStore(path);
            var loaded = store.Load();
            store.Save(loaded with { ToolboxCollapsed = true, PropertiesCollapsed = true });

            var again = new JsonSettingsStore(path).Load();
            Assert.True(again.ToolboxCollapsed);
            Assert.True(again.PropertiesCollapsed);
            Assert.Equal(loaded.Theme, again.Theme);                       // untouched field preserved
            Assert.Equal(loaded.ExternalEditorCommand, again.ExternalEditorCommand);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Defaults_AreFalse()
    {
        var s = new AppSettings();
        Assert.False(s.ToolboxCollapsed);
        Assert.False(s.PropertiesCollapsed);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AppSettingsCollapseTests"`
Expected: FAIL — the two properties don't exist.

- [ ] **Step 3: Add the fields**

In `AdbUi.Theme/AppSettings.cs`, add:

```csharp
    /// <summary>Whether the BotBuilder toolbox (left palette) panel is collapsed to a rail.</summary>
    public bool ToolboxCollapsed { get; init; }

    /// <summary>Whether the BotBuilder properties (right) panel is collapsed to a rail.</summary>
    public bool PropertiesCollapsed { get; init; }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AppSettingsCollapseTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add AdbUi.Theme/AppSettings.cs AdbUi.Theme.Tests/AppSettingsCollapseTests.cs
git commit -m "Add toolbox/properties collapse flags to AppSettings"
```

---

## Task 4.2: Collapse UI in MainWindow.xaml

**Files:**
- Modify: `BotBuilder/MainWindow.xaml` (columns 198-201; palette col0 ~204; properties col2 ~376)

- [ ] **Step 1: Name the columns and the two panels**

Change the `Grid.ColumnDefinitions` (lines 198-201) to name the side columns:

```xml
            <Grid.ColumnDefinitions>
                <ColumnDefinition x:Name="ToolboxColumn" Width="220" />
                <ColumnDefinition Width="*" />
                <ColumnDefinition x:Name="PropertiesColumn" Width="240" />
            </Grid.ColumnDefinitions>
```

Give the palette root (`<DockPanel Grid.Column="0" ...>`) `x:Name="ToolboxPanel"` and the properties root (`<Border Grid.Column="2" ...>`) `x:Name="PropertiesPanel"`.

- [ ] **Step 2: Add a chevron toggle to each panel header**

Inside `ToolboxPanel`, at the very top (DockPanel — add a small header docked top), add a right-aligned collapse button:

```xml
                <Button DockPanel.Dock="Top" Content="&#x2039;" Click="ToggleToolbox_Click"
                        HorizontalAlignment="Right" Padding="6,0" Margin="0,0,2,2"
                        ToolTip="Collapse toolbox (Ctrl+[)" AutomationProperties.Name="Collapse toolbox" />
```

Inside `PropertiesPanel`'s top-level content, add a left-aligned collapse button (chevron pointing right):

```xml
                <Button Content="&#x203A;" Click="ToggleProperties_Click"
                        HorizontalAlignment="Left" Padding="6,0" Margin="2,0,0,2"
                        ToolTip="Collapse properties (Ctrl+])" AutomationProperties.Name="Collapse properties" />
```

(Place it as the first child of the properties `Grid` so it sits at the top-left; adjust the surrounding `RowDefinitions` if needed so it doesn't overlap existing content.)

- [ ] **Step 3: Add the collapsed rails**

Add two rail borders as siblings in the same grid columns, initially collapsed. After `ToolboxPanel`:

```xml
            <Border x:Name="ToolboxRail" Grid.Column="0" Visibility="Collapsed"
                    Background="{DynamicResource PanelBackgroundBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="0,0,1,0">
                <Button Content="&#x203A;" Click="ToggleToolbox_Click" VerticalAlignment="Top" Margin="0,6,0,0"
                        ToolTip="Expand toolbox (Ctrl+[)" AutomationProperties.Name="Expand toolbox">
                    <Button.LayoutTransform><RotateTransform Angle="0" /></Button.LayoutTransform>
                </Button>
            </Border>
```

After `PropertiesPanel`:

```xml
            <Border x:Name="PropertiesRail" Grid.Column="2" Visibility="Collapsed"
                    Background="{DynamicResource PanelBackgroundBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1,0,0,0">
                <Button Content="&#x2039;" Click="ToggleProperties_Click" VerticalAlignment="Top" Margin="0,6,0,0"
                        ToolTip="Expand properties (Ctrl+])" AutomationProperties.Name="Expand properties" />
            </Border>
```

(The rails share their grid cell with the full panels; visibility is mutually exclusive, driven from code-behind. Keep them after the panels in XAML so they render on top when visible.)

- [ ] **Step 4: Build to verify XAML compiles**

Run: `dotnet build ADB.slnx`
Expected: builds (the `Click` handlers are added in the next task; if the build fails on missing handlers, do Task 4.3 first, then build).

- [ ] **Step 5: Commit**

```bash
git add BotBuilder/MainWindow.xaml
git commit -m "Add collapse chevrons and rails to the side panels"
```

---

## Task 4.3: Collapse toggle, persistence, and shortcuts (code-behind)

**Files:**
- Modify: `BotBuilder/MainWindow.xaml.cs`

- [ ] **Step 1: Add fields + toggle/apply/persist methods**

Add near the other private fields:

```csharp
    private const double ToolboxWidthPx = 220;
    private const double PropertiesWidthPx = 240;
    private const double RailWidthPx = 24;
```

Add these methods (anywhere in the class body):

```csharp
    private void ToggleToolbox_Click(object sender, RoutedEventArgs e)
        => SetToolboxCollapsed(ToolboxRail.Visibility != Visibility.Visible);

    private void ToggleProperties_Click(object sender, RoutedEventArgs e)
        => SetPropertiesCollapsed(PropertiesRail.Visibility != Visibility.Visible);

    private void SetToolboxCollapsed(bool collapsed)
    {
        ToolboxColumn.Width = collapsed ? new GridLength(RailWidthPx) : new GridLength(ToolboxWidthPx);
        ToolboxPanel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        ToolboxRail.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
        PersistPanelState();
    }

    private void SetPropertiesCollapsed(bool collapsed)
    {
        PropertiesColumn.Width = collapsed ? new GridLength(RailWidthPx) : new GridLength(PropertiesWidthPx);
        PropertiesPanel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        PropertiesRail.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
        PersistPanelState();
    }

    private void PersistPanelState()
    {
        if (_isChild) return;   // only the root window owns the persisted layout
        var store = ((App)Application.Current).Settings;
        store.Save(store.Load() with
        {
            ToolboxCollapsed = ToolboxRail.Visibility == Visibility.Visible,
            PropertiesCollapsed = PropertiesRail.Visibility == Visibility.Visible,
        });
    }

    private void ApplySavedPanelState()
    {
        var s = ((App)Application.Current).Settings.Load();
        SetToolboxCollapsedNoPersist(s.ToolboxCollapsed);
        SetPropertiesCollapsedNoPersist(s.PropertiesCollapsed);
    }

    private void SetToolboxCollapsedNoPersist(bool collapsed)
    {
        ToolboxColumn.Width = collapsed ? new GridLength(RailWidthPx) : new GridLength(ToolboxWidthPx);
        ToolboxPanel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        ToolboxRail.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetPropertiesCollapsedNoPersist(bool collapsed)
    {
        PropertiesColumn.Width = collapsed ? new GridLength(RailWidthPx) : new GridLength(PropertiesWidthPx);
        PropertiesPanel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        PropertiesRail.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
    }
```

- [ ] **Step 2: Apply saved state at startup (root only)**

In the root constructor (`public MainWindow()`), after `_nestedEditors = ...;`, add:

```csharp
        Loaded += (_, _) => ApplySavedPanelState();
```

- [ ] **Step 3: Add keyboard shortcuts**

Locate `Window_KeyDown` (the existing key handler, ~line 440). Add these cases (Ctrl+[ / Ctrl+]):

```csharp
            if (e.Key == Key.OemOpenBrackets && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ToggleToolbox_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }
            if (e.Key == Key.OemCloseBrackets && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ToggleProperties_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }
```

- [ ] **Step 4: Add View-menu items**

In `MainWindow.xaml`, in the View menu (find the menu with theme items), add:

```xml
                <Separator />
                <MenuItem Header="Toggle _Toolbox" InputGestureText="Ctrl+[" Click="ToggleToolbox_Click" />
                <MenuItem Header="Toggle _Properties" InputGestureText="Ctrl+]" Click="ToggleProperties_Click" />
```

- [ ] **Step 5: Build and smoke-test**

Run: `dotnet build ADB.slnx`
Expected: builds. Then `dotnet run --project BotBuilder`: chevrons collapse each panel to a rail; the rail's chevron re-expands; Ctrl+[ / Ctrl+] toggle; state survives an app restart; collapsing widens the canvas and a Tidy Up re-run uses the extra width.

- [ ] **Step 6: Commit**

```bash
git add BotBuilder/MainWindow.xaml BotBuilder/MainWindow.xaml.cs
git commit -m "Wire panel collapse: toggle, persistence, shortcuts, View menu"
```

---

# PART 5 — Unsaved-changes prompt

## Task 5.1: UnsavedChangesGuard core helper

**Files:**
- Create: `BotBuilder.Core/UnsavedChangesGuard.cs`
- Test: `BotBuilder.Core.Tests/UnsavedChangesGuardTests.cs`

- [ ] **Step 1: Write failing tests**

Create `BotBuilder.Core.Tests/UnsavedChangesGuardTests.cs`:

```csharp
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class UnsavedChangesGuardTests
{
    [Fact]
    public void NotDirty_ProceedsWithoutAsking()
    {
        var asked = false;
        var ok = UnsavedChangesGuard.ConfirmProceed(() => false, () => { asked = true; return SaveChoice.Cancel; }, () => true);
        Assert.True(ok);
        Assert.False(asked);
    }

    [Fact]
    public void Save_Succeeds_Proceeds()
        => Assert.True(UnsavedChangesGuard.ConfirmProceed(() => true, () => SaveChoice.Save, () => true));

    [Fact]
    public void Save_Cancelled_Aborts()
        => Assert.False(UnsavedChangesGuard.ConfirmProceed(() => true, () => SaveChoice.Save, () => false));

    [Fact]
    public void DontSave_Proceeds()
        => Assert.True(UnsavedChangesGuard.ConfirmProceed(() => true, () => SaveChoice.DontSave, () => false));

    [Fact]
    public void Cancel_Aborts()
        => Assert.False(UnsavedChangesGuard.ConfirmProceed(() => true, () => SaveChoice.Cancel, () => true));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~UnsavedChangesGuardTests"`
Expected: FAIL — `UnsavedChangesGuard` / `SaveChoice` don't exist.

- [ ] **Step 3: Create the helper**

Create `BotBuilder.Core/UnsavedChangesGuard.cs`:

```csharp
namespace BotBuilder.Core;

/// <summary>The user's answer to "save changes before continuing?".</summary>
public enum SaveChoice { Save, DontSave, Cancel }

/// <summary>Decides whether a New/Open/close should proceed when the document may have unsaved changes.
/// Pure orchestration so it is unit-testable without WPF: the caller supplies the dirty check, the prompt,
/// and the save action (which returns whether the save actually completed).</summary>
public static class UnsavedChangesGuard
{
    /// <summary>Returns true to proceed, false to abort. Prompts only when dirty; on Save, proceeds only if
    /// <paramref name="save"/> returns true (e.g. the user didn't cancel the file dialog).</summary>
    public static bool ConfirmProceed(System.Func<bool> isDirty, System.Func<SaveChoice> ask, System.Func<bool> save)
    {
        if (!isDirty()) return true;
        return ask() switch
        {
            SaveChoice.Save => save(),
            SaveChoice.DontSave => true,
            _ => false,
        };
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~UnsavedChangesGuardTests"`
Expected: PASS (all five).

- [ ] **Step 5: Commit**

```bash
git add BotBuilder.Core/UnsavedChangesGuard.cs BotBuilder.Core.Tests/UnsavedChangesGuardTests.cs
git commit -m "Add UnsavedChangesGuard core helper"
```

---

## Task 5.2: Wire the guard into New / Open / window close

**Files:**
- Modify: `BotBuilder/MainWindow.xaml.cs`

- [ ] **Step 1: Add the WPF glue methods**

Add to `MainWindow.xaml.cs`:

```csharp
    /// <summary>Root-window guard: prompt to save if dirty before a New/Open/close. Returns true to proceed.</summary>
    private bool ConfirmDiscardIfDirty()
        => UnsavedChangesGuard.ConfirmProceed(() => _editor.IsDirty, AskSaveChanges, TrySaveForGuard);

    private SaveChoice AskSaveChanges()
    {
        var result = MessageBox.Show(this,
            $"Save changes to \"{_editor.BotName}\" before continuing?",
            "ADB Bot Builder", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        return result switch
        {
            MessageBoxResult.Yes => SaveChoice.Save,
            MessageBoxResult.No => SaveChoice.DontSave,
            _ => SaveChoice.Cancel,
        };
    }

    private bool TrySaveForGuard()
    {
        if (_editor.FilePath is not null) { _editor.Save(); return true; }
        if (PromptForBotPath() is string path) { _editor.SaveAsNew(path); return true; }
        return false;   // user cancelled the path dialog -> abort the New/Open/close
    }
```

Add `using BotBuilder.Core;` if not present (it is — the file already uses `BotBuilder.Core`).

- [ ] **Step 2: Guard New and Open**

In `New_Click`, after the `if (_isChild) { return; }` line, add:

```csharp
        if (!ConfirmDiscardIfDirty()) return;
```

Do the same in `Open_Click`, after its `if (_isChild) { return; }` line (before the `OpenFileDialog`).

- [ ] **Step 3: Guard window close (root only)**

Add an `OnClosing` override:

```csharp
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_isChild) return;   // child windows sync back automatically; nothing to lose to disk
        if (!ConfirmDiscardIfDirty()) e.Cancel = true;
    }
```

- [ ] **Step 4: Build and smoke-test**

Run: `dotnet build ADB.slnx`
Expected: builds. Then `dotnet run --project BotBuilder`: make an edit, then File>New / File>Open / close the window — a Save/Don't Save/Cancel prompt appears; Cancel aborts; Save on a never-saved bot prompts for a path and aborts if that dialog is cancelled; a clean (non-dirty) document never prompts; opening/closing a nested-bot child window never prompts.

- [ ] **Step 5: Commit**

```bash
git add BotBuilder/MainWindow.xaml.cs
git commit -m "Prompt to save unsaved changes on New/Open/close"
```

---

# Documentation (all three parts)

## Task D: Docs sync

**Files:** `CLAUDE.md`, `README.md`, `ADB.wiki/*`

- [ ] **Step 1: CLAUDE.md**

- In the `.bot File Format` → Notes list, document the new optional field:

```markdown
- **`portsFlipped`** (per action, optional, default `false`) records that "Tidy Up" placed the node in a
  right-to-left serpentine band, so its ports render input-right / output-left. Persisted so a saved tidy
  graph reloads clean.
```

- In the Theme/Settings section, note the two new settings:

```markdown
- `AppSettings` also carries `ToolboxCollapsed` / `PropertiesCollapsed` (BotBuilder side-panel rail state).
```

- Add a one-line note near the canvas/editor description that Tidy Up now produces a serpentine layout that
  wraps to the canvas width, and that side panels collapse to rails (Ctrl+[ / Ctrl+]).

- [ ] **Step 2: README.md**

Add, in the editor-features area (goblin voice intact), lines for the serpentine Tidy Up, collapsible panels, and the save-before-you-rage-quit prompt. Example:

```markdown
- **Tidy Up** now snakes long bot graphs into tidy rows that fit your canvas, and the toolbox/properties panels fold away (Ctrl+[ / Ctrl+]) when you need elbow room.
- ADB now nags you to save before you slam New, Open, or the close button on unsaved work.
```

- [ ] **Step 3: Wiki (submodule)**

Edit under `ADB.wiki/`: document (a) the serpentine Tidy Up + fit-to-width behavior and the `portsFlipped` field in the Bot-File-Format page, (b) collapsible panels + their shortcuts and persisted settings, and (c) the unsaved-changes prompt on New/Open/close. Then:

```bash
cd ADB.wiki
git add -A
git commit -m "Docs: serpentine Tidy Up, collapsible panels, unsaved-changes prompt"
git push
cd ..
git add ADB.wiki CLAUDE.md README.md
git commit -m "Docs: serpentine Tidy Up, collapsible panels, unsaved-changes prompt (+ wiki pointer)"
```

- [ ] **Step 4: Full verification**

Run: `dotnet test ADB.slnx`
Expected: PASS (full suite). Then `dotnet build ADB.slnx` clean.

---

# Finish: open the PR (do not merge)

- [ ] Push the branch and open a PR bundling Parts 3–5. Body should link the spec and summarize the three
  features. **Do not merge** — this is parked for the user's visual review.

```bash
git push -u origin feat/editor-usability-visual
gh pr create --title "Editor usability: serpentine Tidy Up, collapsible panels, unsaved-changes prompt" --body "Implements items 3-5 of Docs/superpowers/specs/2026-07-04-editor-usability-batch-design.md.

- Serpentine Tidy Up with per-band port flip, direction-aware routing, and fit-to-canvas-width wrapping (portsFlipped persisted in .bot).
- Collapsible toolbox/properties panels (rails, Ctrl+[ / Ctrl+], persisted in settings.json).
- Unsaved-changes prompt on New/Open/close.

https://claude.ai/code/session_01UvcnQr4NvWncn1DeKnC38a"
```

---

## Self-Review Notes

- **Spec coverage:** item 3 → Tasks 3.1-3.6 + D; item 4 → Tasks 4.1-4.3 + D; item 5 → Tasks 5.1-5.2 + D. All covered.
- **Type consistency:** `IsBackward(CanvasPoint, PortEdge, CanvasPoint)` used in ConnectionGeometry, BackRoutePlanner, ConnectionViewModel. `NodePlacement(X,Y,Flipped)` keeps existing `pos[x].X/.Y` accessors compiling. `SetPortsFlipped(bool)` / `PortsFlipped` used in NodeViewModel, DocumentMapper, LayoutNodesCommand. `SaveChoice { Save, DontSave, Cancel }` + `ConfirmProceed(Func<bool>,Func<SaveChoice>,Func<bool>)` consistent between helper, tests, and MainWindow. `ToolboxCollapsed`/`PropertiesCollapsed` consistent across AppSettings, tests, MainWindow.
- **Placeholder scan:** two spots depend on names to verify at execution (`editor.Connect` in Task 3.5 Step 1, and the exact Tidy Up handler in Task 3.6 Step 1) — each has an explicit `rg`/grep step to confirm before use, not a silent assumption.
- **Risk:** existing `AutoLayoutTests.LongChain_WrapsIntoStackedRows` may be sensitive to the tighter `ColGap`; Task 3.4 Step 5 calls this out explicitly and forbids weakening the assertion without confirming a real regression.
