# Editor Usability — Plan A (Backend-Only) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the nested-bot selection-clearing bug and add a "Throw Error" control-flow action.

**Architecture:** Two independent, backend-only changes. (1) Guard `PropertiesViewModel.SelectedNestedBotId` against the spurious `null` WPF writes when the picker's `ItemsSource` swaps after a nested bot is edited; route deliberate unassignment through `RemoveSelectedNestedBot()` directly. (2) Add `ThrowErrorAction` (`IActionDefinition` + `IActionExecutor`) that returns `ActionResult.Fail(message)` — a terminal node whose failure escapes loops and returns control from a nested bot to its parent.

**Tech Stack:** C# / .NET 10, xUnit, CommunityToolkit.Mvvm. Source spec: `Docs/superpowers/specs/2026-07-04-editor-usability-batch-design.md` (items 1 & 2).

**Commit convention:** end every commit message body with the line `Claude-Session: https://claude.ai/code/session_01UvcnQr4NvWncn1DeKnC38a` (repo Bash rule).

---

## File Structure

- `BotBuilder.Core/Properties/PropertiesViewModel.cs` — modify the `SelectedNestedBotId` setter and `RemoveSelectedNestedBot()`.
- `BotBuilder.Core.Tests/Properties/NestedBotPropertiesTests.cs` — add a regression test for the spurious-null case.
- `AdbCore/Actions/BuiltIn/ThrowErrorAction.cs` — new action (create).
- `AdbCore.Tests/Actions/BuiltIn/ThrowErrorActionTests.cs` — new tests (create).
- `AdbCore/Actions/BuiltIn/BuiltInActions.cs` — register the new action.
- `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs` — update registry counts + key list.
- Docs: `CLAUDE.md`, `README.md`, `ADB.wiki/Control-Flow.md`, `ADB.wiki/Actions-Reference.md`.

---

## Task 1: Fix nested-bot selection-clearing bug

**Files:**
- Modify: `BotBuilder.Core/Properties/PropertiesViewModel.cs` (setter at lines 56-84; `RemoveSelectedNestedBot` at 128-136)
- Test: `BotBuilder.Core.Tests/Properties/NestedBotPropertiesTests.cs`

- [ ] **Step 1: Write the failing regression test**

Add this test to `NestedBotPropertiesTests.cs` (uses the existing `NewEditor()` helper):

```csharp
[Fact]
public void SelectedNestedBotId_NullFromPicker_KeepsAssignment()
{
    var editor = NewEditor();
    var node = editor.AddNode(NestedBotAction.NestedBotTypeKey, 0, 0);
    editor.Select(node);
    var bot = editor.NestedBotLibrary.AddNew("Sub");
    editor.Properties.SelectedNestedBotId = bot.Id;

    // Simulates the spurious WPF writeback: the ComboBox pushes null when its ItemsSource swaps and the
    // previously-selected Bot instance was replaced by reference (after a nested bot was edited/synced back).
    editor.Properties.SelectedNestedBotId = null;

    Assert.Equal(bot.Id, editor.Properties.SelectedNestedBotId);
    Assert.Equal(bot.Id.ToString(), node.Config[NestedBotAction.NestedBotIdKey]);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotPropertiesTests.SelectedNestedBotId_NullFromPicker_KeepsAssignment"`
Expected: FAIL — the current `else` branch removes the config key, so `node.Config[...]` throws `KeyNotFoundException` / `SelectedNestedBotId` is null.

- [ ] **Step 3: Rewrite the setter to ignore spurious null**

Replace the entire `SelectedNestedBotId` setter (the `set { ... }` block) with:

```csharp
        set
        {
            if (Node is null) { return; }
            if (value is not Guid id)
            {
                // The picker has no "unassigned" item, so a null from the binding is always spurious: WPF
                // pushes it when the ComboBox's ItemsSource swaps and the prior selection's Bot instance was
                // replaced by reference (e.g. after a nested bot was edited and synced back). Ignore it and
                // snap the picker back to the node's real value. Deliberate unassignment goes through
                // RemoveSelectedNestedBot(), which clears the config directly.
                OnPropertyChanged(nameof(SelectedNestedBotId));
                return;
            }
            if (_editor.NestedBotLibrary.WouldCreateCycle(_editor.BotId, id))
            {
                CycleWarning = "That would make this bot run itself (a nested-bot cycle).";
                OnPropertyChanged(nameof(SelectedNestedBotId)); // snap the picker back
                return;
            }
            Node.Config[NestedBotAction.NestedBotIdKey] = id.ToString();
            CycleWarning = null;
            _editor.MarkDirty();
            _editor.RefreshNestedBotSubtitles();
            OnPropertyChanged(nameof(SelectedNestedBotName));
            OnPropertyChanged(nameof(SelectedNestedBotEditableName));
        }
```

