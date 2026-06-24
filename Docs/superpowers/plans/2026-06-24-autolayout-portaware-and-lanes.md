# Tidy Up round 2: Port-Aware Ordering + Back-Route Lane Separation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Eliminate the two remaining Tidy Up defects the user found: (1) a Branch node's two output wires cross because the layout is port-blind, and (2) return/loop wires lie on top of each other because every back-route shares the same corridor.

**Architecture:**
- **Part A — port-aware ordering.** `AutoLayout.Arrange` currently takes `(Guid Source, Guid Target)` edges and breaks within-layer barycenter ties by raw input order. Children of a single parent (e.g. Branch's two targets) therefore tie and get placed in creation order, unrelated to which output port feeds them → crossing. Fix: carry each edge's **source/target port Y** into `Arrange`, and break barycenter ties by the feeding-port height so the child wired from the higher port is placed higher.
- **Part B — back-route lane separation.** Add a pure `BackRoutePlanner` (global knowledge of all back-routes) that assigns each backward edge (target anchor X < source anchor X) a **lane**: a distinct right-side corridor X, a distinct left-side corridor X, and a gutter Y. `ConnectionGeometry` gains a laned back-route builder; `ConnectionViewModel` carries the assigned corner/gutter; `BotEditorViewModel.RerouteBackEdges()` runs the planner and applies it, called from `AfterEdit()`, `AutoLayout()` (via AfterEdit), and `LoadFrom()`. Lanes are derived view state — not serialized, not undoable.

**Tech Stack:** C# net10.0-windows, xUnit, pure `BotBuilder.Core` (`Layout/AutoLayout.cs`, `Connections/ConnectionGeometry.cs`, `Connections/ConnectionViewModel.cs`, `BotEditorViewModel.cs`). No new deps, no `.bot` schema change.

**Branch:** continue on `worktree-autolayout-wrap-and-uncross` (parked PR #69).

---

## File Structure

- **Modify:** `BotBuilder.Core/Layout/AutoLayout.cs` — port-aware edges + tie-break (Task A).
- **Modify:** `BotBuilder.Core/BotEditorViewModel.cs` — pass port Y to `Arrange` (Task A); add `RerouteBackEdges()` and call sites (Task B3).
- **Modify:** `BotBuilder.Core/DocumentMapper.cs` — reroute after load (Task B3).
- **Create:** `BotBuilder.Core/Connections/BackRoutePlanner.cs` — pure lane assignment (Task B1).
- **Modify:** `BotBuilder.Core/Connections/ConnectionGeometry.cs` — laned back-route path (Task B2).
- **Modify:** `BotBuilder.Core/Connections/ConnectionViewModel.cs` — carry lane corner/gutter, fold into `PathData` (Task B2).
- **Tests:** `BotBuilder.Core.Tests/AutoLayoutTests.cs`, `ConnectionGeometryTests.cs`, new `BackRoutePlannerTests.cs`, `BotEditorViewModelTests` (existing file if present, else new `BackRouteRoutingTests.cs`).

**Preserve:** all existing tests stay green. The existing `Arrange(nodes, (Guid,Guid)[])` call shape must keep compiling (overload), so the current AutoLayout tests are untouched.

---

## Task A: Port-aware within-layer ordering (fixes Branch crossing)

**Files:**
- Modify: `BotBuilder.Core/Layout/AutoLayout.cs`
- Modify: `BotBuilder.Core/BotEditorViewModel.cs`
- Test: `BotBuilder.Core.Tests/AutoLayoutTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `AutoLayoutTests.cs`. This models Branch: one parent `br` with two outputs to two siblings `t0`,`t1` in the next column. The parent's **top** output port (smaller Y) feeds `t1`, the **bottom** port (larger Y) feeds `t0`. Creation order is `t0` then `t1`, so naive tie-break would put `t0` on top — crossing. Port-aware ordering must place `t1` (fed by the top port) on top.

```csharp
    [Fact]
    public void PortAware_OrdersSiblingsByFeedingPort()
    {
        var br = Guid.NewGuid();
        var t0 = Guid.NewGuid();   // created first; fed by the LOWER port
        var t1 = Guid.NewGuid();   // created second; fed by the UPPER port
        // edges: (source, target, sourcePortY, targetPortY)
        var pos = AutoLayout.Arrange(
            new[] { N(br), N(t0), N(t1) },
            new[]
            {
                (br, t0, 60.0, 35.0),   // br's lower output port (Y=60) -> t0
                (br, t1, 20.0, 35.0),   // br's upper output port (Y=20) -> t1
            });

        Assert.Equal(pos[t0].X, pos[t1].X);     // same column
        Assert.True(pos[t1].Y < pos[t0].Y);     // t1 (upper port) placed above t0 -> uncrossed
    }
```

- [ ] **Step 2: Run test to verify it fails (compile error first, then assertion)**

Run: `dotnet test BotBuilder.Core.Tests/BotBuilder.Core.Tests.csproj --filter "FullyQualifiedName~PortAware_OrdersSiblingsByFeedingPort"`
Expected: FAILS — first because `Arrange` has no 4-tuple overload; after Step 3 wiring, because without the port tie-break `t0` (created first) sorts above `t1`.

- [ ] **Step 3: Add the port-aware overload + tie-break in `AutoLayout.cs`**

Replace the `Arrange` method signature and the adjacency/cycle/barycenter sections. Concretely:

(a) Add a backward-compatible overload and make the existing one delegate. Find the current method header:

```csharp
    public static IReadOnlyDictionary<Guid, (double X, double Y)> Arrange(
        IReadOnlyList<(Guid Id, double Height)> nodes,
        IReadOnlyList<(Guid Source, Guid Target)> edges)
    {
```

Replace it with an overload that maps to the richer one, plus the richer signature:

```csharp
    /// <summary>Back-compat overload: edges without port positions (port Y defaults to 0, so ties fall
    /// back to input order exactly as before).</summary>
    public static IReadOnlyDictionary<Guid, (double X, double Y)> Arrange(
        IReadOnlyList<(Guid Id, double Height)> nodes,
        IReadOnlyList<(Guid Source, Guid Target)> edges)
        => Arrange(nodes, edges.Select(e => (e.Source, e.Target, 0.0, 0.0)).ToList());

    /// <summary>Port-aware layout: each edge carries the source/target port Y (relative to the card top),
    /// so siblings fed from a single parent are ordered by feeding-port height (prevents crossed outputs).</summary>
    public static IReadOnlyDictionary<Guid, (double X, double Y)> Arrange(
        IReadOnlyList<(Guid Id, double Height)> nodes,
        IReadOnlyList<(Guid Source, Guid Target, double SourcePortY, double TargetPortY)> edges)
    {
```

(b) Change the adjacency build to carry port Y. Replace:

```csharp
        // adjacency over edges whose endpoints are both real nodes
        var adj = ids.ToDictionary(id => id, _ => new List<Guid>());
        foreach (var (s, t) in edges)
            if (idSet.Contains(s) && idSet.Contains(t) && s != t) adj[s].Add(t);
```

with (an edge record carrying the neighbor + both port Ys):

```csharp
        // adjacency over edges whose endpoints are both real nodes, carrying port Y for ordering
        var adj = ids.ToDictionary(id => id, _ => new List<(Guid To, double SrcY, double TgtY)>());
        foreach (var (s, t, sy, ty) in edges)
            if (idSet.Contains(s) && idSet.Contains(t) && s != t) adj[s].Add((t, sy, ty));
```

(c) Update cycle removal to keep port Y on the kept forward edges. Replace:

```csharp
        var forward = ids.ToDictionary(id => id, _ => new List<Guid>());
        var state = new Dictionary<Guid, int>();           // 0=unvisited,1=on-stack,2=done
        foreach (var id in ids) state[id] = 0;
        void Dfs(Guid u)
        {
            state[u] = 1;
            foreach (var v in adj[u])
            {
                if (state[v] == 1) continue;               // back-edge -> skip for layering
                forward[u].Add(v);
                if (state[v] == 0) Dfs(v);
            }
            state[u] = 2;
        }
```

with:

```csharp
        var forward = ids.ToDictionary(id => id, _ => new List<(Guid To, double SrcY, double TgtY)>());
        var state = new Dictionary<Guid, int>();           // 0=unvisited,1=on-stack,2=done
        foreach (var id in ids) state[id] = 0;
        void Dfs(Guid u)
        {
            state[u] = 1;
            foreach (var e in adj[u])
            {
                if (state[e.To] == 1) continue;            // back-edge -> skip for layering
                forward[u].Add(e);
                if (state[e.To] == 0) Dfs(e.To);
            }
            state[u] = 2;
        }
```

(d) Update everywhere `forward[u]` is iterated for layering (indeg + Kahn). Replace:

```csharp
        var indeg = ids.ToDictionary(id => id, _ => 0);
        foreach (var u in ids) foreach (var v in forward[u]) indeg[v]++;
        var layer = ids.ToDictionary(id => id, _ => 0);
        var queue = new Queue<Guid>(ids.Where(id => indeg[id] == 0).OrderBy(i => order[i]));
        while (queue.Count > 0)
        {
            var u = queue.Dequeue();
            foreach (var v in forward[u])
            {
                if (layer[v] < layer[u] + 1) layer[v] = layer[u] + 1;
                if (--indeg[v] == 0) queue.Enqueue(v);
            }
        }
```

with (note `e.To`):

```csharp
        var indeg = ids.ToDictionary(id => id, _ => 0);
        foreach (var u in ids) foreach (var e in forward[u]) indeg[e.To]++;
        var layer = ids.ToDictionary(id => id, _ => 0);
        var queue = new Queue<Guid>(ids.Where(id => indeg[id] == 0).OrderBy(i => order[i]));
        while (queue.Count > 0)
        {
            var u = queue.Dequeue();
            foreach (var e in forward[u])
            {
                if (layer[e.To] < layer[u] + 1) layer[e.To] = layer[u] + 1;
                if (--indeg[e.To] == 0) queue.Enqueue(e.To);
            }
        }
```

(e) Build predecessor edges carrying source port Y, and update the barycenter to use a port tie-break. Replace:

```csharp
        // predecessors over the forward DAG (for up-sweeps)
        var preds = ids.ToDictionary(id => id, _ => new List<Guid>());
        foreach (var u in ids) foreach (var v in forward[u]) preds[v].Add(u);

        var posInLayer = new Dictionary<Guid, int>();
        foreach (var lyr in layers) for (var i = 0; i < lyr.Count; i++) posInLayer[lyr[i]] = i;

        // 4) barycenter crossing reduction: alternate down-sweeps (order by predecessor positions)
        //    and up-sweeps (order by successor positions). Nodes with no neighbors keep their index
        //    (stable); ties break by original input order so the result is deterministic.
        void SortLayer(List<Guid> lyr, IReadOnlyDictionary<Guid, List<Guid>> neighbors)
        {
            double Bary(Guid id)
            {
                var ns = neighbors[id];
                return ns.Count == 0 ? posInLayer[id] : ns.Average(n => (double)posInLayer[n]);
            }
            lyr.Sort((x, y) =>
            {
                var cmp = Bary(x).CompareTo(Bary(y));
                return cmp != 0 ? cmp : order[x].CompareTo(order[y]);
            });
            for (var i = 0; i < lyr.Count; i++) posInLayer[lyr[i]] = i;
        }

        for (var pass = 0; pass < BarycenterPasses; pass++)
        {
            if (pass % 2 == 0)
                for (var l = 1; l <= maxLayer; l++) SortLayer(layers[l], preds);
            else
                for (var l = maxLayer - 1; l >= 0; l--) SortLayer(layers[l], forward);
        }
```

with (predecessors carry the source port Y; the barycenter tie-break is the average feeding-port Y — source-port Y when ordering a layer by its predecessors, target-port Y when ordering by successors):

```csharp
        // predecessors over the forward DAG (for up-sweeps), carrying the edge's port Ys
        var preds = ids.ToDictionary(id => id, _ => new List<(Guid From, double SrcY, double TgtY)>());
        foreach (var u in ids) foreach (var e in forward[u]) preds[e.To].Add((u, e.SrcY, e.TgtY));

        var posInLayer = new Dictionary<Guid, int>();
        foreach (var lyr in layers) for (var i = 0; i < lyr.Count; i++) posInLayer[lyr[i]] = i;

        // 4) barycenter crossing reduction: alternate down-sweeps (order layer by predecessor positions)
        //    and up-sweeps (order by successor positions). Primary key = avg neighbor index; ties break by
        //    the average feeding-port Y (so two children of one parent sort by which output port feeds
        //    them — prevents crossed outputs); final tie-break = original input order (determinism).
        //    `usePortY` selects which port end matters: when ordering a layer by its PREDECESSORS we use the
        //    SOURCE port Y (down-sweep, neighbor = parent); by its SUCCESSORS we use the TARGET port Y.
        void SortLayer(List<Guid> lyr, IReadOnlyDictionary<Guid, List<(Guid Node, double SrcY, double TgtY)>> neighbors, bool down)
        {
            double Bary(Guid id)
            {
                var ns = neighbors[id];
                return ns.Count == 0 ? posInLayer[id] : ns.Average(n => (double)posInLayer[n.Node]);
            }
            double PortKey(Guid id)
            {
                var ns = neighbors[id];
                return ns.Count == 0 ? 0.0 : ns.Average(n => down ? n.SrcY : n.TgtY);
            }
            lyr.Sort((x, y) =>
            {
                var cmp = Bary(x).CompareTo(Bary(y));
                if (cmp != 0) return cmp;
                cmp = PortKey(x).CompareTo(PortKey(y));
                return cmp != 0 ? cmp : order[x].CompareTo(order[y]);
            });
            for (var i = 0; i < lyr.Count; i++) posInLayer[lyr[i]] = i;
        }

        for (var pass = 0; pass < BarycenterPasses; pass++)
        {
            if (pass % 2 == 0)
                for (var l = 1; l <= maxLayer; l++) SortLayer(layers[l], preds, down: true);
            else
                for (var l = maxLayer - 1; l >= 0; l--) SortLayer(layers[l], forward.ToDictionary(kv => kv.Key, kv => kv.Value.Select(e => (e.To, e.SrcY, e.TgtY)).ToList()), down: false);
        }
```

NOTE on the up-sweep call: `forward` is keyed `(Guid To, double SrcY, double TgtY)` but `SortLayer`'s neighbor tuple is named `(Guid Node, double SrcY, double TgtY)`. Tuple element names don't affect assignment compatibility in C# (positional), but the `.ToDictionary(... .Select(e => (e.To, e.SrcY, e.TgtY)) ...)` projection above rebuilds it cleanly. To avoid rebuilding a dictionary every odd pass, the implementer MAY instead declare both `forward` and `preds` with identically-named tuple fields `(Guid Node, double SrcY, double TgtY)` from the start and pass `forward` directly. Prefer that cleaner approach: name the forward/adjacency tuple field `Node` (not `To`) everywhere and pass `forward` directly to `SortLayer` in the up-sweep. Keep layering code referring to `e.Node`.

- [ ] **Step 4: Make the editor pass port Y. In `BotEditorViewModel.AutoLayout()` replace the edges line**

Find (around line 166):
```csharp
        var edges = Connections.Select(c => (c.Source.Id, c.Target.Id)).ToList();
```
Replace with:
```csharp
        var edges = Connections
            .Select(c => (c.Source.Id, c.Target.Id, c.SourcePort.AnchorOffset.Y, c.TargetPort.AnchorOffset.Y))
            .ToList();
```

- [ ] **Step 5: Run the new test + full AutoLayout suite**

Run: `dotnet test BotBuilder.Core.Tests/BotBuilder.Core.Tests.csproj --filter "FullyQualifiedName~AutoLayout"`
Expected: PASS — `PortAware_OrdersSiblingsByFeedingPort` plus all existing AutoLayout/Editor tests (the back-compat overload keeps them passing). Then run the full suite `dotnet test BotBuilder.Core.Tests/BotBuilder.Core.Tests.csproj` — 0 failures.

- [ ] **Step 6: Commit**

```bash
git add BotBuilder.Core/Layout/AutoLayout.cs BotBuilder.Core/BotEditorViewModel.cs BotBuilder.Core.Tests/AutoLayoutTests.cs
git commit -m "Tidy Up: port-aware within-layer ordering (uncross Branch outputs)"
```

---

## Task B1: Pure `BackRoutePlanner` (lane assignment)

**Files:**
- Create: `BotBuilder.Core/Connections/BackRoutePlanner.cs`
- Test: `BotBuilder.Core.Tests/BackRoutePlannerTests.cs`

A back-route is a connection whose target anchor X is left of its source anchor X. The planner takes all candidate routes plus the layout's horizontal extent and returns, per back-route id, a **right corridor X**, a **left corridor X**, and a **gutter Y** — each lane nested so no two routes share a corridor or gutter.

- [ ] **Step 1: Write the failing tests**

Create `BotBuilder.Core.Tests/BackRoutePlannerTests.cs`:

```csharp
using System;
using System.Linq;
using BotBuilder.Core.Connections;
using Xunit;

namespace BotBuilder.Core.Tests;

public class BackRoutePlannerTests
{
    private static BackRouteInput In(Guid id, double sx, double sy, double ex, double ey)
        => new(id, sx, sy, ex, ey);

    [Fact]
    public void OnlyBackwardEdgesGetLanes()
    {
        var fwd = Guid.NewGuid(); var back = Guid.NewGuid();
        var plans = BackRoutePlanner.Plan(
            new[] { In(fwd, 0, 0, 100, 0), In(back, 100, 0, 0, 50) },
            nodesLeftX: 0, nodesRightX: 260);

        Assert.False(plans.ContainsKey(fwd));   // forward edge: not a back-route
        Assert.True(plans.ContainsKey(back));   // backward edge: laned
    }

    [Fact]
    public void EachBackRouteGetsADistinctCorridorAndGutter()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        var plans = BackRoutePlanner.Plan(
            new[]
            {
                In(a, 500, 100, 40, 300),
                In(b, 500, 110, 40, 310),
                In(c, 500, 120, 40, 320),
            },
            nodesLeftX: 40, nodesRightX: 660);

        var rightXs = plans.Values.Select(p => p.RightCornerX).ToList();
        var leftXs  = plans.Values.Select(p => p.LeftCornerX).ToList();
        var gutters = plans.Values.Select(p => p.GutterY).ToList();

        Assert.Equal(3, rightXs.Distinct().Count());   // no shared right corridor
        Assert.Equal(3, leftXs.Distinct().Count());    // no shared left corridor
        Assert.Equal(3, gutters.Distinct().Count());   // no shared gutter row
        Assert.All(rightXs, x => Assert.True(x > 660)); // right corridors sit right of all nodes
        Assert.All(leftXs,  x => Assert.True(x < 40));  // left corridors sit left of all nodes
    }

    [Fact]
    public void WiderSpanGetsOuterLane()
    {
        var narrow = Guid.NewGuid(); var wide = Guid.NewGuid();
        var plans = BackRoutePlanner.Plan(
            new[]
            {
                In(narrow, 300, 100, 200, 300),   // span 100
                In(wide,   500, 100, 40, 300),    // span 460
            },
            nodesLeftX: 40, nodesRightX: 660);

        // Wider span routes farther out on both sides (outer lane).
        Assert.True(plans[wide].RightCornerX > plans[narrow].RightCornerX);
        Assert.True(plans[wide].LeftCornerX  < plans[narrow].LeftCornerX);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test BotBuilder.Core.Tests/BotBuilder.Core.Tests.csproj --filter "FullyQualifiedName~BackRoutePlannerTests"`
Expected: FAIL to compile — `BackRouteInput`, `BackRoutePlan`, `BackRoutePlanner` don't exist yet.

- [ ] **Step 3: Implement `BackRoutePlanner.cs`**

Create `BotBuilder.Core/Connections/BackRoutePlanner.cs`:

```csharp
namespace BotBuilder.Core.Connections;

/// <summary>A candidate connection for back-route planning: absolute source/target anchor positions.</summary>
public readonly record struct BackRouteInput(Guid Id, double StartX, double StartY, double EndX, double EndY);

/// <summary>The lane a back-route was assigned: the vertical corridor X on each side and the gutter Y for
/// its horizontal run. Distinct per route so no two back-routes overlap.</summary>
public readonly record struct BackRoutePlan(double RightCornerX, double LeftCornerX, double GutterY);

/// <summary>Assigns each backward connection (target left of source) its own nested lane: a right-side
/// corridor, a left-side corridor, and a gutter row, so return/loop wires never lie on top of each other.
/// Pure and deterministic — narrower spans nest inside wider ones.</summary>
public static class BackRoutePlanner
{
    public const double Margin = 40;       // gap from the node block to the first corridor
    public const double LaneGap = 18;      // horizontal spacing between corridors
    public const double GutterStep = 16;   // vertical spacing between gutter rows

    public static IReadOnlyDictionary<Guid, BackRoutePlan> Plan(
        IReadOnlyList<BackRouteInput> routes, double nodesLeftX, double nodesRightX)
    {
        var result = new Dictionary<Guid, BackRoutePlan>();

        // Backward edges only (target strictly left of source), ordered narrowest-span first so the
        // narrowest nests in the innermost lane and wider spans wrap around the outside.
        var back = routes
            .Where(r => r.EndX < r.StartX)
            .OrderBy(r => r.StartX - r.EndX)
            .ThenBy(r => r.StartY)
            .ThenBy(r => r.Id)
            .ToList();

        for (var i = 0; i < back.Count; i++)
        {
            var r = back[i];
            var rightX = nodesRightX + Margin + i * LaneGap;
            var leftX = nodesLeftX - Margin - i * LaneGap;
            // base gutter midway between the two rows, then a per-lane step so equal-row pairs separate.
            var gutterY = (r.StartY + r.EndY) / 2 + i * GutterStep;
            result[r.Id] = new BackRoutePlan(rightX, leftX, gutterY);
        }

        return result;
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test BotBuilder.Core.Tests/BotBuilder.Core.Tests.csproj --filter "FullyQualifiedName~BackRoutePlannerTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add BotBuilder.Core/Connections/BackRoutePlanner.cs BotBuilder.Core.Tests/BackRoutePlannerTests.cs
git commit -m "Tidy Up: BackRoutePlanner assigns nested lanes to return wires"
```

---

## Task B2: Laned back-route geometry + ConnectionViewModel carrier

**Files:**
- Modify: `BotBuilder.Core/Connections/ConnectionGeometry.cs`
- Modify: `BotBuilder.Core/Connections/ConnectionViewModel.cs`
- Test: `BotBuilder.Core.Tests/ConnectionGeometryTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `ConnectionGeometryTests.cs`:

```csharp
    [Fact]
    public void LanedBackRoute_UsesGivenCornersAndGutter()
    {
        // start right -> end left, routed via explicit right/left corridors and a gutter Y.
        var path = ConnectionGeometry.BuildLanedBackRoute(
            new CanvasPoint(500, 100), PortEdge.Right,
            new CanvasPoint(40, 300), PortEdge.Left,
            rightCornerX: 720, leftCornerX: 10, gutterY: 215);

        Assert.StartsWith("M 500,100 ", path);
        Assert.Contains(" L ", path);
        Assert.DoesNotContain(" C ", path);
        Assert.Contains("720,", path);   // right corridor used
        Assert.Contains("10,", path);    // left corridor used
        Assert.Contains(",215", path);   // gutter row used
        Assert.EndsWith(" 40,300", path);
    }
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test BotBuilder.Core.Tests/BotBuilder.Core.Tests.csproj --filter "FullyQualifiedName~LanedBackRoute_UsesGivenCornersAndGutter"`
Expected: FAIL to compile — `BuildLanedBackRoute` doesn't exist.

- [ ] **Step 3: Add `BuildLanedBackRoute` to `ConnectionGeometry.cs`**

Add this public method (next to `BuildBackRoute`):

```csharp
    /// <summary>Orthogonal back-route through an explicitly assigned lane: out along the source normal to
    /// the right corridor, down/up to the gutter row, across to the left corridor, then up/down into the
    /// target. The corridors and gutter come from <see cref="BackRoutePlanner"/> so parallel return wires
    /// never coincide.</summary>
    public static string BuildLanedBackRoute(
        CanvasPoint start, PortEdge startEdge, CanvasPoint end, PortEdge endEdge,
        double rightCornerX, double leftCornerX, double gutterY)
    {
        var s = NodeLayout.Outward(startEdge);
        var e = NodeLayout.Outward(endEdge);
        var sy = start.Y + s.Y * BackRoutePull;   // a short stub along the source normal (handles Bottom ports)
        var ey = end.Y + e.Y * BackRoutePull;
        return string.Create(CultureInfo.InvariantCulture,
            $"M {start.X},{start.Y} L {start.X},{sy} L {rightCornerX},{sy} L {rightCornerX},{gutterY} " +
            $"L {leftCornerX},{gutterY} L {leftCornerX},{ey} L {end.X},{ey} L {end.X},{end.Y}");
    }
```

(Note: for a Right source the stub `s.Y` is 0, so `sy == start.Y` and the first `L` is a no-op-length segment to `start.X,start.Y` then straight out to the corridor — valid. For a Bottom failure-port source `s.Y == 1`, the stub drops down first. Same for the target side.)

- [ ] **Step 4: Wire the lane into `ConnectionViewModel`**

In `ConnectionViewModel.cs`, add lane carrier fields and fold them into `PathData`. Replace the `PathData` property:

```csharp
    public string PathData => ConnectionGeometry.BuildPath(
        Anchor(Source, SourcePort), SourcePort.Edge, Anchor(Target, TargetPort), TargetPort.Edge);
```

with:

```csharp
    private BackRoutePlan? _lane;

    public string PathData
    {
        get
        {
            var start = Anchor(Source, SourcePort);
            var end = Anchor(Target, TargetPort);
            if (_lane is { } lane && end.X < start.X)
                return ConnectionGeometry.BuildLanedBackRoute(
                    start, SourcePort.Edge, end, TargetPort.Edge,
                    lane.RightCornerX, lane.LeftCornerX, lane.GutterY);
            return ConnectionGeometry.BuildPath(start, SourcePort.Edge, end, TargetPort.Edge);
        }
    }

    /// <summary>Assigns (or clears) this connection's back-route lane and re-renders its path. Lanes are
    /// derived display state computed by <see cref="BackRoutePlanner"/> after layout/move/load.</summary>
    public void SetLane(BackRoutePlan? lane)
    {
        _lane = lane;
        OnPropertyChanged(nameof(PathData));
    }
```

Add `using BotBuilder.Core.Connections;` only if the file's namespace doesn't already make `BackRoutePlan` visible — `ConnectionViewModel` IS in `BotBuilder.Core.Connections`, so no using is needed; `BackRoutePlan` resolves directly.

- [ ] **Step 5: Run the geometry test + full ConnectionGeometry suite + build core**

Run: `dotnet test BotBuilder.Core.Tests/BotBuilder.Core.Tests.csproj --filter "FullyQualifiedName~ConnectionGeometry"`
Expected: PASS — the new laned test + the 8 existing.
Run: `dotnet build BotBuilder.Core/BotBuilder.Core.csproj` — 0 errors (confirms ConnectionViewModel change compiles).

- [ ] **Step 6: Commit**

```bash
git add BotBuilder.Core/Connections/ConnectionGeometry.cs BotBuilder.Core/Connections/ConnectionViewModel.cs BotBuilder.Core.Tests/ConnectionGeometryTests.cs
git commit -m "Tidy Up: laned back-route geometry + ConnectionViewModel lane carrier"
```

---

## Task B3: Editor wiring — compute and apply lanes

**Files:**
- Modify: `BotBuilder.Core/BotEditorViewModel.cs`
- Modify: `BotBuilder.Core/DocumentMapper.cs`
- Test: `BotBuilder.Core.Tests/BackRouteRoutingTests.cs` (new)

- [ ] **Step 1: Write the failing test**

Create `BotBuilder.Core.Tests/BackRouteRoutingTests.cs`. It builds a tiny editor graph with one forward and one backward connection, runs `RerouteBackEdges()`, and asserts the backward connection got a laned (orthogonal) path while the forward one stayed a bezier. Use the existing test helpers/patterns in `BotBuilder.Core.Tests` for constructing a `BotEditorViewModel` and nodes (inspect a sibling test e.g. `AutoLayoutEditorTests.cs` for the exact construction helper to reuse — reuse it, do not invent a new registry shape).

```csharp
using System.Linq;
using Xunit;

namespace BotBuilder.Core.Tests;

public class BackRouteRoutingTests
{
    [Fact]
    public void RerouteBackEdges_LanesBackwardConnectionsOnly()
    {
        // Build an editor with two nodes wired forward, then position the target to the LEFT of the
        // source so the connection becomes a back-route; reroute; assert it switched to an orthogonal path.
        var editor = EditorTestFactory.CreateWithChain(2, out var nodes, out var connections);

        // place node 0 to the right of node 1 -> the 0->1 connection is now backward
        nodes[0].X = 600; nodes[0].Y = 100;
        nodes[1].X = 40;  nodes[1].Y = 300;

        editor.RerouteBackEdges();

        var path = connections[0].PathData;
        Assert.Contains(" L ", path);          // laned orthogonal route
        Assert.DoesNotContain(" C ", path);
    }
}
```

If no shared `EditorTestFactory` exists, the implementer MUST instead reuse the exact construction code already used by `AutoLayoutEditorTests.cs` (read that file) to build the editor + nodes + connections inline in the test, rather than inventing a factory. The assertion (backward connection → orthogonal `L` path after `RerouteBackEdges`) is the contract.

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test BotBuilder.Core.Tests/BotBuilder.Core.Tests.csproj --filter "FullyQualifiedName~RerouteBackEdges_LanesBackwardConnectionsOnly"`
Expected: FAIL to compile — `RerouteBackEdges` doesn't exist.

- [ ] **Step 3: Add `RerouteBackEdges()` to `BotEditorViewModel.cs`**

Add this public method (near `AutoLayout`), using `NodeLayout.CardWidth` for the right edge and the connection anchors for inputs:

```csharp
    /// <summary>Recomputes back-route lanes for all connections so return/loop wires don't overlap, then
    /// applies each lane (or clears it) on its connection. Derived display state — not undoable, not saved.</summary>
    public void RerouteBackEdges()
    {
        if (Connections.Count == 0) return;

        double leftX = Nodes.Count == 0 ? 0 : Nodes.Min(n => n.X);
        double rightX = Nodes.Count == 0 ? 0 : Nodes.Max(n => n.X + NodeLayout.CardWidth);

        var inputs = Connections.Select(c =>
        {
            var s = (c.Source.X + c.SourcePort.AnchorOffset.X, c.Source.Y + c.SourcePort.AnchorOffset.Y);
            var t = (c.Target.X + c.TargetPort.AnchorOffset.X, c.Target.Y + c.TargetPort.AnchorOffset.Y);
            return new BotBuilder.Core.Connections.BackRouteInput(c.Id, s.Item1, s.Item2, t.Item1, t.Item2);
        }).ToList();

        var plans = BotBuilder.Core.Connections.BackRoutePlanner.Plan(inputs, leftX, rightX);

        foreach (var c in Connections)
            c.SetLane(plans.TryGetValue(c.Id, out var p) ? p : (BotBuilder.Core.Connections.BackRoutePlan?)null);
    }
```

Confirm `ConnectionViewModel` exposes an `Id` property (it does: `public Guid Id { get; }`).

- [ ] **Step 4: Call it from the edit + layout + load paths**

(a) In `AfterEdit()` add a call at the end:
```csharp
    private void AfterEdit()
    {
        IsDirty = true;
        RaiseUndoState();
        RefreshTargetBadges();
        RefreshNestedBotSubtitles();
        RerouteBackEdges();
    }
```

(b) `AutoLayout()` ends with `CommitMoves(...)` which calls `AfterEdit()` — so layout already reroutes. No change needed there, but verify by reading.

(c) Load path: in `DocumentMapper.Populate`, after `editor.RefreshNestedBotSubtitles();` add `editor.RerouteBackEdges();` so a freshly loaded bot is laned without requiring an edit.

- [ ] **Step 5: Run the new test + the FULL solution build + FULL test suite**

Run: `dotnet build ADB.slnx` — 0 warnings, 0 errors.
Run: `dotnet test BotBuilder.Core.Tests/BotBuilder.Core.Tests.csproj` — 0 failures (new routing test + everything else).

- [ ] **Step 6: Commit**

```bash
git add BotBuilder.Core/BotEditorViewModel.cs BotBuilder.Core/DocumentMapper.cs BotBuilder.Core.Tests/BackRouteRoutingTests.cs
git commit -m "Tidy Up: compute and apply back-route lanes after layout/edit/load"
```

---

## Task C: Full verification

- [ ] **Step 1: Build solution** — `dotnet build ADB.slnx` → 0 warnings, 0 errors.
- [ ] **Step 2: Full BotBuilder.Core suite** — `dotnet test BotBuilder.Core.Tests/BotBuilder.Core.Tests.csproj` → 0 failures.
- [ ] **Step 3: Manual visual check (user)** — relaunch BotBuilder, reload the same bot, Tidy Up. Confirm: Branch's two outputs no longer cross, and return/loop wires no longer lie on top of each other (each takes its own lane). Tuning knobs: `BackRoutePlanner.Margin/LaneGap/GutterStep`.

---

## Self-Review

- **Spec coverage:** Branch crossing → Task A (port-aware tie-break); overlapping wires → Tasks B1–B3 (planner + laned geometry + editor wiring). ✓
- **Back-compat:** `Arrange(nodes, (Guid,Guid)[])` overload preserves every existing AutoLayout test; `ConnectionGeometry.BuildPath`/`BuildBackRoute` unchanged so non-laned rendering (and all current geometry tests) still hold; lanes only apply when `SetLane` was called AND the edge is backward. ✓
- **Determinism:** planner sorts by (span, startY, id); barycenter port tie-break then input order. ✓
- **Type consistency:** `BackRouteInput`/`BackRoutePlan`/`BackRoutePlanner.Plan` signatures match their call sites in `RerouteBackEdges`; `BuildLanedBackRoute` params match the `ConnectionViewModel.PathData` call; the forward/preds tuple field name (`Node`) is used consistently in AutoLayout. ✓
- **Not serialized / not undoable:** lanes are derived view state set via `SetLane`; `RerouteBackEdges` does not push undo or set dirty itself (its callers own that). ✓
- **Placeholder scan:** every step has concrete code + exact command + expected result. The only deferred decision (reuse `EditorTestFactory` vs inline construction in Task B3 Step 1) is explicitly bounded to "reuse the existing AutoLayoutEditorTests construction; don't invent a registry shape." ✓
