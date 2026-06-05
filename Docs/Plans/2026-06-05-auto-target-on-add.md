# Auto-Target on Node-Add Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** On palette node-add, pre-assign the node's target to the lone target of its matching type (editor companion to PR #36's runtime defaulting).

**Architecture:** A pure `NodeTargetType.For(category) → BotTargetType?` mapping helper; `BotEditorViewModel.AddNode` uses it to set `node.TargetId` to the single matching-type target (or null).

**Tech Stack:** C# / .NET 10, BotBuilder.Core, xUnit.

**Reference spec:** `Docs/Specs/2026-06-05-auto-target-on-add-design.md`.

**Merge handling:** pure editor-VM logic, deterministic unit tests, no rendering → **self-merged** via `gh`. Independent of PRs #36/#37 (no shared files).

**`<WT>` = `C:\git\ADB\.claude\worktrees\auto-target-on-add`. Windows: PowerShell for `dotnet`/`git`; NEVER redirect to `/dev/null`/`NUL`.**

---

## Task 1: `NodeTargetType` mapping helper

**Files:** Create `BotBuilder.Core/Targets/NodeTargetType.cs`, `BotBuilder.Core.Tests/Targets/NodeTargetTypeTests.cs`.

- [ ] **Step 1: Read** `AdbCore/Models/BotTargetType.cs` (confirm values `Window`, `AndroidDevice`, `Browser`) and the `BotBuilder.Core/Targets/` namespace (for the right `using`/namespace).

- [ ] **Step 2: Write the failing test.** `BotBuilder.Core.Tests/Targets/NodeTargetTypeTests.cs`:
```csharp
using AdbCore.Models;
using BotBuilder.Core.Targets;
using Xunit;

namespace BotBuilder.Core.Tests.Targets;

public class NodeTargetTypeTests
{
    [Theory]
    [InlineData("Android", BotTargetType.AndroidDevice)]
    [InlineData("Browser", BotTargetType.Browser)]
    [InlineData("Screen", BotTargetType.Window)]
    [InlineData("Input", BotTargetType.Window)]
    public void For_KnownCategories_MapToTargetType(string category, BotTargetType expected)
        => Assert.Equal(expected, NodeTargetType.For(category));

    [Theory]
    [InlineData("Control Flow")]
    [InlineData("Data")]
    [InlineData("Scripting")]
    [InlineData("Whatever")]
    public void For_TargetAgnosticCategories_ReturnNull(string category)
        => Assert.Null(NodeTargetType.For(category));
}
```

- [ ] **Step 3: Run to verify it fails** — `dotnet test "<WT>\BotBuilder.Core.Tests" --filter "FullyQualifiedName~NodeTargetTypeTests"` → compile FAIL.

- [ ] **Step 4: Create `BotBuilder.Core/Targets/NodeTargetType.cs`:**
```csharp
using AdbCore.Models;

namespace BotBuilder.Core.Targets;

/// <summary>Maps an action's design-time <c>Category</c> to the target type its nodes act on, used to
/// auto-assign a target when a node is added. Window-acting categories (Screen, Input — both resolve to a
/// window HWND at runtime) map to <see cref="BotTargetType.Window"/>; target-agnostic categories
/// (Control Flow / Data / Scripting / unknown) map to null.</summary>
public static class NodeTargetType
{
    public static BotTargetType? For(string category) => category switch
    {
        "Android" => BotTargetType.AndroidDevice,
        "Browser" => BotTargetType.Browser,
        "Screen" => BotTargetType.Window,
        "Input" => BotTargetType.Window,
        _ => null,
    };
}
```

- [ ] **Step 5: Run to verify it passes** — green.

- [ ] **Step 6: Commit:**
```
git -C "<WT>" add BotBuilder.Core/Targets/NodeTargetType.cs BotBuilder.Core.Tests/Targets/NodeTargetTypeTests.cs
git -C "<WT>" commit -m "feat(editor): NodeTargetType category->target-type mapping"
```

---

## Task 2: Auto-assign in `AddNode`

**Files:** modify `BotBuilder.Core/BotEditorViewModel.cs`; modify/create `BotBuilder.Core.Tests/BotEditorViewModelTests.cs` (or wherever AddNode is tested — find it).

- [ ] **Step 1: Read** `BotBuilder.Core/BotEditorViewModel.cs` (`AddNode` ~line 55, `TargetBar.Targets`, `AssignTarget`, how the registry/targets are set up) and the existing test that constructs a `BotEditorViewModel` + adds a node + seeds targets (search `BotBuilder.Core.Tests` for `AddNode(` and `TargetBar` usage). Confirm `TargetViewModel` exposes `Id` (Guid) and `Type` (BotTargetType). Reuse the existing editor-construction test helper.

