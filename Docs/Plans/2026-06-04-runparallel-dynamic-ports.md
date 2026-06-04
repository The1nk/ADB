# Run Parallel Dynamic Branch Ports — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Run Parallel node render `branch1..branchN` output ports from its `Branches` config (min 2), so >2-branch parallel bots can be wired; the engine already fans out to `branchN`.

**Architecture:** Config-driven output ports scoped to Run Parallel. A static port builder on `RunParallelAction`; `NodeViewModel.OutputPorts` becomes an `ObservableCollection` with primitives to grow/shrink and replace it (instance-preserving); an undoable `SetBranchCountCommand` reconciles ports + orphaned connections when the count changes; the Properties panel routes only the Run Parallel `branches` edit through it; and `DocumentMapper` grows the ports on load before wiring connections.

**Tech Stack:** C# / .NET 10, WPF (BotBuilder), CommunityToolkit.Mvvm, xUnit. AdbCore (`RunParallelAction`), BotBuilder.Core (editor/undo/mapper).

**Reference spec:** `Docs/Specs/2026-06-04-runparallel-dynamic-ports-design.md`.

**Merge handling:** has a canvas/visual surface → built to compile-clean + unit-green, opened as a PR, and **parked for the user** to visually verify and merge (NOT self-merged).

---

## File Structure

- `AdbCore/Actions/BuiltIn/RunParallelAction.cs` — add `OutputPortsForBranches(int)`; default `OutputPorts` built from it.
- `BotBuilder.Core/NodeViewModel.cs` — `OutputPorts` → `ObservableCollection<PortViewModel>`; `ReplaceOutputPorts`, `SetBranchPortCount`, static `BranchOutputPort`.
- `BotBuilder.Core/Undo/EditorCommands.cs` — `SetBranchCountCommand`.
- `BotBuilder.Core/BotEditorViewModel.cs` — `OnBranchCountChanged(NodeViewModel)`.
- `BotBuilder.Core/Properties/PropertiesViewModel.cs` — route the Run Parallel `branches` field through `OnBranchCountChanged`.
- `BotBuilder.Core/DocumentMapper.cs` — grow Run Parallel ports on load before wiring connections.
- Tests in `AdbCore.Tests` and `BotBuilder.Core.Tests`.

---

## Task 1: `RunParallelAction.OutputPortsForBranches`

