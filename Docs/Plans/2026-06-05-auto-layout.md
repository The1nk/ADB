# Auto-Layout ("Tidy Up") Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** A "Tidy Up" command that auto-arranges the node graph into a clean left-to-right layered layout, in one undoable step.

**Architecture:** A pure `AutoLayout.Arrange(nodes, edges)` (Sugiyama-style layering, cycle-safe, height-aware packing); `BotEditorViewModel.AutoLayout()` applies it via the existing `MoveNodesCommand`; a WPF menu item triggers it.

**Tech Stack:** C# / .NET 10, BotBuilder.Core (+ WPF menu), xUnit.

**Reference spec:** `Docs/Specs/2026-06-05-auto-layout-design.md`.

**Merge handling:** algorithm unit-tested, payoff is visual → **user-verified PR, not self-merged.** Conflict-free (PRs #37/#39 already merged; reuses `MoveNodesCommand`).

**`<WT>` = `C:\git\ADB\.claude\worktrees\auto-layout`. Windows: PowerShell for `dotnet`/`git`; NEVER redirect to `/dev/null`/`NUL`.**

---

## Task 1: `AutoLayout` pure algorithm

**Files:** Create `BotBuilder.Core/Layout/AutoLayout.cs`, `BotBuilder.Core.Tests/AutoLayoutTests.cs`.

- [ ] **Step 1: Write the failing tests.** `BotBuilder.Core.Tests/AutoLayoutTests.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using BotBuilder.Core.Layout;
using Xunit;

namespace BotBuilder.Core.Tests;

public class AutoLayoutTests
{
    private static (Guid Id, double Height) N(Guid id, double h = 70) => (id, h);

    [Fact]
    public void LinearChain_LayersLeftToRight()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        var pos = AutoLayout.Arrange(new[] { N(a), N(b), N(c) },
            new[] { (a, b), (b, c) });
        Assert.True(pos[a].X < pos[b].X && pos[b].X < pos[c].X);
    }

    [Fact]
    public void FanOut_SameColumn_DifferentRows_NoOverlap()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        var pos = AutoLayout.Arrange(new[] { N(a), N(b, 70), N(c, 70) },
            new[] { (a, b), (a, c) });
        Assert.Equal(pos[b].X, pos[c].X);                 // same layer/column
        Assert.True(Math.Abs(pos[b].Y - pos[c].Y) >= 70); // packed, no overlap
    }

    [Fact]
    public void Diamond_TakesLongestPath()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid(); var d = Guid.NewGuid();
        var pos = AutoLayout.Arrange(new[] { N(a), N(b), N(c), N(d) },
            new[] { (a, b), (a, c), (b, d), (c, d) });
        Assert.True(pos[d].X > pos[b].X);                 // d after b/c (layer 2, not 1)
        Assert.Equal(pos[b].X, pos[c].X);
    }

    [Fact]
    public void Cycle_Terminates_AndPlacesBoth()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var pos = AutoLayout.Arrange(new[] { N(a), N(b) }, new[] { (a, b), (b, a) });
        Assert.True(pos.ContainsKey(a) && pos.ContainsKey(b));
        Assert.True(pos[a].X < pos[b].X);                 // back-edge b->a dropped; a=layer0, b=layer1
    }

    [Fact]
    public void HeightAwarePacking()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        var pos = AutoLayout.Arrange(new[] { N(a), N(b, 70), N(c, 110) },
            new[] { (a, b), (a, c) });
        var top = Math.Min(pos[b].Y, pos[c].Y);
        var firstHeight = pos[b].Y < pos[c].Y ? 70 : 110;
        var bottom = Math.Max(pos[b].Y, pos[c].Y);
        Assert.True(bottom >= top + firstHeight + 30);    // gap >= firstHeight + RowGap
    }

    [Fact]
    public void IsolatedNode_AtOriginColumn()
    {
        var a = Guid.NewGuid();
        var pos = AutoLayout.Arrange(new[] { N(a) }, Array.Empty<(Guid, Guid)>());
        Assert.Equal(AutoLayout.OriginX, pos[a].X);
    }

    [Fact]
    public void EmptyGraph_EmptyResult()
        => Assert.Empty(AutoLayout.Arrange(Array.Empty<(Guid, double)>(), Array.Empty<(Guid, Guid)>()));
}
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test "<WT>\BotBuilder.Core.Tests" --filter "FullyQualifiedName~AutoLayoutTests"` → compile FAIL.

- [ ] **Step 3: Create `BotBuilder.Core/Layout/AutoLayout.cs`:**
```csharp
namespace BotBuilder.Core.Layout;

/// <summary>Pure layered left-to-right graph layout ("Tidy Up"). Assigns each node a layer by longest path
/// on the back-edge-removed DAG (so cycles are safe), then packs each layer's column top-to-bottom by height.</summary>
public static class AutoLayout
{
    public const double ColGap = 240;
    public const double RowGap = 30;
    public const double OriginX = 40;
    public const double OriginY = 40;

    public static IReadOnlyDictionary<Guid, (double X, double Y)> Arrange(
        IReadOnlyList<(Guid Id, double Height)> nodes,
        IReadOnlyList<(Guid Source, Guid Target)> edges)
    {
        var result = new Dictionary<Guid, (double X, double Y)>();
        if (nodes.Count == 0) return result;

        var ids = nodes.Select(n => n.Id).ToList();
        var idSet = new HashSet<Guid>(ids);
        var height = nodes.ToDictionary(n => n.Id, n => n.Height);
        var order = new Dictionary<Guid, int>();           // stable input order
        for (var i = 0; i < ids.Count; i++) order[ids[i]] = i;

        // adjacency over edges whose endpoints are both real nodes
        var adj = ids.ToDictionary(id => id, _ => new List<Guid>());
        foreach (var (s, t) in edges)
            if (idSet.Contains(s) && idSet.Contains(t) && s != t) adj[s].Add(t);

        // 1) cycle removal: DFS, drop edges that point to a node on the current stack (back-edges)
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
        foreach (var id in ids.OrderBy(i => order[i])) if (state[id] == 0) Dfs(id);

        // 2) longest-path layering on the forward DAG (Kahn)
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

        // 3) group by layer, stable order within layer; 4) pack columns by height
        var byLayer = ids.GroupBy(id => layer[id]).OrderBy(g => g.Key);
        foreach (var group in byLayer)
        {
            var x = OriginX + group.Key * ColGap;
            var y = OriginY;
            foreach (var id in group.OrderBy(i => order[i]))
            {
                result[id] = (x, y);
                y += height[id] + RowGap;
            }
        }
        return result;
    }
}
```
**Adaptation:** the recursive `Dfs` is fine for typical bot sizes; if a stack-depth concern arises, convert to an explicit stack — but recursion is acceptable here. Add `using System;`/`System.Collections.Generic;`/`System.Linq;` if the project lacks ImplicitUsings (check the csproj). The tests are the spec.

- [ ] **Step 4: Run to verify it passes** — green.

- [ ] **Step 5: Commit:**
```
git -C "<WT>" add BotBuilder.Core/Layout/AutoLayout.cs BotBuilder.Core.Tests/AutoLayoutTests.cs
git -C "<WT>" commit -m "feat(canvas): AutoLayout layered left-to-right graph arrangement (cycle-safe)"
```

---

## Task 2: `BotEditorViewModel.AutoLayout()`

**Files:** modify `BotBuilder.Core/BotEditorViewModel.cs`; modify `BotBuilder.Core.Tests/` (add to an editor test file or create `AutoLayoutEditorTests.cs`).

- [ ] **Step 1: Read** `BotEditorViewModel` — `Nodes`, `Connections` (each `ConnectionViewModel` has `.Source`/`.Target` `NodeViewModel` with `.Id`), `NodeViewModel.Height`/`.X`/`.Y`/`.Id`, the existing `CommitMoves(IReadOnlyList<(NodeViewModel Node, double OldX, double OldY)>)` + `MoveNodesCommand` (added with copy/paste's multi-drag), and `_undo.PushExecuted`/`AfterEdit`. Confirm exact shapes.

- [ ] **Step 2: Write the failing test** (`AutoLayoutEditorTests.cs`, adapt editor construction + connect to the existing test helpers):
```csharp
using System.Linq;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class AutoLayoutEditorTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    [Fact]
    public void AutoLayout_LaysOutChain_AndIsSingleUndoStep()
    {
        var editor = NewEditor();
        var a = editor.AddNode("control.start", 500, 500);
        var b = editor.AddNode("data.log", 30, 200);
        Connect(editor, a, "out", b, "in");

        editor.AutoLayout();

        Assert.True(a.X < b.X);                 // chain flows left-to-right
        var (ax, ay, bx, by) = (a.X, a.Y, b.X, b.Y);

        editor.Undo();                          // ONE undo restores both
        Assert.Equal(500, a.X);
        Assert.Equal(500, a.Y);
        Assert.Equal(30, b.X);
        Assert.Equal(200, b.Y);
        Assert.NotEqual(ax, a.X);               // sanity: layout had moved them
    }

    private static void Connect(BotEditorViewModel editor, NodeViewModel s, string sp, NodeViewModel t, string tp)
    {
        var sport = s.OutputPorts.First(p => p.Name == sp);
        var tport = t.InputPorts.First(p => p.Name == tp);
        editor.Connect(s, sport, t, tport);
    }
}
```
(Use the SAME public `Connect`/`Undo` path the copy/paste tests use. Real port names: `control.start` output `"out"`, `data.log` input `"in"`.)

- [ ] **Step 3: Run to verify it fails.**

- [ ] **Step 4: Implement `AutoLayout()` in `BotEditorViewModel`:**
```csharp
    /// <summary>Re-arranges all nodes into a tidy left-to-right layered layout, as one undoable step.</summary>
    public void AutoLayout()
    {
        if (Nodes.Count == 0) return;
        var nodes = Nodes.Select(n => (n.Id, n.Height)).ToList();
        var edges = Connections.Select(c => (c.Source.Id, c.Target.Id)).ToList();
        var positions = BotBuilder.Core.Layout.AutoLayout.Arrange(nodes, edges);

        var moves = new List<(NodeViewModel Node, double OldX, double OldY)>();
        foreach (var node in Nodes)
        {
            if (positions.TryGetValue(node.Id, out var p))
            {
                var oldX = node.X; var oldY = node.Y;
                node.X = p.X; node.Y = p.Y;
                moves.Add((node, oldX, oldY));
            }
        }
        CommitMoves(moves);   // records a single MoveNodesCommand (no-op-safe)
    }
```
(`CommitMoves` already filters no-movement and pushes one `MoveNodesCommand` + `AfterEdit`. Since `AutoLayout` sets the new positions BEFORE calling `CommitMoves` with the OLD positions, the command captures old→new correctly — matching how the drag path uses it. Confirm `CommitMoves`' signature/behavior; adapt if needed.)

- [ ] **Step 5: Run to verify it passes** — green.

- [ ] **Step 6: Commit:**
```
git -C "<WT>" add BotBuilder.Core/BotEditorViewModel.cs BotBuilder.Core.Tests/AutoLayoutEditorTests.cs
git -C "<WT>" commit -m "feat(editor): AutoLayout() applies tidy layout as one undoable step"
```

---

## Task 3: WPF "Tidy Up" trigger + full sweep

**Files:** modify `BotBuilder/MainWindow.xaml`, `BotBuilder/MainWindow.xaml.cs`.

- [ ] **Step 1: Read** `BotBuilder/MainWindow.xaml` — find the `<Menu>` structure (there's an Edit menu with a Delete item — see `Delete_Click`). Add a menu item, e.g. under Edit (or a new "Arrange" menu): `<MenuItem Header="_Tidy Up" Click="TidyUp_Click" InputGestureText="Ctrl+L" />`. In `MainWindow.xaml.cs` add the handler `private void TidyUp_Click(object sender, RoutedEventArgs e) => _editor.AutoLayout();`, mirroring `Delete_Click`. OPTIONAL: wire Ctrl+L in `Window_KeyDown` (mirror the existing Ctrl+Z/Y/C/V cases — `else if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control) { _editor.AutoLayout(); e.Handled = true; }`, and guard `if (e.OriginalSource is TextBox) return;` like the copy/paste cases so it doesn't fire while editing a field).

- [ ] **Step 2: Build + full sweep.** `dotnet build "<WT>\ADB.slnx" -warnaserror -v q --nologo` → 0 warnings; `dotnet test "<WT>\ADB.slnx"` → all green. Report totals.

- [ ] **Step 3: Commit:**
```
git -C "<WT>" add BotBuilder/MainWindow.xaml BotBuilder/MainWindow.xaml.cs
git -C "<WT>" commit -m "feat(editor): Tidy Up menu item + Ctrl+L for auto-layout"
```

---

## Self-Review Notes (addressed)

- **Spec coverage:** pure layered algorithm w/ cycle removal + longest-path + height-aware packing (Task 1); editor apply as one undo step via `MoveNodesCommand` (Task 2); menu/keybinding trigger (Task 3). ✓
- **Cycle safety:** DFS back-edge removal → layering terminates (tested). ✓
- **Type consistency:** `AutoLayout.Arrange((Guid,double)[], (Guid,Guid)[]) → IReadOnlyDictionary<Guid,(double,double)>`; `BotEditorViewModel.AutoLayout()`; reuses `CommitMoves`/`MoveNodesCommand`. ✓
- **Conflict-free** (PRs #37/#39 merged). Visual payoff → user-verified PR.
