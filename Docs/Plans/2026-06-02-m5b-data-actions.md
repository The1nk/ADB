# M5b — Data Actions (Set Variable, Comment) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the remaining Data leaf actions — **Set Variable** (writes a run variable) and **Comment** (a no-op documentation node) — completing the Data category (Log already exists).

**Architecture:** Both are ordinary leaf actions implementing `IActionDefinition` + `IActionExecutor` in one class (the existing `LogAction` pattern). Set Variable writes `name → value` into `BotExecutionContext.Variables`; Comment passes through (`in`→`out`) doing nothing. No engine changes. Set Variable is the piece that lets a bot populate the variables the M5a1 Branch/Loop conditions already read.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), xUnit. Project: `AdbCore` + `AdbCore.Tests`. Solution: `ADB.slnx`.

**Design reference:** `Docs/Specs/2026-06-01-m5-built-in-actions-design.md` §4.1 (Data: Set Variable, Comment), slice M5b in §8.

---

## Background the implementer needs

- **Leaf-action pattern** (`AdbCore/Actions/BuiltIn/LogAction.cs`): a `sealed class` implementing `IActionDefinition` (TypeKey, DisplayName, Category, Description, InputPorts, OutputPorts, ConfigFields, SupportsRetry) + `IActionExecutor` (`Task<ActionResult> ExecuteAsync(ActionExecutionContext, CancellationToken)`). Registered via `BuiltInActions.Add<T>` (both registries).
- **`ActionExecutionContext`** exposes `.Action` (the `BotAction`, with `.Config`), `.Context` (the run-wide `BotExecutionContext` whose `Variables` is a `ConcurrentDictionary<string, object>`), and `.Log`. `ActionResult.Ok("out")` / `ActionResult.Fail(msg)`.
- **`ConfigValues`** (`AdbCore.Actions.ConfigValues`, accessible from the `BuiltIn` sub-namespace without a using): `GetString(IReadOnlyDictionary<string,object>, key, fallback="")` etc. Use it; don't hand-read config.
- **`ConfigFieldType`** values: `String, MultilineString, Number, Boolean, Enum, FilePath, ImagePath`.
- **Variables flow to conditions:** `BranchAction` reads `Context.Variables[name]` and coerces via `ConfigValues`. Storing a Set Variable value as a plain string is sufficient — Branch's `GreaterThan`/`IsTrue`/etc. coerce on read.
- **Current registry counts:** 8 definitions / 5 executors. After M5b: **10 definitions / 7 executors** (both new actions are def+executor). The Data palette category goes from 1 item (Log) to 3.
- Strict TDD: failing test first, run red, implement, run green.

## Build / test commands (run from the worktree root)

- Single class: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Actions.BuiltIn.SetVariableActionTests"`
- Full suite: `dotnet test ADB.slnx`
- Zero-warning build (hard gate): `dotnet build ADB.slnx`

---

## File Structure

- **Create** `AdbCore/Actions/BuiltIn/SetVariableAction.cs` — `data.setVariable` leaf action.
- **Create** `AdbCore/Actions/BuiltIn/CommentAction.cs` — `data.comment` leaf action (pass-through).
- **Modify** `AdbCore/Actions/BuiltIn/BuiltInActions.cs` — register both.
- **Create** `AdbCore.Tests/Actions/BuiltIn/SetVariableActionTests.cs`
- **Create** `AdbCore.Tests/Actions/BuiltIn/CommentActionTests.cs`
- **Modify** `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs` — registry counts.
- **Modify** `BotBuilder.Core.Tests/PaletteViewModelTests.cs` — palette counts.

---

## Task 1: Set Variable action (`data.setVariable`)

