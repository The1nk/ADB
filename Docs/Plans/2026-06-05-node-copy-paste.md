# Node Copy / Paste Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Copy/paste the selected node(s) + their internal connections via an in-app clipboard, undoable, on Ctrl+C / Ctrl+V.

**Architecture:** A `NodeClipboard` snapshot model; `BotEditorViewModel.CopySelection()`/`Paste()`; a `PasteCommand` (mirrors `DeleteNodesCommand`); Ctrl+C/V wired in `MainWindow.xaml.cs`'s `Window_KeyDown`.

**Tech Stack:** C# / .NET 10, BotBuilder.Core (+ one WPF code-behind edit), xUnit.

**Reference spec:** `Docs/Specs/2026-06-05-node-copy-paste-design.md`.

**Merge handling:** core unit-tested, but the UX is editor-interactive → **user-verified PR, not self-merged.** Conflict-free with PRs #36/#37.

**`<WT>` = `C:\git\ADB\.claude\worktrees\node-copy-paste`. Windows: PowerShell for `dotnet`/`git`; NEVER redirect to `/dev/null`/`NUL`.**

**Relevant existing APIs (verified):** `NodeViewModel` exposes `Id`, `TypeKey`, `Label`, `X`, `Y`, `TargetId`, `RetryMaxAttempts`, `RetryDelayMs`, `Config` (`Dictionary<string,object>`), `InputPorts`/`OutputPorts` (`PortViewModel` with `.Name`), `SetBranchPortCount(int)`. `NodeViewModel.FromDefinition(def, id, label, x, y)`. `ConnectionViewModel(Guid id, NodeViewModel source, PortViewModel sourcePort, NodeViewModel target, PortViewModel targetPort)` with `.Source/.SourcePort/.Target/.TargetPort`. `BotEditorViewModel`: `Nodes`, `Connections`, `SelectedNode`, `_registry.Get(typeKey)`, `_undo.Execute(IUndoableCommand)`, `AfterEdit()`, `AddNodeCore/RemoveNodeCore/AddConnectionCore/RemoveConnectionCore`, `SelectNodes(IEnumerable<NodeViewModel>)`. `RunParallelAction.RunParallelTypeKey`/`BranchesKey`/`DefaultBranchCount`; `AdbCore.Actions.ConfigValues.GetInt(config, key, default)`.

---

## Task 1: Clipboard model + `CopySelection`

**Files:** Create `BotBuilder.Core/NodeClipboard.cs`; modify `BotBuilder.Core/BotEditorViewModel.cs`; create/modify `BotBuilder.Core.Tests/NodeCopyPasteTests.cs`.

- [ ] **Step 1: Read** `BotEditorViewModel.cs` (the `Select*`/`DeleteSelection` region ~150-178, the command-execution pattern, `AddNodeCore` etc.) and an existing test that builds an editor + adds nodes + connects them (search `BotBuilder.Core.Tests` for `AddNode(` and how connections are created in tests — e.g. a `Connect`/`AddConnection` test helper or direct `AddConnectionCore`). Reuse those patterns.

- [ ] **Step 2: Create `BotBuilder.Core/NodeClipboard.cs`:**
```csharp
namespace BotBuilder.Core;

internal sealed record NodeClip(
    string TypeKey, string Label, System.Guid? TargetId,
    int RetryMaxAttempts, int RetryDelayMs,
    System.Collections.Generic.Dictionary<string, object> Config, double X, double Y);

internal sealed record ConnectionClip(int SourceIndex, string SourcePort, int TargetIndex, string TargetPort);

internal sealed record NodeClipboard(
    System.Collections.Generic.IReadOnlyList<NodeClip> Nodes,
    System.Collections.Generic.IReadOnlyList<ConnectionClip> Connections);
```

- [ ] **Step 3: Write the failing test** for copy (in `BotBuilder.Core.Tests/NodeCopyPasteTests.cs`). Since the clipboard is internal, test copy *via its effect on Paste* (Task 2) — so for Task 1, write a minimal test that `CopySelection()` doesn't throw and enables a subsequent paste. Practically, fold the copy assertions into the Task 2 paste tests. For Task 1, add just:
```csharp
using System.Linq;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using Xunit;

namespace BotBuilder.Core.Tests;

public class NodeCopyPasteTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    [Fact]
    public void CopySelection_NothingSelected_IsNoOp_AndPasteDoesNothing()
    {
        var editor = NewEditor();
        editor.CopySelection();          // nothing selected
        var before = editor.Nodes.Count;
        editor.Paste();
        Assert.Equal(before, editor.Nodes.Count);
    }
}
```
(Adapt `NewEditor()` to the real registry-construction the other tests use.)

