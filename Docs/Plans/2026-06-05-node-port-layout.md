# Node Port Layout + Failure-Down Connectors Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Node cards grow to fit their right-edge ports (centered block; fixes Run Parallel's spilling branches), and failure outputs (`onFailure`, `someFailed`) drop to the bottom edge with direction-aware connectors.

**Architecture:** Geometry lives in `NodeLayout` (shared by view + connection anchors). Ports carry a `PortEdge`. `NodeViewModel` derives each port's edge + anchor and an observable `Height`, recomputing on Run Parallel branch-count changes. `ConnectionGeometry` pulls each curve along its port's edge normal. All in `BotBuilder*`; no AdbCore/engine change.

**Tech Stack:** C# / .NET 10, BotBuilder.Core (+ BotBuilder WPF for the one XAML binding), xUnit.

**Reference spec:** `Docs/Specs/2026-06-05-node-port-layout-design.md`.

**Merge handling:** logic unit-tested, but the payoff is visual → **user-verified PR, not self-merged.** Independent of PR #36.

**`<WT>` = `C:\git\ADB\.claude\worktrees\node-port-layout`. Windows: PowerShell for `dotnet`/`git`; NEVER redirect to `/dev/null`/`NUL`.**

**Geometry constants (existing in `NodeLayout`): `CardWidth=160`, `CardHeight=70` (becomes the default/min), `HeaderHeight=28`, `PortSpacing=20`, `PortRadius=5`. New: `BodyPad=11` (tuned so 1–2 right-port nodes stay 70px).**

---

## Task 1: `PortEdge` + `NodeLayout` geometry (pure, testable)

**Files:** Create `BotBuilder.Core/PortEdge.cs`; modify `BotBuilder.Core/NodeLayout.cs`; modify `BotBuilder.Core.Tests/NodeLayoutTests.cs`.

- [ ] **Step 1: Read** the current `NodeLayout.cs` and `NodeLayoutTests.cs` to see existing members/tests (`InputAnchor`/`OutputAnchor` take only an index today; tests assert those). You will REPLACE the index-only anchors with count/height-aware ones and update the tests.

- [ ] **Step 2: Create `BotBuilder.Core/PortEdge.cs`:**
```csharp
namespace BotBuilder.Core;

/// <summary>Which edge of a node card a port sits on (determines its anchor and the connector's
/// outgoing direction). Inputs are Left; failure outputs (onFailure/someFailed) are Bottom; all other
/// outputs are Right.</summary>
public enum PortEdge { Left, Right, Bottom }
```

- [ ] **Step 3: Write the failing tests.** Replace/extend `NodeLayoutTests.cs` (adapt to the existing test class; keep its namespace/usings). Cover the new API:
```csharp
using Xunit;
namespace BotBuilder.Core.Tests;

public class NodeLayoutTests
{
    [Theory]
    [InlineData(1, 70)]
    [InlineData(2, 70)]
    [InlineData(3, 90)]   // 28 + (3-1)*20 + 2*11 = 90
    [InlineData(4, 110)]
    public void CardHeight_GrowsForThreeOrMoreRightPorts(int rightCount, double expected)
        => Assert.Equal(expected, NodeLayout.CardHeight(rightCount));

    [Fact]
    public void RightBlock_IsVerticallyCentered_AndSymmetric()
    {
        var h = NodeLayout.CardHeight(2);
        var a0 = NodeLayout.RightAnchor(0, 2, h);
        var a1 = NodeLayout.RightAnchor(1, 2, h);
        Assert.Equal(NodeLayout.CardWidth, a0.X);
        Assert.Equal(NodeLayout.PortSpacing, a1.Y - a0.Y);          // adjacent ports one spacing apart
        var centerY = NodeLayout.HeaderHeight + (h - NodeLayout.HeaderHeight) / 2;
        Assert.Equal(centerY, (a0.Y + a1.Y) / 2);                   // block centered on body
    }

    [Fact]
    public void SingleInput_LandsAtBodyCenter()
    {
        var h = NodeLayout.CardHeight(1);
        var inp = NodeLayout.LeftAnchor(0, 1, h);
        var centerY = NodeLayout.HeaderHeight + (h - NodeLayout.HeaderHeight) / 2;
        Assert.Equal(0, inp.X);
        Assert.Equal(centerY, inp.Y);
    }

    [Fact]
    public void BottomAnchor_SingleIsBottomCenter()
    {
        var h = NodeLayout.CardHeight(1);
        var b = NodeLayout.BottomAnchor(0, 1, h);
        Assert.Equal(NodeLayout.CardWidth / 2, b.X);
        Assert.Equal(h, b.Y);
    }

    [Fact]
    public void BottomAnchor_DistributesHorizontally()
    {
        var h = NodeLayout.CardHeight(1);
        var b0 = NodeLayout.BottomAnchor(0, 2, h);
        var b1 = NodeLayout.BottomAnchor(1, 2, h);
        Assert.True(b0.X < NodeLayout.CardWidth / 2 && b1.X > NodeLayout.CardWidth / 2);
        Assert.Equal(h, b0.Y);
    }

    [Theory]
    [InlineData(PortEdge.Left, -1, 0)]
    [InlineData(PortEdge.Right, 1, 0)]
    [InlineData(PortEdge.Bottom, 0, 1)]
    public void Outward_Normals(PortEdge edge, double nx, double ny)
    {
        var n = NodeLayout.Outward(edge);
        Assert.Equal(nx, n.X);
        Assert.Equal(ny, n.Y);
    }
}
```