- [ ] **Step 4: Rewrite `RemoveSelectedNestedBot` to clear config directly**

Replace the whole `RemoveSelectedNestedBot()` method with:

```csharp
    /// <summary>Removes the selected entry from the library and unassigns the card. Clears the config key
    /// directly rather than via the SelectedNestedBotId setter (which now ignores null as a spurious
    /// picker writeback).</summary>
    public void RemoveSelectedNestedBot()
    {
        if (Node is null || SelectedNestedBotId is not Guid id) { return; }
        _editor.NestedBotLibrary.Remove(id);
        Node.Config.Remove(NestedBotAction.NestedBotIdKey);
        _editor.MarkDirty();
        _editor.RefreshNestedBotSubtitles();
        OnPropertyChanged(nameof(NestedBotEntries));
        OnPropertyChanged(nameof(SelectedNestedBotId));
        OnPropertyChanged(nameof(SelectedNestedBotName));
        OnPropertyChanged(nameof(SelectedNestedBotEditableName));
    }
```

- [ ] **Step 5: Run the nested-bot property tests to verify all pass**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotPropertiesTests"`
Expected: PASS — the new regression test passes, and the existing `RemoveSelectedNestedBot_RemovesFromLibraryAndUnassigns` and `SelectedNestedBotId_RoundTripsToConfig` still pass.

- [ ] **Step 6: Commit**

```bash
git add BotBuilder.Core/Properties/PropertiesViewModel.cs BotBuilder.Core.Tests/Properties/NestedBotPropertiesTests.cs
git commit -m "Fix nested-bot picker clearing on next card after editing a nested bot"
```

---

## Task 2: Add ThrowErrorAction (definition + executor)