- [ ] **Step 4: Run to verify it fails** — `dotnet test "<WT>\BotBuilder.Core.Tests" --filter "FullyQualifiedName~NodeCopyPasteTests"` → compile FAIL (`CopySelection`/`Paste` missing).

- [ ] **Step 5: Implement `CopySelection` + the `_clipboard` field in `BotEditorViewModel`:**
```csharp
    private NodeClipboard? _clipboard;

    /// <summary>Snapshots the selected nodes (or the single SelectedNode) and the connections among them
    /// into the in-app clipboard. No-op when nothing is selected.</summary>
    public void CopySelection()
    {
        var selected = Nodes.Where(n => n.IsSelected).ToList();
        if (selected.Count == 0 && SelectedNode is not null) selected.Add(SelectedNode);
        if (selected.Count == 0) return;

        var indexOf = new Dictionary<NodeViewModel, int>();
        for (var i = 0; i < selected.Count; i++) indexOf[selected[i]] = i;

        var nodeClips = selected
            .Select(n => new NodeClip(n.TypeKey, n.Label, n.TargetId, n.RetryMaxAttempts, n.RetryDelayMs,
                new Dictionary<string, object>(n.Config), n.X, n.Y))
            .ToList();

        var connClips = Connections
            .Where(c => indexOf.ContainsKey(c.Source) && indexOf.ContainsKey(c.Target))
            .Select(c => new ConnectionClip(indexOf[c.Source], c.SourcePort.Name, indexOf[c.Target], c.TargetPort.Name))
            .ToList();

        _clipboard = new NodeClipboard(nodeClips, connClips);
    }
```
(Add `using System.Collections.Generic;`/`System.Linq;` if not implicit.)

- [ ] **Step 6:** Leave `Paste()` as a stub that compiles for now (Task 2 implements it) — OR implement Paste in this task too. Cleanest: implement a temporary `public void Paste() { }` so Task 1 compiles + its no-op test passes, then Task 2 fills it in. Run the Task 1 test → green.

- [ ] **Step 7: Commit:**
```
git -C "<WT>" add BotBuilder.Core/NodeClipboard.cs BotBuilder.Core/BotEditorViewModel.cs BotBuilder.Core.Tests/NodeCopyPasteTests.cs
git -C "<WT>" commit -m "feat(editor): node clipboard + CopySelection (internal connections)"
```

---

## Task 2: `Paste` + `PasteCommand`

**Files:** modify `BotBuilder.Core/BotEditorViewModel.cs`, `BotBuilder.Core/Undo/EditorCommands.cs`, `BotBuilder.Core.Tests/NodeCopyPasteTests.cs`.

- [ ] **Step 1: Add the `PasteCommand`** to `EditorCommands.cs` (mirrors `DeleteNodesCommand` inverse):
```csharp
/// <summary>Adds a pasted set of nodes and connections; undo removes them.</summary>
internal sealed class PasteCommand : IUndoableCommand
{
    private readonly BotEditorViewModel _editor;
    private readonly IReadOnlyList<NodeViewModel> _nodes;
    private readonly IReadOnlyList<ConnectionViewModel> _connections;

    public PasteCommand(BotEditorViewModel editor, IReadOnlyList<NodeViewModel> nodes, IReadOnlyList<ConnectionViewModel> connections)
    {
        _editor = editor;
        _nodes = nodes;
        _connections = connections;
    }

    public void Do()
    {
        foreach (var n in _nodes) { _editor.AddNodeCore(n); }
        foreach (var c in _connections) { _editor.AddConnectionCore(c); }
    }

    public void Undo()
    {
        foreach (var c in _connections) { _editor.RemoveConnectionCore(c); }
        foreach (var n in _nodes) { _editor.RemoveNodeCore(n); }
    }
}
```