- [ ] **Step 4: Run to verify it fails** — `dotnet test "<WT>\BotBuilder.Core.Tests" --filter "FullyQualifiedName~NodeLayoutTests"` → compile FAIL.

- [ ] **Step 5: Rewrite `BotBuilder.Core/NodeLayout.cs`:**
```csharp
namespace BotBuilder.Core;

/// <summary>Shared geometry for node cards, used by both the view (rendering) and the core (connection
/// anchor math) so endpoints line up with rendered ports. Cards grow to fit their right-edge ports; each
/// edge's ports form a block centered on the card body; failure outputs sit on the bottom edge.</summary>
public static class NodeLayout
{
    public const double CardWidth = 160;
    public const double CardHeight_Default = 70;
    public const double HeaderHeight = 28;
    public const double PortSpacing = 20;
    public const double PortRadius = 5;
    public const double BodyPad = 11;

    /// <summary>Card height needed to fit <paramref name="rightCount"/> centered right-edge ports
    /// (bottom-edge ports do not affect height). Never smaller than the default 70.</summary>
    public static double CardHeight(int rightCount)
    {
        var needed = HeaderHeight + Math.Max(0, rightCount - 1) * PortSpacing + 2 * BodyPad;
        return Math.Max(CardHeight_Default, needed);
    }

    private static double CenterY(double height) => HeaderHeight + (height - HeaderHeight) / 2;
    private static double BlockY(int index, int count, double height)
        => CenterY(height) - (count - 1) * PortSpacing / 2 + index * PortSpacing;

    public static CanvasPoint LeftAnchor(int index, int count, double height) => new(0, BlockY(index, count, height));
    public static CanvasPoint RightAnchor(int index, int count, double height) => new(CardWidth, BlockY(index, count, height));

    /// <summary>Bottom-edge anchor for failure port <paramref name="index"/> of <paramref name="count"/>,
    /// distributed evenly across the card's bottom edge.</summary>
    public static CanvasPoint BottomAnchor(int index, int count, double height)
        => new(CardWidth * (index + 1) / (count + 1), height);

    /// <summary>Unit outward normal for a port edge (the direction a connector leaves/approaches the port).</summary>
    public static CanvasPoint Outward(PortEdge edge) => edge switch
    {
        PortEdge.Left => new(-1, 0),
        PortEdge.Right => new(1, 0),
        PortEdge.Bottom => new(0, 1),
        _ => new(1, 0),
    };
}
```
NOTE: `CardHeight` was previously a `const`. It is now a method; update EVERY reference (`MarqueeSelection`, any test, the XAML/code-behind) — the const is renamed `CardHeight_Default`. The compiler will list all references; fix each (MarqueeSelection is handled in Task 4).

