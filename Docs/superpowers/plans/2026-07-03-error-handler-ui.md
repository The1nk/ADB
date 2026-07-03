# Error Handler node — editor UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Enforce **one Error Handler node per bot** in the editor, and confirm the node surfaces and renders correctly (palette + canvas).

**Architecture:** The `control.errorHandler` node was added to the engine + registry in a prior PR, so it already appears in the **Control Flow** palette (the palette lists every registered definition) and renders like `Start` (empty input ports, single output). The only new code is a single-instance guard in `BotEditorViewModel.AddNode`: attempting to add a second Error Handler surfaces (selects) the existing one instead of duplicating it. The engine tolerates duplicates (first wins), so this is UX polish, not a correctness fix.

**Tech Stack:** C# / .NET 10, WPF (BotBuilder), MVVM view-models in BotBuilder.Core, xUnit.

**Spec:** `Docs/superpowers/specs/2026-07-03-nested-logging-and-error-handler-design.md` (Feature 2, UI slice).

**Delivery:** This is a **visual slice — park the PR for the user to verify and merge** (they confirm the node appears in the palette, renders with only an output port, and that dropping a second selects the existing one).

---

### Task 1: Single-instance guard for the Error Handler node

**Files:**
- Modify: `BotBuilder.Core/BotEditorViewModel.cs` (`AddNode`, around line 82)
- Test: `BotBuilder.Core.Tests/ErrorHandlerNodeTests.cs` (create)

`BotEditorViewModel.cs` already has `using AdbCore.Actions.BuiltIn;` (so `ErrorHandlerAction.Key` resolves) and a `Select(NodeViewModel?)` method (line ~289) that single-selects a node. `System.Linq` is available via implicit usings.

- [ ] **Step 1: Write the failing tests**

Create `BotBuilder.Core.Tests/ErrorHandlerNodeTests.cs`:

```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using BotBuilder.Core.Palette;
using Xunit;

namespace BotBuilder.Core.Tests;

public class ErrorHandlerNodeTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    [Fact]
    public void AddNode_FirstErrorHandler_IsAdded()
    {
        var editor = NewEditor();

        var node = editor.AddNode(ErrorHandlerAction.Key, 10, 20);

        Assert.Contains(node, editor.Nodes);
        Assert.Equal(ErrorHandlerAction.Key, node.TypeKey);
    }

    [Fact]
    public void AddNode_SecondErrorHandler_DoesNotDuplicate_AndSelectsExisting()
    {
        var editor = NewEditor();
        var first = editor.AddNode(ErrorHandlerAction.Key, 0, 0);

        var second = editor.AddNode(ErrorHandlerAction.Key, 100, 100);

        Assert.Same(first, second);
        Assert.Single(editor.Nodes, n => n.TypeKey == ErrorHandlerAction.Key);
        Assert.True(first.IsSelected);
    }

    [Fact]
    public void Palette_IncludesErrorHandler_UnderControlFlow()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        var palette = new PaletteViewModel(defs);

        var controlFlow = Assert.Single(palette.Categories, c => c.Name == "Control Flow");
        Assert.Contains(controlFlow.Items, i => i.TypeKey == ErrorHandlerAction.Key);
    }
}
```

- [ ] **Step 2: Run the tests to verify the guard test fails**

Run: `dotnet test BotBuilder.Core.Tests/BotBuilder.Core.Tests.csproj --filter "FullyQualifiedName~ErrorHandlerNodeTests"`
Expected: `AddNode_SecondErrorHandler_DoesNotDuplicate_AndSelectsExisting` FAILS (a second node is currently added). The other two PASS already (palette presence + first-add are automatic).

- [ ] **Step 3: Add the guard to `AddNode`**

In `BotBuilder.Core/BotEditorViewModel.cs`, replace:

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
```

with:

```csharp
    public NodeViewModel AddNode(string typeKey, double x, double y)
    {
        // At most one Error Handler per bot: the engine routes unhandled failures to the first one, so a second
        // would be dead weight. Rather than add a duplicate, surface the existing handler (select it) so the
        // user sees it's already on the canvas.
        if (typeKey == ErrorHandlerAction.Key
            && Nodes.FirstOrDefault(n => n.TypeKey == ErrorHandlerAction.Key) is { } existing)
        {
            Select(existing);
            return existing;
        }

        var definition = _registry.Get(typeKey);
        var node = NodeViewModel.FromDefinition(definition, Guid.NewGuid(), definition.DisplayName, x, y);
        node.TargetId = AutoTargetFor(definition.Category);
        _undo.Execute(new AddNodeCommand(this, node));
        AfterEdit();
        return node;
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test BotBuilder.Core.Tests/BotBuilder.Core.Tests.csproj --filter "FullyQualifiedName~ErrorHandlerNodeTests"`
Expected: PASS (all three).

- [ ] **Step 5: Run the full BotBuilder.Core suite (no regressions)**

Run: `dotnet test BotBuilder.Core.Tests/BotBuilder.Core.Tests.csproj`
Expected: PASS. (Existing `AddNode` tests are unaffected — the guard only triggers for `control.errorHandler`.)

- [ ] **Step 6: Commit**

```bash
git add BotBuilder.Core/BotEditorViewModel.cs BotBuilder.Core.Tests/ErrorHandlerNodeTests.cs
git commit -m "Editor: enforce a single Error Handler node per bot"
```

---

## Self-Review

- **Spec coverage (Feature 2 UI):** palette presence — automatic + test ✓; node renders with no input port — automatic (Start precedent; verified by the user at merge) ✓; single-instance guard + selection feedback — test ✓; canvas highlight during run — automatic (normal action id) ✓.
- **Placeholder scan:** none.
- **Type consistency:** `ErrorHandlerAction.Key`, `Nodes` (ObservableCollection<NodeViewModel>), `NodeViewModel.TypeKey`, `NodeViewModel.IsSelected`, `Select(NodeViewModel?)`, `PaletteViewModel(ActionRegistry)`, `PaletteCategory.Name`/`.Items`, `PaletteItem.TypeKey` — all match existing code.