- [ ] **Step 2: Write the failing tests** in `NodeCopyPasteTests.cs` (adapt the connection-creation to how tests connect nodes — likely via `editor.AddConnectionCore(new ConnectionViewModel(...))` or an existing connect helper; build a small local helper if needed):
```csharp
    [Fact]
    public void Paste_SingleNode_ClonesWithNewIdAndOffset()
    {
        var editor = NewEditor();
        var src = editor.AddNode("data.setVariable", 100, 100);
        src.Config["name"] = "counter";
        editor.Select(src);
        editor.CopySelection();
        editor.Paste();

        Assert.Equal(2, editor.Nodes.Count);
        var pasted = editor.Nodes.Last();
        Assert.NotEqual(src.Id, pasted.Id);
        Assert.Equal("data.setVariable", pasted.TypeKey);
        Assert.Equal("counter", pasted.Config["name"]);
        Assert.Equal(124, pasted.X);
        Assert.Equal(124, pasted.Y);
        Assert.True(pasted.IsSelected);
        Assert.False(src.IsSelected);
    }

    [Fact]
    public void Paste_TwoConnectedNodes_ClonesNodesAndInternalConnection()
    {
        var editor = NewEditor();
        var a = editor.AddNode("control.start", 0, 0);
        var b = editor.AddNode("data.log", 200, 0);
        Connect(editor, a, "out", b, "in");           // local helper (see note)
        editor.SelectNodes(new[] { a, b });
        editor.CopySelection();
        var connsBefore = editor.Connections.Count;
        editor.Paste();

        Assert.Equal(4, editor.Nodes.Count);
        Assert.Equal(connsBefore + 1, editor.Connections.Count);
        var pastedConn = editor.Connections.Last();
        // pasted connection joins the two pasted nodes, not the originals
        Assert.DoesNotContain(pastedConn.Source, new[] { a, b });
        Assert.DoesNotContain(pastedConn.Target, new[] { a, b });
    }

    [Fact]
    public void Paste_ConnectionToUnselectedNode_IsNotCopied()
    {
        var editor = NewEditor();
        var a = editor.AddNode("control.start", 0, 0);
        var b = editor.AddNode("data.log", 200, 0);
        Connect(editor, a, "out", b, "in");
        editor.Select(a);                  // only A selected
        editor.CopySelection();
        editor.Paste();
        // one new node (A'), no new connection (the A->B edge had a non-selected endpoint)
        Assert.Equal(3, editor.Nodes.Count);
        Assert.Single(editor.Connections);
    }

    [Fact]
    public void Paste_RunParallel_PreservesBranchPorts()
    {
        var editor = NewEditor();
        var rp = editor.AddNode(RunParallelAction.RunParallelTypeKey, 0, 0);
        rp.Config[RunParallelAction.BranchesKey] = 3;
        rp.SetBranchPortCount(3);
        editor.Select(rp);
        editor.CopySelection();
        editor.Paste();
        var pasted = editor.Nodes.Last();
        Assert.Equal(3, pasted.OutputPorts.Count);
    }

    [Fact]
    public void Paste_IsSingleUndoStep()
    {
        var editor = NewEditor();
        var a = editor.AddNode("control.start", 0, 0);
        var b = editor.AddNode("data.log", 200, 0);
        Connect(editor, a, "out", b, "in");
        editor.SelectNodes(new[] { a, b });
        editor.CopySelection();
        editor.Paste();
        Assert.Equal(4, editor.Nodes.Count);
        editor.Undo();
        Assert.Equal(2, editor.Nodes.Count);
        Assert.Single(editor.Connections);
    }

    // Local helper — adapt to the real connect path used by other tests.
    private static void Connect(BotEditorViewModel editor, NodeViewModel src, string outPort, NodeViewModel tgt, string inPort)
    {
        var sp = src.OutputPorts.First(p => p.Name == outPort);
        var tp = tgt.InputPorts.First(p => p.Name == inPort);
        editor.AddConnectionCore(new BotBuilder.Core.Connections.ConnectionViewModel(System.Guid.NewGuid(), src, sp, tgt, tp));
    }
```
NOTE: confirm the real input/output port names (`control.start`'s output is likely `"out"`; `data.log`'s input `"in"`). READ the actual port names from those action defs (`StartAction`, `LogAction`) and use the real ones. If `editor.Undo()` isn't public, use the real undo entry point (the editor exposes Undo via a method/command — find it). If `AddConnectionCore` is `internal`, the test project likely has `InternalsVisibleTo` (other tests use internal members) — confirm; else use the real public connect method.

- [ ] **Step 3: Run to verify they fail.**