- [ ] **Step 2: Write the failing tests** (adapt construction to the existing test style — use the real registry the editor is built with so `Get(typeKey)` resolves; pick concrete typeKeys: an Android action e.g. `android.findImage`, a Screen action e.g. `screen.findImage`/`ScreenActionBase` subclass key, an Input action e.g. `input.click`/tap, a Control-Flow action e.g. `control.branch` or `flow.delay`. READ the registry/registered TypeKeys to use real keys):
```csharp
    [Fact]
    public void AddNode_OneMatchingTypeTarget_AutoAssigns()
    {
        var editor = /* build editor with full built-in registry, as existing tests do */;
        var android = AddTarget(editor, BotTargetType.AndroidDevice, "Emu");   // helper that seeds TargetBar.Targets
        var node = editor.AddNode("android.findImage", 10, 10);                // a real Android typeKey
        Assert.Equal(android.Id, node.TargetId);
    }

    [Fact]
    public void AddNode_TwoMatchingTargets_LeavesUnassigned()
    {
        var editor = /* ... */;
        AddTarget(editor, BotTargetType.AndroidDevice, "A");
        AddTarget(editor, BotTargetType.AndroidDevice, "B");
        var node = editor.AddNode("android.findImage", 10, 10);
        Assert.Null(node.TargetId);
    }

    [Fact]
    public void AddNode_TargetAgnosticNode_NeverAssigns()
    {
        var editor = /* ... */;
        AddTarget(editor, BotTargetType.AndroidDevice, "A");
        var node = editor.AddNode("control.branch", 10, 10);   // a real target-agnostic typeKey
        Assert.Null(node.TargetId);
    }

    [Fact]
    public void AddNode_WindowNodeWithLoneWindowTarget_AutoAssigns()
    {
        var editor = /* ... */;
        var win = AddTarget(editor, BotTargetType.Window, "Notepad");
        var node = editor.AddNode("input.click", 10, 10);      // a real Input/Screen typeKey -> Window
        Assert.Equal(win.Id, node.TargetId);
    }
```
(If a seeding helper for `TargetBar.Targets` doesn't exist, add a tiny local one that constructs a `TargetViewModel { Type = ..., Name = ... }` with a fresh `Id` and adds it to `editor.TargetBar.Targets`. Match how `TargetViewModel` is constructed elsewhere — it may have an `Id` set in ctor or as a property.)

- [ ] **Step 3: Run to verify they fail.**

- [ ] **Step 4: Implement in `BotEditorViewModel.AddNode`:**
```csharp
    public NodeViewModel AddNode(string typeKey, double x, double y)
    {
        var definition = _registry.Get(typeKey);
        var node = NodeViewModel.FromDefinition(definition, Guid.NewGuid(), definition.DisplayName, x, y);
        node.TargetId = AutoTargetFor(definition.Category);
        _undo.Execute(new AddNodeCommand(this, node));
        AfterEdit();
        return node;
    }

    /// <summary>The lone target whose type matches the node's category, or null when the category is
    /// target-agnostic or there isn't exactly one matching-type target.</summary>
    private Guid? AutoTargetFor(string category)
    {
        if (NodeTargetType.For(category) is not BotTargetType type) return null;
        Guid? found = null;
        var count = 0;
        foreach (var t in TargetBar.Targets)
        {
            if (t.Type == type) { found = t.Id; count++; }
        }
        return count == 1 ? found : null;
    }
```
(Add `using AdbCore.Models;` / `using BotBuilder.Core.Targets;` if not already imported — the file already has `using BotBuilder.Core.Targets;`.)

- [ ] **Step 5: Run to verify they pass** — `--filter "FullyQualifiedName~BotEditorViewModel"` (and the AddNode tests) → green.

- [ ] **Step 6: Full build + sweep.** `dotnet build "<WT>\ADB.slnx" -warnaserror -v q --nologo` → 0 warnings; `dotnet test "<WT>\ADB.slnx"` → all green. Report totals.

- [ ] **Step 7: Commit:**
```
git -C "<WT>" add BotBuilder.Core/BotEditorViewModel.cs BotBuilder.Core.Tests/BotEditorViewModelTests.cs
git -C "<WT>" commit -m "feat(editor): auto-assign lone matching-type target on node-add"
```

---

## Self-Review Notes (addressed)

- **Spec coverage:** mapping helper (Task 1); AddNode auto-assign with single/ambiguous/none/agnostic cases (Task 2). ✓
- **Scope:** only on add; load/paste untouched; ambiguous left unassigned. ✓
- **Type consistency:** `NodeTargetType.For(string)→BotTargetType?`, `AutoTargetFor(string)→Guid?`, `TargetViewModel.Id`/`.Type`. ✓
- **Conflict-free:** new `NodeTargetType.cs` + `BotEditorViewModel.AddNode` only — disjoint from #36 (AdbCore) and #37 (canvas/node files + MainWindow.xaml). ✓
- **No rendering/visual surface → self-mergeable.**