**Files:**
- Create: `AdbCore/Actions/BuiltIn/ThrowErrorAction.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/ThrowErrorActionTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `AdbCore.Tests/Actions/BuiltIn/ThrowErrorActionTests.cs`:

```csharp
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Actions.BuiltIn;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class ThrowErrorActionTests
{
    private static ActionExecutionContext Ctx(BotAction action)
        => new(action, new BotExecutionContext(), _ => { });

    [Fact]
    public async Task Execute_ReturnsFailure_WithConfiguredMessage()
    {
        var action = new BotAction();
        action.Config[ThrowErrorAction.MessageKey] = "boom";

        var result = await new ThrowErrorAction().ExecuteAsync(Ctx(action), default);

        Assert.False(result.Success);
        Assert.Equal("boom", result.ErrorMessage);
    }

    [Fact]
    public async Task Execute_MissingMessage_UsesDefault()
    {
        var result = await new ThrowErrorAction().ExecuteAsync(Ctx(new BotAction()), default);

        Assert.False(result.Success);
        Assert.Equal(ThrowErrorAction.DefaultMessage, result.ErrorMessage);
    }

    [Fact]
    public void Definition_IsTerminalControlFlow_NoRetry()
    {
        var def = new ThrowErrorAction();

        Assert.Equal("control.throwError", def.TypeKey);
        Assert.Equal("Control Flow", def.Category);
        Assert.Equal(new[] { "in" }, def.InputPorts.Select(p => p.Name));
        Assert.Empty(def.OutputPorts);
        Assert.False(def.SupportsRetry);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~ThrowErrorActionTests"`
Expected: FAIL — `ThrowErrorAction` does not exist (compile error).

- [ ] **Step 3: Create the action**

Create `AdbCore/Actions/BuiltIn/ThrowErrorAction.cs`:

```csharp
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Deliberately fails the run with a configurable message. Terminal (one input, no outputs), so its
/// failure always propagates: it escapes an enclosing Loop and, inside a nested bot, returns control to the
/// parent — unlike End, which returns Ok and merely dead-ends the current path. Caught by the bot's Error
/// Handler if one is present, otherwise it bubbles up. The message is ${var}-interpolated by the engine
/// before execution.</summary>
public sealed class ThrowErrorAction : IActionDefinition, IActionExecutor
{
    public const string MessageKey = "message";
    public const string DefaultMessage = "Bot threw an error.";

    public string TypeKey => "control.throwError";
    public string DisplayName => "Throw Error";
    public string Category => "Control Flow";
    public string Description => "Fails the run with a message; use it to exit a nested bot or break out to an error handler.";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public List<PortDefinition> OutputPorts { get; } = new();
    public List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = MessageKey, Label = "Message", Type = ConfigFieldType.String, DefaultValue = DefaultMessage },
    };
    public bool SupportsRetry => false;

    public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        var message = ConfigValues.GetString(context.Action.Config, MessageKey, DefaultMessage);
        return Task.FromResult(ActionResult.Fail(string.IsNullOrWhiteSpace(message) ? DefaultMessage : message));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~ThrowErrorActionTests"`
Expected: PASS (all three).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Actions/BuiltIn/ThrowErrorAction.cs AdbCore.Tests/Actions/BuiltIn/ThrowErrorActionTests.cs
git commit -m "Add Throw Error control-flow action (terminal, fails with a message)"
```

---

## Task 3: Register ThrowErrorAction and update the registry test

**Files:**
- Modify: `AdbCore/Actions/BuiltIn/BuiltInActions.cs:19` (near `Add(new EndAction()...)`)
- Modify: `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs` (key list + counts, lines 22-42)

- [ ] **Step 1: Update the registration test first (TDD)**

In `BuiltInActionsTests.cs`, add `"control.throwError"` to the key array that expects **both** registries (after `"control.errorHandler",` on line 24):

```csharp
            "control.start", "control.end", "control.errorHandler", "control.throwError", "data.log", "control.delay", "control.branch",
```

Then bump the two count assertions (lines 41-42) by one each:

```csharp
        Assert.Equal(54, defs.Count);
        Assert.Equal(50, execs.Count);
```

- [ ] **Step 2: Run the registration test to verify it fails**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~BuiltInActionsTests.Register_AddsAllBuiltInsToBothRegistries"`
Expected: FAIL — `control.throwError` is not registered yet; counts are 53/49.

- [ ] **Step 3: Register the action**

In `BuiltInActions.cs`, add this line immediately after `Add(new EndAction(), definitions, executors);` (line 19):

```csharp
        Add(new ThrowErrorAction(), definitions, executors);
```

- [ ] **Step 4: Run the full test suite to verify green**

Run: `dotnet test ADB.slnx`
Expected: PASS — registration test passes with 54/50; no other test regressed.

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Actions/BuiltIn/BuiltInActions.cs AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs
git commit -m "Register Throw Error action in the built-in set"
```

---

## Task 4: Documentation sync

**Files:**
- Modify: `CLAUDE.md` (Control Flow / error-handling section)
- Modify: `README.md` (action list, keep the goblin voice)
- Modify: `ADB.wiki/Control-Flow.md` and `ADB.wiki/Actions-Reference.md` (submodule)

- [ ] **Step 1: Update CLAUDE.md**

In the "Error handling / Error Handler node" paragraph of `CLAUDE.md`, append a sentence documenting the new action. Add after the existing Error Handler description:

```markdown
A **Throw Error** node (`control.throwError`, `ThrowErrorAction`) deliberately fails the run with a
configurable (`message`, `${var}`-interpolated) message: it has one input and no outputs, so its failure
always propagates — escaping an enclosing Loop and, from inside a nested bot, returning control to the
parent (caught by an Error Handler if one is present). Use it where `End` won't do, e.g. to bail out of a
loop inside a nested bot.
```

- [ ] **Step 2: Update README.md**

Find the action/control-flow listing in `README.md` and add "Throw Error" alongside End / Loop-Break, in the existing voice. Example line to insert next to the other control-flow actions:

```markdown
- **Throw Error** — rage-quit the current flow with a message. Blows past loops and pops you out of a nested bot back to the parent (or into your Error Handler if you wired one).
```

- [ ] **Step 3: Update the wiki (submodule)**

Edit `ADB.wiki/Control-Flow.md` — add a "Throw Error" subsection describing: TypeKey `control.throwError`, one input port, no outputs, `message` config field (default "Bot threw an error.", interpolated), and the propagation semantics (escapes loops; returns control from a nested bot; caught by an Error Handler if present). Edit `ADB.wiki/Actions-Reference.md` — add a "Throw Error" row/entry in the Control Flow section mirroring the other terminal actions (End, Loop-Break).

- [ ] **Step 4: Commit and push the wiki, then bump the pointer**

```bash
cd ADB.wiki
git add Control-Flow.md Actions-Reference.md
git commit -m "Docs: add Throw Error action"
git push
cd ..
git add ADB.wiki CLAUDE.md README.md
git commit -m "Docs: document Throw Error action (CLAUDE.md, README, wiki pointer)"
```

- [ ] **Step 5: Verify the build + tests once more**

Run: `dotnet test ADB.slnx`
Expected: PASS (full suite green).

---

## Self-Review Notes

- **Spec coverage:** item 1 (Task 1), item 2 (Tasks 2-4). Both covered.
- **Counts:** registry assertions moved 53→54 defs, 49→50 execs (one action added to both registries). Matches `BuiltInActions.Add<T>` registering into both.
- **Types:** `ThrowErrorAction.MessageKey` / `DefaultMessage` referenced consistently in tests and impl; `ConfigFieldType.String` matches `LogAction`; `ConfigValues.GetString(config, key, default)` matches `LoopControlFlowExecutor` usage.