- [ ] **Step 4: Implement `Paste()` in `BotEditorViewModel`:**
```csharp
    /// <summary>Pastes the clipboard: fresh nodes (new Ids, offset +24,+24), the internal connections among
    /// them re-created by port name, added as one undoable step and selected. No-op when the clipboard is empty.</summary>
    public void Paste()
    {
        if (_clipboard is null || _clipboard.Nodes.Count == 0) return;
        const double dx = 24, dy = 24;

        var newNodes = new List<NodeViewModel>(_clipboard.Nodes.Count);
        foreach (var clip in _clipboard.Nodes)
        {
            var definition = _registry.Get(clip.TypeKey);
            var node = NodeViewModel.FromDefinition(definition, Guid.NewGuid(), clip.Label, clip.X + dx, clip.Y + dy);
            node.TargetId = clip.TargetId;
            node.RetryMaxAttempts = clip.RetryMaxAttempts;
            node.RetryDelayMs = clip.RetryDelayMs;
            node.Config.Clear();
            foreach (var kv in clip.Config) { node.Config[kv.Key] = kv.Value; }
            if (node.TypeKey == RunParallelAction.RunParallelTypeKey)
            {
                node.SetBranchPortCount(Math.Max(2, ConfigValues.GetInt(node.Config, RunParallelAction.BranchesKey, RunParallelAction.DefaultBranchCount)));
            }
            newNodes.Add(node);
        }

        var newConnections = new List<ConnectionViewModel>(_clipboard.Connections.Count);
        foreach (var cc in _clipboard.Connections)
        {
            var source = newNodes[cc.SourceIndex];
            var target = newNodes[cc.TargetIndex];
            var sp = source.OutputPorts.FirstOrDefault(p => p.Name == cc.SourcePort);
            var tp = target.InputPorts.FirstOrDefault(p => p.Name == cc.TargetPort);
            if (sp is not null && tp is not null)
            {
                newConnections.Add(new ConnectionViewModel(Guid.NewGuid(), source, sp, target, tp));
            }
        }

        _undo.Execute(new PasteCommand(this, newNodes, newConnections));
        SelectNodes(newNodes);
        AfterEdit();
    }
```
(Add `using AdbCore.Actions;` for `ConfigValues`, `using BotBuilder.Core.Connections;` for `ConnectionViewModel`, `using AdbCore.Actions.BuiltIn;` for `RunParallelAction` — most are already imported in this file.)

- [ ] **Step 5: Run to verify they pass** — `--filter "FullyQualifiedName~NodeCopyPasteTests"` → green.

- [ ] **Step 6: Commit:**
```
git -C "<WT>" add BotBuilder.Core/BotEditorViewModel.cs BotBuilder.Core/Undo/EditorCommands.cs BotBuilder.Core.Tests/NodeCopyPasteTests.cs
git -C "<WT>" commit -m "feat(editor): Paste (clone + remap internal connections, single undo step)"
```

---

## Task 3: Ctrl+C / Ctrl+V keybindings + full sweep

**Files:** modify `BotBuilder/MainWindow.xaml.cs`.

- [ ] **Step 1: Read** `BotBuilder/MainWindow.xaml.cs` `Window_KeyDown` (~line 181) — it switches on `e.Key` with `Keyboard.Modifiers` (Ctrl+Z undo, Ctrl+Y redo, Delete). Add Copy/Paste cases mirroring the existing style:
```csharp
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _editor.CopySelection();
            e.Handled = true;
        }
        else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _editor.Paste();
            e.Handled = true;
        }
```
Match the exact `e.Handled`/return style used by the surrounding cases (read them and be consistent — e.g. if they `return;` instead of `e.Handled = true`, do the same).

- [ ] **Step 2: Build + full sweep.** `dotnet build "<WT>\ADB.slnx" -warnaserror -v q --nologo` → 0 warnings; `dotnet test "<WT>\ADB.slnx"` → all green. Report totals. (No unit test for the keybinding — visual/manual verify.)

- [ ] **Step 3: Commit:**
```
git -C "<WT>" add BotBuilder/MainWindow.xaml.cs
git -C "<WT>" commit -m "feat(editor): Ctrl+C / Ctrl+V copy-paste keybindings"
```

---

## Self-Review Notes (addressed)

- **Spec coverage:** clipboard model + CopySelection internal-connection filter (Task 1); Paste clone+remap+offset+select+single-undo, empty no-op, Run Parallel ports (Task 2); Ctrl+C/V trigger (Task 3). ✓
- **Deep config copy** (`new Dictionary<>(n.Config)` on copy + `Config.Clear()`+copy on paste) so clones are independent. ✓
- **Conflict-free:** new `NodeClipboard.cs`, `PasteCommand` (append to EditorCommands), `BotEditorViewModel` Copy/Paste, `MainWindow.xaml.cs` keybinding — none are PR #36/#37 files. ✓
- **Type consistency:** `NodeClipboard`/`NodeClip`/`ConnectionClip`, `CopySelection()`/`Paste()`, `PasteCommand(editor, nodes, connections)`. ✓
- **Editor UX → user-verified PR, not self-merge.**