**Files:**
- Create: `AdbCore/Actions/BuiltIn/SetVariableAction.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/SetVariableActionTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `AdbCore.Tests/Actions/BuiltIn/SetVariableActionTests.cs`:

```csharp
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class SetVariableActionTests
{
    private static ActionExecutionContext Ctx(BotAction action, BotExecutionContext context)
        => new(action, context, _ => { });

    [Fact]
    public async Task SetVariable_WritesNameValueIntoContext_AndContinues()
    {
        var action = new BotAction { TypeKey = "data.setVariable" };
        action.Config[SetVariableAction.NameKey] = "greeting";
        action.Config[SetVariableAction.ValueKey] = "hello";
        var context = new BotExecutionContext();

        var result = await new SetVariableAction().ExecuteAsync(Ctx(action, context), default);

        Assert.True(result.Success);
        Assert.Equal("out", result.OutputPort);
        Assert.Equal("hello", context.Variables["greeting"]);
    }

    [Fact]
    public async Task SetVariable_OverwritesExistingValue()
    {
        var action = new BotAction { TypeKey = "data.setVariable" };
        action.Config[SetVariableAction.NameKey] = "x";
        action.Config[SetVariableAction.ValueKey] = "2";
        var context = new BotExecutionContext();
        context.Variables["x"] = "1";

        await new SetVariableAction().ExecuteAsync(Ctx(action, context), default);

        Assert.Equal("2", context.Variables["x"]);
    }

    [Fact]
    public async Task SetVariable_EmptyName_IsNoOp_AndContinues()
    {
        var action = new BotAction { TypeKey = "data.setVariable" };
        action.Config[SetVariableAction.ValueKey] = "orphan";
        var context = new BotExecutionContext();

        var result = await new SetVariableAction().ExecuteAsync(Ctx(action, context), default);

        Assert.True(result.Success);
        Assert.Empty(context.Variables);
    }

    [Fact]
    public void Definition_Metadata()
    {
        var def = new SetVariableAction();

        Assert.Equal("data.setVariable", def.TypeKey);
        Assert.Equal("Data", def.Category);
        Assert.Equal(new[] { "in" }, def.InputPorts.Select(p => p.Name));
        Assert.Equal(new[] { "out" }, def.OutputPorts.Select(p => p.Name));
        Assert.Equal(new[] { SetVariableAction.NameKey, SetVariableAction.ValueKey }, def.ConfigFields.Select(f => f.Key));
        Assert.False(def.SupportsRetry);
    }

    [Fact]
    public async Task SetVariable_FeedsBranchCondition_ThroughEngine()
    {
        // Set Variable x=5 -> Branch (x GreaterThan 3) -> true: yes ; false: no
        var setVar = new BotAction { Id = Guid.NewGuid(), TypeKey = "data.setVariable" };
        setVar.Config[SetVariableAction.NameKey] = "x";
        setVar.Config[SetVariableAction.ValueKey] = "5";

        var branch = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.branch" };
        branch.Config[BranchAction.VariableKey] = "x";
        branch.Config[BranchAction.OperatorKey] = BranchAction.OpGreaterThan;
        branch.Config[BranchAction.ValueKey] = "3";

        var yes = new BotAction { Id = Guid.NewGuid(), TypeKey = "yes" };
        var no = new BotAction { Id = Guid.NewGuid(), TypeKey = "no" };

        var bot = new Bot { Name = "setvar-branch" };
        bot.Actions.AddRange(new[] { setVar, branch, yes, no });
        bot.Connections.Add(new ActionConnection { Id = Guid.NewGuid(), SourceActionId = setVar.Id, SourcePort = "out", TargetActionId = branch.Id, TargetPort = "in" });
        bot.Connections.Add(new ActionConnection { Id = Guid.NewGuid(), SourceActionId = branch.Id, SourcePort = BranchAction.TruePort, TargetActionId = yes.Id, TargetPort = "in" });
        bot.Connections.Add(new ActionConnection { Id = Guid.NewGuid(), SourceActionId = branch.Id, SourcePort = BranchAction.FalsePort, TargetActionId = no.Id, TargetPort = "in" });

        var yesRan = false;
        var noRan = false;
        var registry = new ActionExecutorRegistry();
        registry.Register(new SetVariableAction());
        registry.Register(new BranchAction());
        registry.Register(new FakeExecutor { TypeKey = "yes", Behavior = c => { yesRan = true; return ActionResult.Ok(string.Empty); } });
        registry.Register(new FakeExecutor { TypeKey = "no", Behavior = c => { noRan = true; return ActionResult.Ok(string.Empty); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);
        Assert.True(yesRan);
        Assert.False(noRan);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Actions.BuiltIn.SetVariableActionTests"`
Expected: FAIL to compile — `SetVariableAction` does not exist.

- [ ] **Step 3: Write the implementation**

Create `AdbCore/Actions/BuiltIn/SetVariableAction.cs`:

```csharp
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Writes a named run variable (as a string) into the execution context, then continues.
/// Readers (Branch, Loop) coerce the string to number/bool as needed.</summary>
public sealed class SetVariableAction : IActionDefinition, IActionExecutor
{
    public const string NameKey = "name";
    public const string ValueKey = "value";

    public string TypeKey => "data.setVariable";
    public string DisplayName => "Set Variable";
    public string Category => "Data";
    public string Description => "Sets a run variable to a value.";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public List<PortDefinition> OutputPorts { get; } = new() { new PortDefinition { Name = "out", Label = "Out" } };
    public List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = NameKey, Label = "Name", Type = ConfigFieldType.String },
        new ConfigField { Key = ValueKey, Label = "Value", Type = ConfigFieldType.String },
    };
    public bool SupportsRetry => false;

    public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        var name = ConfigValues.GetString(context.Action.Config, NameKey);
        if (!string.IsNullOrEmpty(name))
        {
            context.Context.Variables[name] = ConfigValues.GetString(context.Action.Config, ValueKey);
        }

        return Task.FromResult(ActionResult.Ok("out"));
    }
}
```

- [ ] **Step 4: Run to verify the tests pass**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Actions.BuiltIn.SetVariableActionTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Actions/BuiltIn/SetVariableAction.cs AdbCore.Tests/Actions/BuiltIn/SetVariableActionTests.cs
git commit -m "feat(actions): add Set Variable data action"
```

---

## Task 2: Comment action (`data.comment`)

**Files:**
- Create: `AdbCore/Actions/BuiltIn/CommentAction.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/CommentActionTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `AdbCore.Tests/Actions/BuiltIn/CommentActionTests.cs`:

```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class CommentActionTests
{
    private static ActionExecutionContext Ctx(BotAction action)
        => new(action, new BotExecutionContext(), _ => { });

    [Fact]
    public async Task Comment_IsNoOp_AndContinues()
    {
        var action = new BotAction { TypeKey = "data.comment" };
        action.Config[CommentAction.TextKey] = "remember to tune confidence";

        var result = await new CommentAction().ExecuteAsync(Ctx(action), default);

        Assert.True(result.Success);
        Assert.Equal("out", result.OutputPort);
    }

    [Fact]
    public async Task Comment_NoText_StillContinues()
    {
        var result = await new CommentAction().ExecuteAsync(Ctx(new BotAction { TypeKey = "data.comment" }), default);

        Assert.True(result.Success);
        Assert.Equal("out", result.OutputPort);
    }

    [Fact]
    public void Definition_Metadata()
    {
        var def = new CommentAction();

        Assert.Equal("data.comment", def.TypeKey);
        Assert.Equal("Data", def.Category);
        Assert.Equal(new[] { "in" }, def.InputPorts.Select(p => p.Name));
        Assert.Equal(new[] { "out" }, def.OutputPorts.Select(p => p.Name));
        var text = def.ConfigFields.Single(f => f.Key == CommentAction.TextKey);
        Assert.Equal(ConfigFieldType.MultilineString, text.Type);
        Assert.False(def.SupportsRetry);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Actions.BuiltIn.CommentActionTests"`
Expected: FAIL to compile — `CommentAction` does not exist.

- [ ] **Step 3: Write the implementation**

Create `AdbCore/Actions/BuiltIn/CommentAction.cs`:

```csharp
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn;

/// <summary>A documentation node. Does nothing at runtime; if wired into the flow it passes through.</summary>
public sealed class CommentAction : IActionDefinition, IActionExecutor
{
    public const string TextKey = "text";

    public string TypeKey => "data.comment";
    public string DisplayName => "Comment";
    public string Category => "Data";
    public string Description => "A note on the canvas. No effect at runtime.";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public List<PortDefinition> OutputPorts { get; } = new() { new PortDefinition { Name = "out", Label = "Out" } };
    public List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = TextKey, Label = "Text", Type = ConfigFieldType.MultilineString },
    };
    public bool SupportsRetry => false;

    public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
        => Task.FromResult(ActionResult.Ok("out"));
}
```

- [ ] **Step 4: Run to verify the tests pass**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Actions.BuiltIn.CommentActionTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Actions/BuiltIn/CommentAction.cs AdbCore.Tests/Actions/BuiltIn/CommentActionTests.cs
git commit -m "feat(actions): add Comment data action"
```

---

## Task 3: Register the actions and update counts

**Files:**
- Modify: `AdbCore/Actions/BuiltIn/BuiltInActions.cs`
- Modify: `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs`
- Modify: `BotBuilder.Core.Tests/PaletteViewModelTests.cs`
- Modify: `Docs/Specs/2026-06-01-m5-built-in-actions-design.md`

After this task: definitions = 10, executors = 7.

- [ ] **Step 1: Update the registration assertions (failing first)**

In `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs`, in `Register_AddsAllBuiltInsToBothRegistries`, change the dual-registry key list and the counts. Replace this block:

```csharp
        foreach (var key in new[] { "control.start", "control.end", "data.log", "control.delay", "control.branch" })
        {
            Assert.True(defs.TryGet(key, out _));
            Assert.True(execs.TryGet(key, out _));
        }

        // Engine-native nodes: definitions only, no executors.
        foreach (var key in new[] { "control.loop", "control.runParallel", "control.join" })
        {
            Assert.True(defs.TryGet(key, out _));
            Assert.False(execs.TryGet(key, out _));
        }

        Assert.Equal(8, defs.Count);
        Assert.Equal(5, execs.Count);
```

with:

```csharp
        foreach (var key in new[] { "control.start", "control.end", "data.log", "control.delay", "control.branch", "data.setVariable", "data.comment" })
        {
            Assert.True(defs.TryGet(key, out _));
            Assert.True(execs.TryGet(key, out _));
        }

        // Engine-native nodes: definitions only, no executors.
        foreach (var key in new[] { "control.loop", "control.runParallel", "control.join" })
        {
            Assert.True(defs.TryGet(key, out _));
            Assert.False(execs.TryGet(key, out _));
        }

        Assert.Equal(10, defs.Count);
        Assert.Equal(7, execs.Count);
```

- [ ] **Step 2: Update the palette counts**

In `BotBuilder.Core.Tests/PaletteViewModelTests.cs`:
- In `Categories_GroupBuiltInsByCategory`, change `Assert.Single(data.Items);` to:

```csharp
        Assert.Equal(3, data.Items.Count); // Log, Set Variable, Comment
```

- In `ClearingSearch_RestoresAll`, change `Assert.Equal(8, palette.Categories.SelectMany(c => c.Items).Count()); // 7 Control Flow + 1 Data` to:

```csharp
        Assert.Equal(10, palette.Categories.SelectMany(c => c.Items).Count()); // 7 Control Flow + 3 Data
```

(`Search_FiltersByDisplayName_CaseInsensitive` searches "LOG" and asserts a single `data.log` match — still correct since neither new action contains "log". `Search_MatchesByCategoryName` is unaffected.)

- [ ] **Step 3: Run those tests to verify they FAIL**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Actions.BuiltIn.BuiltInActionsTests|FullyQualifiedName~BotBuilder.Core.Tests.PaletteViewModelTests"`
Expected: FAIL (counts don't match; not registered yet).

- [ ] **Step 4: Register the two actions**

In `AdbCore/Actions/BuiltIn/BuiltInActions.cs`, in `Register`, after the existing `Add(new BranchAction(), definitions, executors);` line, add:

```csharp
        Add(new SetVariableAction(), definitions, executors);
        Add(new CommentAction(), definitions, executors);
```

- [ ] **Step 5: Run the full suite**

Run: `dotnet test ADB.slnx`
Expected: ALL green. If any other test asserts a built-in count, update it to reflect the two added Data actions (none other expected).

- [ ] **Step 6: Zero-warning build gate**

Run: `dotnet build ADB.slnx`
Expected: Build succeeded, **0 Warning(s), 0 Error(s)**.

- [ ] **Step 7: Update the spec status line**

In `Docs/Specs/2026-06-01-m5-built-in-actions-design.md`, change the status line to exactly:

```
**Status:** Approved — M5a1 + M5a2 + M5b (control-flow engine + Data actions) implemented
```

- [ ] **Step 8: Commit**

```bash
git add AdbCore/Actions/BuiltIn/BuiltInActions.cs AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs BotBuilder.Core.Tests/PaletteViewModelTests.cs Docs/Specs/2026-06-01-m5-built-in-actions-design.md
git commit -m "feat(actions): register Set Variable + Comment data actions"
```

---

## Manual Verification Checklist (for the user)

After merge, in BotBuilder:
- [ ] The **Data** palette category lists **Log, Set Variable, Comment**.
- [ ] Set Variable's Properties Panel shows Name + Value; Comment shows a multiline Text field.
- [ ] Build a bot: Start → Set Variable (`count` = `3`) → Loop (ForEach is overkill; use Branch: `count` GreaterThan `2`) → on true, Log "big"; on false, Log "small". Save and run via BotRunner; confirm "big" logs. *(This exercises Set Variable feeding a Branch condition end-to-end.)*
- [ ] Drop a Comment node (wired or floating); confirm it has no runtime effect.

---

## Self-Review (completed by plan author)

**Spec coverage (design §4.1 / slice M5b):**
- Set Variable (`data.setVariable`, name + value, writes `Context.Variables`) — Task 1. ✓
- Comment (`data.comment`, text Multiline, no-op `in`→`out` pass-through) — Task 2. ✓
- Log already exists (untouched). ✓
- Registration + palette — Task 3. ✓
- End-to-end value: Set Variable feeding a Branch condition — Task 1's `SetVariable_FeedsBranchCondition_ThroughEngine`. ✓

**Placeholder scan:** none — complete code in every step.

**Type consistency:** `SetVariableAction.{NameKey,ValueKey}`, `CommentAction.TextKey`, and the reused `BranchAction.{VariableKey,OperatorKey,ValueKey,OpGreaterThan,TruePort,FalsePort}` (from M5a1) are referenced consistently. Counts: 10 defs / 7 execs; palette 3 Data / 10 total.

**Out of scope (later slices):** Input (M5c, Win32), Screen (M5d, OpenCvSharp). Typed variable values (numbers/bools stored as native types) are deferred — values are stored as strings and coerced by readers, which is sufficient for all current consumers.