- [ ] **Step 6: Run to verify it passes** — `--filter "FullyQualifiedName~NodeLayoutTests"` → green. (Other projects may not compile yet — that's fine; subsequent tasks fix references. To keep this task's gate runnable, you may temporarily build just the test's dependencies; otherwise proceed and the full build is gated in Task 4. If the BotBuilder.Core project itself fails to compile due to `MarqueeSelection`/`NodeViewModel` using the old API, make the MINIMAL reference fixes needed to compile `BotBuilder.Core` — e.g. point `MarqueeSelection` at `CardHeight(...)` provisionally — and note them; Task 4 finalizes them.)

- [ ] **Step 7: Commit:**
```
git -C "<WT>" add BotBuilder.Core/PortEdge.cs BotBuilder.Core/NodeLayout.cs BotBuilder.Core.Tests/NodeLayoutTests.cs
git -C "<WT>" commit -m "feat(canvas): grown card height + centered/edge anchor geometry"
```

---

## Task 2: `PortViewModel.Edge` + `NodeViewModel` (height, edges, recompute)

**Files:** modify `BotBuilder.Core/PortViewModel.cs`, `BotBuilder.Core/NodeViewModel.cs`; modify `BotBuilder.Core.Tests/NodeViewModelPortsTests.cs` and `NodeViewModelTests.cs`.

- [ ] **Step 1: Read** `NodeViewModel.cs`, `PortViewModel.cs`, `NodeViewModelPortsTests.cs`, `NodeViewModelTests.cs`.

- [ ] **Step 2: Add `Edge` to `PortViewModel`** — new ctor param + property (keep `Name`, `Direction`, `AnchorOffset`):
```csharp
    public PortViewModel(string name, PortDirection direction, PortEdge edge, CanvasPoint anchorOffset)
    {
        Name = name;
        Direction = direction;
        Edge = edge;
        AnchorOffset = anchorOffset;
    }
    public string Name { get; }
    public PortDirection Direction { get; }
    public PortEdge Edge { get; }
    public CanvasPoint AnchorOffset { get; private set; }
    /// <summary>Re-place this port (used when a node's layout recomputes, e.g. Run Parallel branch count).</summary>
    public void MoveTo(CanvasPoint anchorOffset) => AnchorOffset = anchorOffset;
```

- [ ] **Step 3: Write the failing tests.** Extend `NodeViewModelPortsTests.cs` (adapt to existing style):
```csharp
    [Fact]
    public void FromDefinition_FailureOutputs_GoToBottom_OthersRight_InputsLeft()
    {
        var def = new AdbCore.Actions.BuiltIn.FindImageAction(/* ctor deps as the existing tests use */);
        var node = NodeViewModel.FromDefinition(def, System.Guid.NewGuid(), "", 0, 0);
        Assert.All(node.InputPorts, p => Assert.Equal(PortEdge.Left, p.Edge));
        var success = System.Linq.Enumerable.Single(node.OutputPorts, p => p.Name == "onSuccess");
        var failure = System.Linq.Enumerable.Single(node.OutputPorts, p => p.Name == "onFailure");
        Assert.Equal(PortEdge.Right, success.Edge);
        Assert.Equal(PortEdge.Bottom, failure.Edge);
    }

    [Fact]
    public void RunParallel_Branches_AreRightEdge_AndCardGrows()
    {
        var node = /* build a Run Parallel node via FromDefinition(new RunParallelAction(), ...) */;
        node.SetBranchPortCount(4);
        Assert.All(node.OutputPorts, p => Assert.Equal(PortEdge.Right, p.Edge));
        Assert.Equal(NodeLayout.CardHeight(4), node.Height);
        // 4 ports centered & symmetric:
        var ys = System.Linq.Enumerable.Select(node.OutputPorts, p => p.AnchorOffset.Y).ToList();
        var centerY = NodeLayout.HeaderHeight + (node.Height - NodeLayout.HeaderHeight) / 2;
        Assert.Equal(centerY, (ys[0] + ys[3]) / 2, 3);
    }

    [Fact]
    public void Join_SomeFailed_GoesToBottom()
    {
        var node = NodeViewModel.FromDefinition(new AdbCore.Actions.BuiltIn.JoinAction(), System.Guid.NewGuid(), "", 0, 0);
        var someFailed = System.Linq.Enumerable.Single(node.OutputPorts, p => p.Name == "someFailed");
        var allSucceeded = System.Linq.Enumerable.Single(node.OutputPorts, p => p.Name == "allSucceeded");
        Assert.Equal(PortEdge.Bottom, someFailed.Edge);
        Assert.Equal(PortEdge.Right, allSucceeded.Edge);
    }
```
(ADAPT the action constructors to how the existing tests build defs — `FindImageAction`/`RunParallelAction`/`JoinAction` may need injected deps; reuse the existing test helpers. If `FindImageAction` needs a capturer/matcher, pick any action with onSuccess/onFailure that the existing tests already construct.)

- [ ] **Step 4: Run to verify it fails.**

- [ ] **Step 5: Implement in `NodeViewModel.cs`:**
  - Add `[ObservableProperty] private double _height;` (CommunityToolkit) — observable so the view + connections react.
  - A failure-name set + classifier:
```csharp
    private static readonly HashSet<string> FailurePortNames = new(StringComparer.Ordinal)
        { "onFailure", "someFailed" };
    private static PortEdge OutputEdge(string portName) =>
        FailurePortNames.Contains(portName) ? PortEdge.Bottom : PortEdge.Right;
```
  - Rework `FromDefinition` to classify edges and lay out via `NodeLayout`:
```csharp
    public static NodeViewModel FromDefinition(IActionDefinition definition, Guid id, string label, double x, double y)
    {
        var rightNames = definition.OutputPorts.Where(p => OutputEdge(p.Name) == PortEdge.Right).Select(p => p.Name).ToList();
        var bottomNames = definition.OutputPorts.Where(p => OutputEdge(p.Name) == PortEdge.Bottom).Select(p => p.Name).ToList();
        var height = NodeLayout.CardHeight(rightNames.Count);

        var inputs = definition.InputPorts
            .Select((p, i) => new PortViewModel(p.Name, PortDirection.In, PortEdge.Left, NodeLayout.LeftAnchor(i, definition.InputPorts.Count, height)))
            .ToList();

        var outputs = new List<PortViewModel>();
        for (var i = 0; i < rightNames.Count; i++)
            outputs.Add(new PortViewModel(rightNames[i], PortDirection.Out, PortEdge.Right, NodeLayout.RightAnchor(i, rightNames.Count, height)));
        for (var j = 0; j < bottomNames.Count; j++)
            outputs.Add(new PortViewModel(bottomNames[j], PortDirection.Out, PortEdge.Bottom, NodeLayout.BottomAnchor(j, bottomNames.Count, height)));

        var node = new NodeViewModel(id, definition.TypeKey, string.IsNullOrEmpty(label) ? definition.DisplayName : label,
            definition.Category, inputs, outputs, x, y);
        node.Height = height;
        return node;
    }
```
  - The Run Parallel branch helpers must recompute via `NodeLayout` and keep `Height` in sync. Replace `BranchOutputPort`/`SetBranchPortCount` so they go through a single `RecomputeBranchLayout(count)`:
```csharp
    /// <summary>Sets the Run Parallel output ports to exactly `count` right-edge branch ports,
    /// re-centering them and growing/shrinking the card height. (All Run Parallel outputs are right-edge.)</summary>
    public void SetBranchPortCount(int count)
    {
        var height = NodeLayout.CardHeight(count);
        // Reuse existing port instances where possible to preserve connections' endpoint identity.
        while (OutputPorts.Count < count)
            OutputPorts.Add(new PortViewModel(RunParallelAction.BranchPort(OutputPorts.Count + 1), PortDirection.Out, PortEdge.Right, default));
        while (OutputPorts.Count > count)
            OutputPorts.RemoveAt(OutputPorts.Count - 1);
        for (var i = 0; i < OutputPorts.Count; i++)
            OutputPorts[i].MoveTo(NodeLayout.RightAnchor(i, count, height));
        // also re-center the input port(s) on the new height
        for (var i = 0; i < InputPorts.Count; i++)
            InputPorts[i].MoveTo(NodeLayout.LeftAnchor(i, InputPorts.Count, height));
        Height = height;
    }
```
  - `ReplaceOutputPorts` (used by the undo command) must also reset `Height` from the new right-port count and re-anchor — simplest: have the undo command call `SetBranchPortCount(newCount)` instead of swapping raw port lists (see Task 4), OR make `ReplaceOutputPorts` recompute height from `ports.Count`. Pick the approach that keeps the undo command correct; the cleanest is to drive both do/undo through `SetBranchPortCount`. Note your choice.
  - NOTE: `InputPorts` is currently `IReadOnlyList<PortViewModel>` — that's fine; `MoveTo` mutates the instances in place. If the existing code stored inputs as immutable with get-only anchor, the new `MoveTo` enables in-place re-centering.

- [ ] **Step 6: Run to verify it passes** (`--filter "FullyQualifiedName~NodeViewModel"`), iterating until green.

- [ ] **Step 7: Commit:**
```
git -C "<WT>" add BotBuilder.Core/PortViewModel.cs BotBuilder.Core/NodeViewModel.cs BotBuilder.Core.Tests/NodeViewModelPortsTests.cs BotBuilder.Core.Tests/NodeViewModelTests.cs
git -C "<WT>" commit -m "feat(canvas): node Height + per-edge port layout + branch recompute"
```

---

## Task 3: Direction-aware `ConnectionGeometry` + `ConnectionViewModel`

**Files:** modify `BotBuilder.Core/Connections/ConnectionGeometry.cs`, `BotBuilder.Core/Connections/ConnectionViewModel.cs`; modify `BotBuilder.Core.Tests/ConnectionGeometryTests.cs`, `ConnectionViewModelTests.cs`.

- [ ] **Step 1: Read** the current `ConnectionGeometry.cs`, `ConnectionViewModel.cs`, and their tests.

- [ ] **Step 2: Write failing tests** (adapt `ConnectionGeometryTests.cs`): right-source pulls +X (a horizontal connector's first control point has the same Y as the start and X > start.X — back-compat), bottom-source pulls +Y (control point has X == start.X and Y > start.Y), left-target pulls −X (last control point X < end.X). Also a `ConnectionViewModelTests` case: changing an endpoint node's `Height` raises `PathData`.
```csharp
    [Fact]
    public void RightSource_PullsHorizontally()
    {
        var (c1, _) = ConnectionGeometry.ControlPoints(new(160, 50), PortEdge.Right, new(300, 90), PortEdge.Left);
        Assert.True(c1.X > 160);
        Assert.Equal(50, c1.Y);
    }

    [Fact]
    public void BottomSource_PullsDownward()
    {
        var (c1, _) = ConnectionGeometry.ControlPoints(new(80, 70), PortEdge.Bottom, new(80, 220), PortEdge.Left);
        Assert.Equal(80, c1.X);
        Assert.True(c1.Y > 70);
    }
```

- [ ] **Step 3: Implement edge-aware geometry:**
```csharp
    public static (CanvasPoint C1, CanvasPoint C2) ControlPoints(
        CanvasPoint start, PortEdge startEdge, CanvasPoint end, PortEdge endEdge)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var pull = Math.Max(MinPull, Math.Sqrt(dx * dx + dy * dy) / 2);
        var s = NodeLayout.Outward(startEdge);
        var e = NodeLayout.Outward(endEdge);
        return (new CanvasPoint(start.X + s.X * pull, start.Y + s.Y * pull),
                new CanvasPoint(end.X + e.X * pull, end.Y + e.Y * pull));
    }

    public static string BuildPath(CanvasPoint start, PortEdge startEdge, CanvasPoint end, PortEdge endEdge)
    {
        var (c1, c2) = ControlPoints(start, startEdge, end, endEdge);
        return string.Create(CultureInfo.InvariantCulture,
            $"M {start.X},{start.Y} C {c1.X},{c1.Y} {c2.X},{c2.Y} {end.X},{end.Y}");
    }
```
(Rename `MinHorizontalPull` → `MinPull` or keep; keep `=40`.)

- [ ] **Step 4: Update `ConnectionViewModel`** to pass edges and re-route on `Height`:
  - `PathData => ConnectionGeometry.BuildPath(Anchor(Source, SourcePort), SourcePort.Edge, Anchor(Target, TargetPort), TargetPort.Edge);`
  - In `OnEndpointMoved`, recompute when `e.PropertyName` is `X`, `Y`, **or `Height`** (a Run Parallel re-center moves ports without moving the node).

- [ ] **Step 5: Run** `--filter "FullyQualifiedName~ConnectionGeometry|FullyQualifiedName~ConnectionViewModel"` → green.

- [ ] **Step 6: Commit:**
```
git -C "<WT>" add BotBuilder.Core/Connections/ConnectionGeometry.cs BotBuilder.Core/Connections/ConnectionViewModel.cs BotBuilder.Core.Tests/ConnectionGeometryTests.cs BotBuilder.Core.Tests/ConnectionViewModelTests.cs
git -C "<WT>" commit -m "feat(canvas): direction-aware connectors (failure paths exit downward)"
```

---

## Task 4: Editor recompute wiring + MarqueeSelection + XAML + full sweep

**Files:** modify `BotBuilder.Core/Canvas/MarqueeSelection.cs`, `BotBuilder.Core/BotEditorViewModel.cs`, `BotBuilder.Core/Undo/EditorCommands.cs`, `BotBuilder.Core/DocumentMapper.cs`, `BotBuilder/MainWindow.xaml`; touch tests as needed.

- [ ] **Step 1: `MarqueeSelection`** — hit-test per-node height:
```csharp
        return nodes.Where(n =>
            n.X < right && n.X + NodeLayout.CardWidth > x &&
            n.Y < bottom && n.Y + n.Height > y);
```
Add/adjust a test: a grown (e.g. branch-count 4) node selected when the marquee overlaps only its lower half. (Adapt existing marquee tests if any assert `CardHeight`.)

- [ ] **Step 2: Branch-count recompute through the editor.** Read `BotEditorViewModel.OnBranchCountChanged` + `Undo/EditorCommands.SetBranchCountCommand` + `DocumentMapper`. Ensure all three drive `NodeViewModel.SetBranchPortCount(newCount)` (which now also re-centers + sets `Height`), so do/undo/load keep ports, height, and connection paths consistent. If `SetBranchCountCommand` currently swaps raw port lists via `ReplaceOutputPorts`, change `Do()`/`Undo()` to call `SetBranchPortCount(_newCount)` / `SetBranchPortCount(_oldCount)` (it preserves surviving port instances so the orphaned-connection removal/re-add logic still lines up — verify against the command's `_removedConnections` handling; keep that logic). `DocumentMapper` already calls `SetBranchPortCount(branches)` on load — confirm it now also sets `Height` (it will, via the reworked method).

- [ ] **Step 3: XAML height binding.** In `BotBuilder/MainWindow.xaml`, change the node `Border` from `Width="160" MinHeight="70"` to `Width="160" Height="{Binding Height}"`. Verify the ports `ItemsControl` (a `Canvas` overlay positioned by `AnchorOffset`) still renders all ports — bottom ports now get a bottom-edge `AnchorOffset` and place themselves; no template change needed. (No unit test — visual verify.)

- [ ] **Step 4: Full build + test sweep.** `dotnet build "<WT>\ADB.slnx" -warnaserror -v q --nologo` → 0 warnings (fix any remaining references to the old `CardHeight` const / `OutputAnchor`/`InputAnchor` / 3-arg `PortViewModel` / 2-arg `BuildPath` across the solution and tests). `dotnet test "<WT>\ADB.slnx"` → all green. Report totals. Pay attention to: any other call sites of `NodeViewModel.FromDefinition`, `PortViewModel` ctor, `ConnectionGeometry.BuildPath`, `NodeLayout.OutputAnchor/InputAnchor` (now removed) in non-test code (e.g. connection creation in `BotEditorViewModel`, drag-connect hit-testing in the WPF code-behind) — update them to the new signatures/edges.

- [ ] **Step 5: Commit:**
```
git -C "<WT>" add -A
git -C "<WT>" commit -m "feat(canvas): bind node height in view, per-node marquee, editor recompute wiring"
```

---

## Self-Review Notes (addressed)

- **Spec coverage:** grown/centered card (Task 1–2), per-edge ports incl. failure→bottom (Task 2), direction-aware connectors (Task 3), editor/undo/load recompute + view height binding + marquee (Task 4). ✓
- **Run Parallel fix:** branches no longer spill — card grows + centers (Task 2 `SetBranchPortCount`); connectors re-route on `Height` change (Task 3). ✓
- **Failure-down:** `onFailure`/`someFailed` classified to bottom edge, connectors pull downward. ✓
- **Back-compat:** right→left connectors unchanged (same horizontal pull); no `.bot` schema change; 1–2 right-port nodes stay 70px. ✓
- **Type consistency:** `PortEdge`, `PortViewModel(name,dir,edge,anchor)`+`MoveTo`, `NodeLayout.CardHeight(int)`/`LeftAnchor/RightAnchor/BottomAnchor(i,count,height)`/`Outward(edge)`, `NodeViewModel.Height`, `ConnectionGeometry.ControlPoints/BuildPath(start,edge,end,edge)`. ✓
- **No AdbCore/engine change; BotBuilder-only → user-verified PR.**