**Files:**
- Modify: `AdbCore/Actions/BuiltIn/RunParallelAction.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/RunParallelActionTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `AdbCore.Tests/Actions/BuiltIn/RunParallelActionTests.cs`:

```csharp
using System.Linq;
using AdbCore.Actions.BuiltIn;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class RunParallelActionTests
{
    [Fact]
    public void OutputPortsForBranches_BuildsNamedBranchPorts()
    {
        var ports = RunParallelAction.OutputPortsForBranches(3);

        Assert.Equal(new[] { "branch1", "branch2", "branch3" }, ports.Select(p => p.Name));
        Assert.Equal(new[] { "Branch 1", "Branch 2", "Branch 3" }, ports.Select(p => p.Label));
    }

    [Fact]
    public void DefaultOutputPorts_AreTwoBranches()
    {
        var def = new RunParallelAction();
        Assert.Equal(new[] { "branch1", "branch2" }, def.OutputPorts.Select(p => p.Name));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test C:\git\ADB-wt-runparallel\AdbCore.Tests --filter "FullyQualifiedName~RunParallelActionTests"`
Expected: compile FAIL (`OutputPortsForBranches` missing).

- [ ] **Step 3: Implement**

In `AdbCore/Actions/BuiltIn/RunParallelAction.cs`, replace the static `OutputPorts` initializer block (the `public List<PortDefinition> OutputPorts { get; } = new() { branch1, branch2 };`) with a static builder plus a default-built property:

```csharp
    /// <summary>Builds the branch output ports (`branch1..branchN`) for a given branch count.</summary>
    public static List<PortDefinition> OutputPortsForBranches(int count)
    {
        var ports = new List<PortDefinition>(Math.Max(0, count));
        for (var i = 1; i <= count; i++)
        {
            ports.Add(new PortDefinition { Name = BranchPort(i), Label = $"Branch {i}" });
        }
        return ports;
    }

    public List<PortDefinition> OutputPorts { get; } = OutputPortsForBranches(DefaultBranchCount);
```

Keep everything else (TypeKey, BranchesKey, BranchPort, ConfigFields, etc.) unchanged.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test C:\git\ADB-wt-runparallel\AdbCore.Tests --filter "FullyQualifiedName~RunParallelActionTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git -C C:\git\ADB-wt-runparallel add AdbCore/Actions/BuiltIn/RunParallelAction.cs AdbCore.Tests/Actions/BuiltIn/RunParallelActionTests.cs
git -C C:\git\ADB-wt-runparallel commit -m "feat(parallel): RunParallelAction.OutputPortsForBranches(count)"
```

---

## Task 2: `NodeViewModel` dynamic output ports

**Files:**
- Modify: `BotBuilder.Core/NodeViewModel.cs`
- Test: `BotBuilder.Core.Tests/NodeViewModelPortsTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `BotBuilder.Core.Tests/NodeViewModelPortsTests.cs`:

```csharp
using System.Collections.Specialized;
using System.Linq;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class NodeViewModelPortsTests
{
    private static NodeViewModel RunParallelNode()
    {
        var def = new RunParallelAction();
        return NodeViewModel.FromDefinition(def, System.Guid.NewGuid(), "Run Parallel", 0, 0);
    }

    [Fact]
    public void FromDefinition_RunParallel_StartsWithTwoBranchPorts()
    {
        var node = RunParallelNode();
        Assert.Equal(new[] { "branch1", "branch2" }, node.OutputPorts.Select(p => p.Name));
    }

    [Fact]
    public void SetBranchPortCount_Grows_PreservingExistingInstances()
    {
        var node = RunParallelNode();
        var first = node.OutputPorts[0];

        node.SetBranchPortCount(4);

        Assert.Equal(new[] { "branch1", "branch2", "branch3", "branch4" }, node.OutputPorts.Select(p => p.Name));
        Assert.Same(first, node.OutputPorts[0]); // existing instances preserved
    }

    [Fact]
    public void SetBranchPortCount_Shrinks_DropsTrailing()
    {
        var node = RunParallelNode();
        node.SetBranchPortCount(5);
        var second = node.OutputPorts[1];

        node.SetBranchPortCount(2);

        Assert.Equal(new[] { "branch1", "branch2" }, node.OutputPorts.Select(p => p.Name));
        Assert.Same(second, node.OutputPorts[1]);
    }

    [Fact]
    public void OutputPorts_IsObservable()
    {
        var node = RunParallelNode();
        var changed = false;
        node.OutputPorts.CollectionChanged += (_, _) => changed = true;

        node.SetBranchPortCount(3);

        Assert.True(changed);
    }

    [Fact]
    public void ReplaceOutputPorts_SwapsContents()
    {
        var node = RunParallelNode();
        var replacement = RunParallelAction.OutputPortsForBranches(3)
            .Select((p, i) => new PortViewModel(p.Name, PortDirection.Out, NodeLayout.OutputAnchor(i)))
            .ToList();

        node.ReplaceOutputPorts(replacement);

        Assert.Equal(new[] { "branch1", "branch2", "branch3" }, node.OutputPorts.Select(p => p.Name));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test C:\git\ADB-wt-runparallel\BotBuilder.Core.Tests --filter "FullyQualifiedName~NodeViewModelPortsTests"`
Expected: compile FAIL.

- [ ] **Step 3: Implement**

In `BotBuilder.Core/NodeViewModel.cs`:

Add usings at the top (alongside existing ones):
```csharp
using System.Collections.ObjectModel;
using AdbCore.Actions.BuiltIn;
```

Change the `OutputPorts` property declaration from
```csharp
    public IReadOnlyList<PortViewModel> OutputPorts { get; }
```
to
```csharp
    public ObservableCollection<PortViewModel> OutputPorts { get; }
```

In the constructor, change the assignment from `OutputPorts = outputPorts;` to:
```csharp
        OutputPorts = new ObservableCollection<PortViewModel>(outputPorts);
```
(Leave the constructor parameter `IReadOnlyList<PortViewModel> outputPorts` as-is, and leave `InputPorts` unchanged.)

Add these members (e.g. after `FromDefinition`):
```csharp
    /// <summary>Builds the output PortViewModel for a 0-based branch index (Run Parallel dynamic ports).</summary>
    public static PortViewModel BranchOutputPort(int zeroBasedIndex) =>
        new(RunParallelAction.BranchPort(zeroBasedIndex + 1), PortDirection.Out, NodeLayout.OutputAnchor(zeroBasedIndex));

    /// <summary>Grows or shrinks the output ports to exactly <paramref name="count"/> branch ports,
    /// preserving existing instances. Non-undoable primitive (used on load and by the undo command's snapshots).</summary>
    public void SetBranchPortCount(int count)
    {
        while (OutputPorts.Count < count)
        {
            OutputPorts.Add(BranchOutputPort(OutputPorts.Count));
        }
        while (OutputPorts.Count > count)
        {
            OutputPorts.RemoveAt(OutputPorts.Count - 1);
        }
    }

    /// <summary>Replaces the output ports with the given instances (used by the undoable branch-count command).</summary>
    public void ReplaceOutputPorts(IReadOnlyList<PortViewModel> ports)
    {
        OutputPorts.Clear();
        foreach (var p in ports)
        {
            OutputPorts.Add(p);
        }
    }
```

- [ ] **Step 4: Build the solution to catch fallout from the type change, then run tests**

Run: `dotnet build C:\git\ADB-wt-runparallel\ADB.slnx`
Expected: success, 0 warnings. (`ObservableCollection<T>` implements `IReadOnlyList<T>`/`IEnumerable<T>`, so existing readers like `DocumentMapper.BuildConnections`'s `source.OutputPorts.FirstOrDefault(...)` and the WPF `ItemsControl` binding still compile.)

Run: `dotnet test C:\git\ADB-wt-runparallel\BotBuilder.Core.Tests --filter "FullyQualifiedName~NodeViewModelPortsTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git -C C:\git\ADB-wt-runparallel add BotBuilder.Core/NodeViewModel.cs BotBuilder.Core.Tests/NodeViewModelPortsTests.cs
git -C C:\git\ADB-wt-runparallel commit -m "feat(builder): NodeViewModel observable output ports + branch-port primitives"
```

---

## Task 3: `SetBranchCountCommand` + `BotEditorViewModel.OnBranchCountChanged`

**Files:**
- Modify: `BotBuilder.Core/Undo/EditorCommands.cs`
- Modify: `BotBuilder.Core/BotEditorViewModel.cs`
- Test: `BotBuilder.Core.Tests/BranchCountTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `BotBuilder.Core.Tests/BranchCountTests.cs`:

```csharp
using System.Linq;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class BranchCountTests
{
    private static BotEditorViewModel Editor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    private static NodeViewModel AddRunParallel(BotEditorViewModel e) => e.AddNode(RunParallelAction.RunParallelTypeKey, 0, 0);
    private static NodeViewModel AddEnd(BotEditorViewModel e, double y) => e.AddNode("control.end", 200, y);

    private static void SetBranches(NodeViewModel node, int n) => node.Config[RunParallelAction.BranchesKey] = n;

    [Fact]
    public void Grow_AddsBranchPorts()
    {
        var e = Editor();
        var rp = AddRunParallel(e);
        SetBranches(rp, 4);

        e.OnBranchCountChanged(rp);

        Assert.Equal(new[] { "branch1", "branch2", "branch3", "branch4" }, rp.OutputPorts.Select(p => p.Name));
    }

    [Fact]
    public void Grow_ThenUndo_RestoresTwoPorts()
    {
        var e = Editor();
        var rp = AddRunParallel(e);
        SetBranches(rp, 4);
        e.OnBranchCountChanged(rp);

        e.Undo();

        Assert.Equal(2, rp.OutputPorts.Count);
    }

    [Fact]
    public void Shrink_DeletesOrphanedConnections_Undoable()
    {
        var e = Editor();
        var rp = AddRunParallel(e);
        SetBranches(rp, 3);
        e.OnBranchCountChanged(rp);

        // Wire branch3 -> an End node.
        var end = AddEnd(e, 100);
        var branch3 = rp.OutputPorts.Single(p => p.Name == "branch3");
        var endIn = end.InputPorts[0];
        Assert.Equal(ConnectionError.None, e.Connect(rp, branch3, end, endIn));
        Assert.Single(e.Connections);

        // Shrink to 2 -> branch3 port + its connection removed.
        SetBranches(rp, 2);
        e.OnBranchCountChanged(rp);
        Assert.Equal(2, rp.OutputPorts.Count);
        Assert.Empty(e.Connections);

        // Undo restores both the port and the connection.
        e.Undo();
        Assert.Equal(3, rp.OutputPorts.Count);
        Assert.Single(e.Connections);
        Assert.Same(branch3, e.Connections[0].SourcePort);
    }

    [Fact]
    public void ClampsToMinimumTwo()
    {
        var e = Editor();
        var rp = AddRunParallel(e);
        SetBranches(rp, 1);

        e.OnBranchCountChanged(rp);

        Assert.Equal(2, rp.OutputPorts.Count);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test C:\git\ADB-wt-runparallel\BotBuilder.Core.Tests --filter "FullyQualifiedName~BranchCountTests"`
Expected: compile FAIL (`OnBranchCountChanged` missing).

- [ ] **Step 3: Add the command**

In `BotBuilder.Core/Undo/EditorCommands.cs`, add at the top with the existing using:
```csharp
using AdbCore.Actions.BuiltIn;
```
Append this command class (after `DeleteNodesCommand`):
```csharp
/// <summary>Changes a Run Parallel node's branch-port count: swaps its output ports and removes the
/// connections orphaned by a shrink. Undo restores the previous ports and re-adds those connections.</summary>
internal sealed class SetBranchCountCommand : IUndoableCommand
{
    private readonly BotEditorViewModel _editor;
    private readonly NodeViewModel _node;
    private readonly IReadOnlyList<PortViewModel> _oldPorts;
    private readonly IReadOnlyList<PortViewModel> _newPorts;
    private readonly int _oldCount;
    private readonly int _newCount;
    private readonly IReadOnlyList<ConnectionViewModel> _removedConnections;

    public SetBranchCountCommand(
        BotEditorViewModel editor,
        NodeViewModel node,
        IReadOnlyList<PortViewModel> oldPorts,
        IReadOnlyList<PortViewModel> newPorts,
        int oldCount,
        int newCount,
        IReadOnlyList<ConnectionViewModel> removedConnections)
    {
        _editor = editor;
        _node = node;
        _oldPorts = oldPorts;
        _newPorts = newPorts;
        _oldCount = oldCount;
        _newCount = newCount;
        _removedConnections = removedConnections;
    }

    public void Do()
    {
        _node.Config[RunParallelAction.BranchesKey] = _newCount;
        _node.ReplaceOutputPorts(_newPorts);
        foreach (var c in _removedConnections) { _editor.RemoveConnectionCore(c); }
    }

    public void Undo()
    {
        _node.Config[RunParallelAction.BranchesKey] = _oldCount;
        _node.ReplaceOutputPorts(_oldPorts);
        foreach (var c in _removedConnections) { _editor.AddConnectionCore(c); }
    }
}
```

- [ ] **Step 4: Add the editor entry point**

In `BotBuilder.Core/BotEditorViewModel.cs`, add the using:
```csharp
using AdbCore.Actions.BuiltIn;
```
Add this public method (e.g. after `AssignTarget`):
```csharp
    /// <summary>Reconciles a Run Parallel node's branch ports to its current `branches` config value
    /// (clamped to >= 2): grows/shrinks the output ports and, on a shrink, deletes the connections on the
    /// dropped ports. The whole change is a single undoable step.</summary>
    public void OnBranchCountChanged(NodeViewModel node)
    {
        var requested = ConfigValues.GetInt(node.Config, RunParallelAction.BranchesKey, RunParallelAction.DefaultBranchCount);
        var newCount = Math.Max(2, requested);
        var oldPorts = node.OutputPorts.ToList();
        var oldCount = oldPorts.Count;

        if (newCount == oldCount)
        {
            // No port change (e.g. the value was clamped back to the current count); keep config consistent.
            node.Config[RunParallelAction.BranchesKey] = newCount;
            IsDirty = true;
            return;
        }

        List<PortViewModel> newPorts;
        List<ConnectionViewModel> removed;
        if (newCount > oldCount)
        {
            newPorts = oldPorts.ToList();
            for (var i = oldCount; i < newCount; i++)
            {
                newPorts.Add(NodeViewModel.BranchOutputPort(i));
            }
            removed = new List<ConnectionViewModel>();
        }
        else
        {
            newPorts = oldPorts.Take(newCount).ToList();
            var dropped = oldPorts.Skip(newCount).ToHashSet();
            removed = Connections
                .Where(c => ReferenceEquals(c.Source, node) && dropped.Contains(c.SourcePort))
                .ToList();
        }

        _undo.Execute(new SetBranchCountCommand(this, node, oldPorts, newPorts, oldCount, newCount, removed));
        AfterEdit();
    }
```

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test C:\git\ADB-wt-runparallel\BotBuilder.Core.Tests --filter "FullyQualifiedName~BranchCountTests"`
Expected: PASS (4 tests).

- [ ] **Step 6: Commit**

```bash
git -C C:\git\ADB-wt-runparallel add BotBuilder.Core/Undo/EditorCommands.cs BotBuilder.Core/BotEditorViewModel.cs BotBuilder.Core.Tests/BranchCountTests.cs
git -C C:\git\ADB-wt-runparallel commit -m "feat(builder): undoable Run Parallel branch-count change (ports + orphaned connections)"
```

---

## Task 4: Route the Properties-panel Branches field

**Files:**
- Modify: `BotBuilder.Core/Properties/PropertiesViewModel.cs`
- Test: `BotBuilder.Core.Tests/PropertiesViewModelTests.cs` (append)

- [ ] **Step 1: Write the failing test**

Append to `BotBuilder.Core.Tests/PropertiesViewModelTests.cs` (inside the existing class). Mirror the file's existing setup helpers (it builds an editor via `BuiltInEditor()` and selects nodes via `AddNode`/`Select` — match exactly). The behavior to assert: editing the Run Parallel `branches` field grows the node's ports.

```csharp
    [Fact]
    public void EditingRunParallelBranchesField_GrowsOutputPorts()
    {
        var e = BuiltInEditor();                                   // adapt to the file's existing helper
        var rp = e.AddNode(AdbCore.Actions.BuiltIn.RunParallelAction.RunParallelTypeKey, 0, 0);
        e.Select(rp);

        var branchesField = e.Properties.Fields.Single(f => f.Key == AdbCore.Actions.BuiltIn.RunParallelAction.BranchesKey);
        branchesField.Value = 4d;                                  // committing the field

        Assert.Equal(4, rp.OutputPorts.Count);
    }
```

If `BuiltInEditor()` is not the helper name in that file, use whatever the existing tests use to construct a `BotEditorViewModel` seeded with built-in actions; do not invent new infrastructure.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test C:\git\ADB-wt-runparallel\BotBuilder.Core.Tests --filter "FullyQualifiedName~PropertiesViewModelTests"`
Expected: FAIL — editing the field does not change the port count (still 2).

- [ ] **Step 3: Implement the routing**

In `BotBuilder.Core/Properties/PropertiesViewModel.cs`, add the using:
```csharp
using AdbCore.Actions.BuiltIn;
```
In `Rebuild()`, the loop that adds fields currently reads:
```csharp
                foreach (var field in definition.ConfigFields)
                {
                    Fields.Add(new ConfigFieldViewModel(Node, field, _editor.MarkDirty));
                }
```
Replace it with a per-field onChanged that, for the Run Parallel `branches` field, reconciles the ports:
```csharp
                foreach (var field in definition.ConfigFields)
                {
                    var node = Node;
                    Action onChanged = node.TypeKey == RunParallelAction.RunParallelTypeKey && field.Key == RunParallelAction.BranchesKey
                        ? () => _editor.OnBranchCountChanged(node)
                        : _editor.MarkDirty;
                    Fields.Add(new ConfigFieldViewModel(node, field, onChanged));
                }
```
(`ConfigFieldViewModel.Value`'s setter writes the field to `node.Config` and then calls `onChanged`, so by the time `OnBranchCountChanged` runs the new `branches` value is already in config.)

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test C:\git\ADB-wt-runparallel\BotBuilder.Core.Tests --filter "FullyQualifiedName~PropertiesViewModelTests"`
Expected: PASS (whole class).

- [ ] **Step 5: Commit**

```bash
git -C C:\git\ADB-wt-runparallel add BotBuilder.Core/Properties/PropertiesViewModel.cs BotBuilder.Core.Tests/PropertiesViewModelTests.cs
git -C C:\git\ADB-wt-runparallel commit -m "feat(builder): route Run Parallel Branches edit through branch-count reconcile"
```

---

## Task 5: Grow Run Parallel ports on load

**Files:**
- Modify: `BotBuilder.Core/DocumentMapper.cs`
- Test: `BotBuilder.Core.Tests/DocumentMapperBranchesTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `BotBuilder.Core.Tests/DocumentMapperBranchesTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Models;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class DocumentMapperBranchesTests
{
    private static BotEditorViewModel Editor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    [Fact]
    public void Load_RunParallelWithFiveBranches_RebuildsPortsAndLinksConnections()
    {
        var registry = new ActionRegistry();
        BuiltInActions.Register(registry, new ActionExecutorRegistry());

        var rpId = System.Guid.NewGuid();
        var endId = System.Guid.NewGuid();
        var bot = new Bot { Id = System.Guid.NewGuid(), Name = "p" };
        bot.Actions.Add(new BotAction
        {
            Id = rpId, TypeKey = RunParallelAction.RunParallelTypeKey, Label = "RP",
            Config = new Dictionary<string, object> { [RunParallelAction.BranchesKey] = 5 },
            CanvasPosition = new Position { X = 0, Y = 0 },
        });
        bot.Actions.Add(new BotAction { Id = endId, TypeKey = "control.end", Label = "End", CanvasPosition = new Position { X = 200, Y = 0 } });
        bot.Connections.Add(new ActionConnection
        {
            Id = System.Guid.NewGuid(), SourceActionId = rpId, SourcePort = "branch5", TargetActionId = endId, TargetPort = "in",
        });

        var editor = Editor();
        DocumentMapper.Populate(editor, bot, registry);

        var rp = editor.Nodes.Single(n => n.Id == rpId);
        Assert.Equal(5, rp.OutputPorts.Count);
        Assert.Single(editor.Connections); // branch5 -> end linked because the port now exists
        Assert.Equal("branch5", editor.Connections[0].SourcePort.Name);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test C:\git\ADB-wt-runparallel\BotBuilder.Core.Tests --filter "FullyQualifiedName~DocumentMapperBranchesTests"`
Expected: FAIL — only 2 ports, the `branch5` connection is dropped (0 connections).

- [ ] **Step 3: Implement**

In `BotBuilder.Core/DocumentMapper.cs`, add the using:
```csharp
using AdbCore.Actions.BuiltIn;
```
In `BuildNode`, after the config-copy loop (`foreach (var kv in action.Config) { node.Config[kv.Key] = kv.Value; }`) and before `return node;`, add:
```csharp
        if (node.TypeKey == RunParallelAction.RunParallelTypeKey)
        {
            var branches = System.Math.Max(2, ConfigValues.GetInt(node.Config, RunParallelAction.BranchesKey, RunParallelAction.DefaultBranchCount));
            node.SetBranchPortCount(branches);
        }
```
(`BuildNode` runs for every node before `BuildConnections` matches `source.OutputPorts.FirstOrDefault(p => p.Name == c.SourcePort)`, so the grown ports are present when connections are linked.)

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test C:\git\ADB-wt-runparallel\BotBuilder.Core.Tests --filter "FullyQualifiedName~DocumentMapperBranchesTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git -C C:\git\ADB-wt-runparallel add BotBuilder.Core/DocumentMapper.cs BotBuilder.Core.Tests/DocumentMapperBranchesTests.cs
git -C C:\git\ADB-wt-runparallel commit -m "feat(builder): grow Run Parallel branch ports on load before wiring connections"
```

---

## Task 6: Build + test sweep + PR (parked for user)

**Files:** none (verification + PR).

- [ ] **Step 1: Build, 0 warnings**

Run: `dotnet build C:\git\ADB-wt-runparallel\ADB.slnx`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 2: Full test suite**

Run: `dotnet test C:\git\ADB-wt-runparallel\ADB.slnx`
Expected: all pass. New tests: AdbCore.Tests (+2 RunParallelAction), BotBuilder.Core.Tests (+5 NodeViewModelPorts, +4 BranchCount, +1 Properties, +1 DocumentMapperBranches). No registry/palette count changes (no new actions). Confirm 0 failures.

- [ ] **Step 3: Push + open PR (DO NOT MERGE)**

```bash
git -C C:\git\ADB-wt-runparallel push -u origin worktree-runparallel-dynamic-ports
gh pr create --repo The1nk/ADB --base main --head worktree-runparallel-dynamic-ports --title "fix(builder): Run Parallel renders N branch ports from its Branches config" --body "<summary + the visual watch-items from the spec>"
```

Report the PR URL to the controller. This PR is **parked for the user** to visually verify (canvas shows N ports; wire branch3+; shrink deletes those wires; Ctrl+Z restores; save/reload round-trips a 5-branch bot) and merge. Do NOT merge it.

---

## Self-Review Notes (addressed)

- **Spec coverage:** §3.1 port source (Task 1); §3.2 observable/reconcilable ports (Task 2); §3.3 undoable command (Task 3); §3.4 properties routing (Task 4); §3.5 load path (Task 5); §3.6 clamp ≥2 (Task 3 `Math.Max(2, …)` + Task 4 path). ✓
- **Type consistency:** `OnBranchCountChanged`, `SetBranchCountCommand`, `BranchOutputPort`, `SetBranchPortCount`, `ReplaceOutputPorts`, `OutputPortsForBranches` are referenced identically across tasks. `OutputPorts` is `ObservableCollection<PortViewModel>` from Task 2 onward. Config value stored as `int` (`newCount`/`branches`), read via `ConfigValues.GetInt` (handles int/double). ✓
- **Instance preservation under undo:** the command snapshots `oldPorts`/`newPorts` as instance lists and the removed connections reference instances held in `oldPorts`, so undo restores the exact port instances the connections point at. ✓
- **No placeholders:** every code step is complete; the one adaptive step (Task 4 test helper) instructs mirroring the existing `PropertiesViewModelTests` setup rather than inventing infrastructure.
- **No new action counts:** M-fix adds no actions, so `BuiltInActionsTests`/`PaletteViewModelTests` counts are untouched.
