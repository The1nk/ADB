# Nested Bots — Engine Slice (B1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make a "Nested Bot" card runnable by the engine: a leaf action that runs another bot from the parent file's flat nested-bot library as a child `BotExecutor` (parent paused), optionally sharing variables and sharing targets by name, returning variables, with reference-cycle protection. No UI in this slice (validated by tests / hand-authored bots).

**Architecture:** The root `Bot` carries a flat `List<Bot> NestedBots` library (round-trips through the existing `BotSerializer`). `NestedBotAction` is the card definition (ports `in`/`onSuccess`/`onFailure` + three boolean config flags). `NestedBotExecutor` is a leaf `IActionExecutor` that resolves the referenced library bot and `await`s a child `BotExecutor.RunAsync` — so the parent walk is paused. Execution state (the library, the ancestry stack for cycle detection, parent target names) is threaded through `BotExecutionContext`; variable seed-in / read-back flows through `ExecutionOptions.InitialVariables` and `ExecutionResult.FinalVariables`.

**Tech Stack:** .NET 10, System.Text.Json (via `BotSerializer`), xUnit (hand-rolled fakes, no mock framework).

Reference spec: `Docs/superpowers/specs/2026-06-10-title-bar-and-nested-bots-design.md` (Feature B, sections B0/B1/B6 — note this slice defers the lazy `ITargetBinder` / own-target binding from B7 to a follow-up slice; here a nested bot uses targets shared from the parent by name).

Work in the worktree `C:\git\ADB-nested-engine` (branch `worktree-nested-bots-engine`). Build/test from the worktree root with `dotnet build ADB.slnx` / `dotnet test ADB.slnx`.

---

### Task 1: Root `Bot.NestedBots` library + serialization round-trip

**Files:**
- Modify: `AdbCore/Models/Bot.cs`
- Test: `AdbCore.Tests/Serialization/NestedBotSerializationTests.cs` (create; confirm the `Serialization` test folder exists — if not, create it)

- [ ] **Step 1: Write the failing test**

Create `AdbCore.Tests/Serialization/NestedBotSerializationTests.cs`:

```csharp
using AdbCore.Models;
using AdbCore.Serialization;
using Xunit;

namespace AdbCore.Tests.Serialization;

public class NestedBotSerializationTests
{
    [Fact]
    public void Bot_WithNestedLibrary_RoundTrips()
    {
        var nested = new Bot
        {
            Id = Guid.NewGuid(),
            Name = "GoToPlayerMenu",
            Actions = { new BotAction { Id = Guid.NewGuid(), TypeKey = "control.start" } },
        };
        var parent = new Bot
        {
            Id = Guid.NewGuid(),
            Name = "Root",
            NestedBots = { nested },
            Actions =
            {
                new BotAction
                {
                    Id = Guid.NewGuid(),
                    TypeKey = "control.nestedBot",
                    Config = { ["nestedBotId"] = nested.Id.ToString() },
                },
            },
        };

        var serializer = new BotSerializer();
        var json = serializer.Serialize(parent);
        var loaded = serializer.Deserialize(json);

        Assert.Single(loaded.NestedBots);
        Assert.Equal("GoToPlayerMenu", loaded.NestedBots[0].Name);
        Assert.Equal(nested.Id, loaded.NestedBots[0].Id);
        Assert.Single(loaded.NestedBots[0].Actions);
    }

    [Fact]
    public void Bot_WithoutNestedBots_HasEmptyLibrary()
    {
        var loaded = new BotSerializer().Deserialize(new BotSerializer().Serialize(new Bot { Id = Guid.NewGuid(), Name = "Plain" }));
        Assert.Empty(loaded.NestedBots);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotSerializationTests"`
Expected: FAIL — `Bot` has no `NestedBots` member.

- [ ] **Step 3: Add the property**

In `AdbCore/Models/Bot.cs`, after the `Connections` property add:
```csharp
    /// <summary>Reusable sub-bot definitions embedded in this (root) bot. Nested Bot action cards reference
    /// an entry by id; the library is flat — only the root bot populates this list.</summary>
    public List<Bot> NestedBots { get; set; } = new();
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotSerializationTests"`
Expected: PASS (2).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Models/Bot.cs AdbCore.Tests/Serialization/NestedBotSerializationTests.cs
git commit -m "Add Bot.NestedBots library + serialization round-trip"
```

---

### Task 2: `NestedBotAction` definition

**Files:**
- Create: `AdbCore/Actions/BuiltIn/NestedBotAction.cs`
- Test: `AdbCore.Tests/Actions/NestedBotActionTests.cs` (create; mirror the existing `Actions` test folder)

- [ ] **Step 1: Write the failing test**

Create `AdbCore.Tests/Actions/NestedBotActionTests.cs`:

```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using Xunit;

namespace AdbCore.Tests.Actions;

public class NestedBotActionTests
{
    [Fact]
    public void Metadata_MatchesContract()
    {
        var a = new NestedBotAction();
        Assert.Equal("control.nestedBot", a.TypeKey);
        Assert.Equal("Control Flow", a.Category);
        Assert.True(a.SupportsRetry);

        Assert.Single(a.InputPorts);
        Assert.Equal("in", a.InputPorts[0].Name);
        Assert.Equal(new[] { "onSuccess", "onFailure" }, a.OutputPorts.Select(p => p.Name).ToArray());

        Assert.Equal(new[] { "sendVars", "sendTargets", "receiveVars" }, a.ConfigFields.Select(f => f.Key).ToArray());
        Assert.All(a.ConfigFields, f => Assert.Equal(ConfigFieldType.Boolean, f.Type));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotActionTests"`
Expected: FAIL — type does not exist.

- [ ] **Step 3: Create the definition**

Create `AdbCore/Actions/BuiltIn/NestedBotAction.cs`:

```csharp
namespace AdbCore.Actions.BuiltIn;

/// <summary>A card that runs another bot from this file's nested-bot library, then continues. Execution is
/// performed by <c>NestedBotExecutor</c> (a leaf executor that runs the referenced bot as a child). This type
/// supplies palette/properties metadata. The <c>nestedBotId</c> reference is stored in config and set by the
/// editor UI; the three boolean flags control variable/target sharing per call site.</summary>
public sealed class NestedBotAction : IActionDefinition
{
    public const string NestedBotTypeKey = "control.nestedBot";

    public const string NestedBotIdKey = "nestedBotId";
    public const string SendVarsKey = "sendVars";
    public const string SendTargetsKey = "sendTargets";
    public const string ReceiveVarsKey = "receiveVars";

    public const string SuccessPort = "onSuccess";
    public const string FailurePort = "onFailure";

    public string TypeKey => NestedBotTypeKey;
    public string DisplayName => "Nested Bot";
    public string Category => "Control Flow";
    public string Description =>
        "Runs another bot from this file's nested-bot library, then continues. Optionally shares variables and targets.";

    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public List<PortDefinition> OutputPorts { get; } = new()
    {
        new PortDefinition { Name = SuccessPort, Label = "On Success" },
        new PortDefinition { Name = FailurePort, Label = "On Failure" },
    };
    public List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = SendVarsKey, Label = "Send Vars", Type = ConfigFieldType.Boolean, DefaultValue = false },
        new ConfigField { Key = SendTargetsKey, Label = "Send Targets", Type = ConfigFieldType.Boolean, DefaultValue = false },
        new ConfigField { Key = ReceiveVarsKey, Label = "Receive Vars", Type = ConfigFieldType.Boolean, DefaultValue = false },
    };
    public bool SupportsRetry => true;
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotActionTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Actions/BuiltIn/NestedBotAction.cs AdbCore.Tests/Actions/NestedBotActionTests.cs
git commit -m "Add NestedBotAction definition (Control Flow card)"
```

---

### Task 3: Engine plumbing — context/options/result fields + `BotExecutor` wiring

**Files:**
- Modify: `AdbCore/Execution/BotExecutionContext.cs`
- Modify: `AdbCore/Execution/ExecutionOptions.cs`
- Modify: `AdbCore/Execution/ExecutionResult.cs`
- Modify: `AdbCore/Execution/BotExecutor.cs`
- Test: `AdbCore.Tests/Execution/BotExecutorVariablePlumbingTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `AdbCore.Tests/Execution/BotExecutorVariablePlumbingTests.cs`:

```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Execution;

public class BotExecutorVariablePlumbingTests
{
    private static Bot LinearBot(out Guid setId)
    {
        var start = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.start" };
        var set = new BotAction
        {
            Id = Guid.NewGuid(),
            TypeKey = "data.setVariable",
            Config = { ["name"] = "greeting", ["value"] = "hi" },
        };
        setId = set.Id;
        var bot = new Bot { Id = Guid.NewGuid(), Name = "Lin", Actions = { start, set } };
        bot.Connections.Add(new ActionConnection { FromActionId = start.Id, FromPort = "out", ToActionId = set.Id, ToPort = "in" });
        return bot;
    }

    private static (ActionRegistry, ActionExecutorRegistry) Registries()
    {
        var defs = new ActionRegistry();
        var execs = new ActionExecutorRegistry();
        BuiltInActions.Register(defs, execs);
        return (defs, execs);
    }

    [Fact]
    public async Task FinalVariables_CapturesVariablesSetDuringRun()
    {
        var bot = LinearBot(out _);
        var (_, execs) = Registries();
        var result = await new BotExecutor(execs).RunAsync(bot, new ExecutionOptions(), null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.FinalVariables.ContainsKey("greeting"));
        Assert.Equal("hi", result.FinalVariables["greeting"]);
    }

    [Fact]
    public async Task InitialVariables_SeedTheRun()
    {
        var bot = LinearBot(out _);
        var (_, execs) = Registries();
        var options = new ExecutionOptions { InitialVariables = new Dictionary<string, object> { ["seed"] = 42 } };
        var result = await new BotExecutor(execs).RunAsync(bot, options, null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(42, result.FinalVariables["seed"]);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~BotExecutorVariablePlumbingTests"`
Expected: FAIL — `ExecutionResult.FinalVariables` / `ExecutionOptions.InitialVariables` don't exist.

- [ ] **Step 3a: Extend `BotExecutionContext`**

In `AdbCore/Execution/BotExecutionContext.cs`, add `using AdbCore.Models;` at the top, and add these properties inside the class:
```csharp
    /// <summary>The root bot's flat nested-bot library (id -> definition), threaded unchanged into child runs.</summary>
    public IReadOnlyDictionary<Guid, Bot> NestedBots { get; set; } = new Dictionary<Guid, Bot>();

    /// <summary>Ids of nested bots currently executing in this call chain, for cycle detection.</summary>
    public IReadOnlyList<Guid> NestedAncestry { get; set; } = Array.Empty<Guid>();

    /// <summary>This bot's target id -> name, so a nested run can match shared targets by name.</summary>
    public IReadOnlyDictionary<Guid, string> TargetNames { get; set; } = new Dictionary<Guid, string>();
```

- [ ] **Step 3b: Extend `ExecutionOptions`**

In `AdbCore/Execution/ExecutionOptions.cs`, add `using AdbCore.Models;` and these properties:
```csharp
    /// <summary>Variables to seed into the run before execution (used by nested runs that share vars).</summary>
    public IReadOnlyDictionary<string, object>? InitialVariables { get; set; }

    /// <summary>Ids of nested bots already running in this call chain (cycle detection). Empty at top level.</summary>
    public IReadOnlyList<Guid> NestedAncestry { get; set; } = Array.Empty<Guid>();

    /// <summary>When set, the run uses this flat library instead of building one from the bot's own NestedBots
    /// (so a child run inherits the root library unchanged).</summary>
    public IReadOnlyDictionary<Guid, Bot>? NestedBotLibrary { get; set; }
```

- [ ] **Step 3c: Extend `ExecutionResult`**

In `AdbCore/Execution/ExecutionResult.cs`, add:
```csharp
    /// <summary>Snapshot of run variables at completion (used by a parent run to receive a nested run's vars).</summary>
    public IReadOnlyDictionary<string, object> FinalVariables { get; set; } = new Dictionary<string, object>();
```

- [ ] **Step 3d: Wire `BotExecutor.RunAsync`**

In `AdbCore/Execution/BotExecutor.cs`, find this block near the top of `RunAsync`:
```csharp
        var context = new BotExecutionContext();
        foreach (var kvp in options.ResolvedTargets)
        {
            context.Targets[kvp.Key] = kvp.Value;
        }
```
Immediately after it, add:
```csharp
        context.TargetNames = bot.Targets.ToDictionary(t => t.Id, t => t.Name);
        context.NestedBots = options.NestedBotLibrary ?? bot.NestedBots.ToDictionary(b => b.Id);
        context.NestedAncestry = options.NestedAncestry;
        if (options.InitialVariables is not null)
        {
            foreach (var kv in options.InitialVariables)
            {
                context.Variables[kv.Key] = kv.Value;
            }
        }
```
Then add `FinalVariables` to BOTH `return new ExecutionResult { ... }` blocks in `RunAsync`. For the no-entry-point early return, add:
```csharp
                FinalVariables = new Dictionary<string, object>(context.Variables),
```
and for the final return (after the walk), add the same line:
```csharp
            FinalVariables = new Dictionary<string, object>(context.Variables),
```
(`System.Linq` is needed for `ToDictionary`; `BotExecutor.cs` already imports namespaces via ImplicitUsings — if the build complains, add `using System.Linq;`.)

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~BotExecutorVariablePlumbingTests"`
Expected: PASS (2).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Execution/BotExecutionContext.cs AdbCore/Execution/ExecutionOptions.cs AdbCore/Execution/ExecutionResult.cs AdbCore/Execution/BotExecutor.cs AdbCore.Tests/Execution/BotExecutorVariablePlumbingTests.cs
git commit -m "Thread nested-bot library, ancestry, target names, and var seed/snapshot through execution"
```

---

### Task 4: `NestedBotExecutor`

**Files:**
- Create: `AdbCore/Execution/NestedBotExecutor.cs`
- Test: `AdbCore.Tests/Execution/NestedBotExecutorTests.cs` (create)

- [ ] **Step 1: Write the failing tests**

Create `AdbCore.Tests/Execution/NestedBotExecutorTests.cs`:

```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Execution;

public class NestedBotExecutorTests
{
    // A trivial leaf executor used inside nested bots: optionally sets a variable, optionally fails.
    private sealed class FakeLeaf : IActionDefinition, IActionExecutor
    {
        public string TypeKey => "test.leaf";
        public string DisplayName => "Leaf";
        public string Category => "Test";
        public string Description => "";
        public List<PortDefinition> InputPorts { get; } = new() { new() { Name = "in", Label = "In" } };
        public List<PortDefinition> OutputPorts { get; } = new() { new() { Name = "out", Label = "Out" } };
        public List<ConfigField> ConfigFields { get; } = new();
        public bool SupportsRetry => false;

        public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
        {
            if (ConfigValues.GetBool(context.Action.Config, "fail")) return Task.FromResult(ActionResult.Fail("boom"));
            var setName = ConfigValues.GetString(context.Action.Config, "set");
            if (!string.IsNullOrEmpty(setName)) context.Context.Variables[setName] = "done";
            return Task.FromResult(ActionResult.Ok("out"));
        }
    }

    private static ActionExecutorRegistry Registry(out ActionRegistry defs)
    {
        defs = new ActionRegistry();
        var execs = new ActionExecutorRegistry();
        var leaf = new FakeLeaf();
        defs.Register(leaf); execs.Register(leaf);
        defs.Register(new StartAction()); execs.Register(new StartAction());
        var nested = new NestedBotAction();
        defs.Register(nested);
        execs.Register(new NestedBotExecutor(execs));
        return execs;
    }

    private static Bot NestedBot(string name, bool fail = false, string? setVar = null)
    {
        var start = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.start" };
        var leaf = new BotAction { Id = Guid.NewGuid(), TypeKey = "test.leaf" };
        if (fail) leaf.Config["fail"] = true;
        if (setVar is not null) leaf.Config["set"] = setVar;
        var bot = new Bot { Id = Guid.NewGuid(), Name = name, Actions = { start, leaf } };
        bot.Connections.Add(new ActionConnection { FromActionId = start.Id, FromPort = "out", ToActionId = leaf.Id, ToPort = "in" });
        return bot;
    }

    private static async Task<ActionResult> RunCard(ActionExecutorRegistry execs, BotExecutionContext ctx, BotAction card)
    {
        var exec = new NestedBotExecutor(execs);
        return await exec.ExecuteAsync(new ActionExecutionContext(card, ctx, _ => { }), CancellationToken.None);
    }

    [Fact]
    public async Task Unassigned_Fails()
    {
        var execs = Registry(out _);
        var card = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.nestedBot" };
        var result = await RunCard(execs, new BotExecutionContext(), card);
        Assert.False(result.Success);
        Assert.Contains("no bot assigned", result.ErrorMessage);
    }

    [Fact]
    public async Task MissingId_Fails()
    {
        var execs = Registry(out _);
        var card = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.nestedBot", Config = { ["nestedBotId"] = Guid.NewGuid().ToString() } };
        var result = await RunCard(execs, new BotExecutionContext(), card);
        Assert.False(result.Success);
        Assert.Contains("not found", result.ErrorMessage);
    }

    [Fact]
    public async Task Success_RoutesOnSuccess()
    {
        var execs = Registry(out _);
        var nested = NestedBot("Child");
        var ctx = new BotExecutionContext { NestedBots = new Dictionary<Guid, Bot> { [nested.Id] = nested } };
        var card = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.nestedBot", Config = { ["nestedBotId"] = nested.Id.ToString() } };
        var result = await RunCard(execs, ctx, card);
        Assert.True(result.Success);
        Assert.Equal("onSuccess", result.OutputPort);
    }

    [Fact]
    public async Task ChildFailure_FailsCard()
    {
        var execs = Registry(out _);
        var nested = NestedBot("Child", fail: true);
        var ctx = new BotExecutionContext { NestedBots = new Dictionary<Guid, Bot> { [nested.Id] = nested } };
        var card = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.nestedBot", Config = { ["nestedBotId"] = nested.Id.ToString() } };
        var result = await RunCard(execs, ctx, card);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task ReceiveVars_MergesChildVarsBack()
    {
        var execs = Registry(out _);
        var nested = NestedBot("Child", setVar: "flag");
        var ctx = new BotExecutionContext { NestedBots = new Dictionary<Guid, Bot> { [nested.Id] = nested } };
        var card = new BotAction
        {
            Id = Guid.NewGuid(),
            TypeKey = "control.nestedBot",
            Config = { ["nestedBotId"] = nested.Id.ToString(), ["receiveVars"] = true },
        };
        var result = await RunCard(execs, ctx, card);
        Assert.True(result.Success);
        Assert.Equal("done", ctx.Variables["flag"]);
    }

    [Fact]
    public async Task ReceiveVarsOff_DoesNotLeak()
    {
        var execs = Registry(out _);
        var nested = NestedBot("Child", setVar: "flag");
        var ctx = new BotExecutionContext { NestedBots = new Dictionary<Guid, Bot> { [nested.Id] = nested } };
        var card = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.nestedBot", Config = { ["nestedBotId"] = nested.Id.ToString() } };
        await RunCard(execs, ctx, card);
        Assert.False(ctx.Variables.ContainsKey("flag"));
    }

    [Fact]
    public async Task Cycle_IsDetected()
    {
        var execs = Registry(out _);
        var nested = NestedBot("Child");
        var ctx = new BotExecutionContext
        {
            NestedBots = new Dictionary<Guid, Bot> { [nested.Id] = nested },
            NestedAncestry = new[] { nested.Id }, // already running
        };
        var card = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.nestedBot", Config = { ["nestedBotId"] = nested.Id.ToString() } };
        var result = await RunCard(execs, ctx, card);
        Assert.False(result.Success);
        Assert.Contains("cycle", result.ErrorMessage);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotExecutorTests"`
Expected: FAIL — `NestedBotExecutor` does not exist.

- [ ] **Step 3: Create the executor**

Create `AdbCore/Execution/NestedBotExecutor.cs`:

```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Models;

namespace AdbCore.Execution;

/// <summary>Leaf executor for the Nested Bot card: resolves the referenced library bot, runs it as a child
/// <see cref="BotExecutor"/> (the parent walk awaits — so it is paused), optionally seeding the child's
/// variables, sharing the parent's resolved targets by name, and merging the child's final variables back.
/// Routes onSuccess/onFailure and guards against reference cycles.</summary>
public sealed class NestedBotExecutor : IActionExecutor
{
    private readonly ActionExecutorRegistry _executors;

    public NestedBotExecutor(ActionExecutorRegistry executors)
    {
        ArgumentNullException.ThrowIfNull(executors);
        _executors = executors;
    }

    public string TypeKey => NestedBotAction.NestedBotTypeKey;

    public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        var config = context.Action.Config;
        var idText = ConfigValues.GetString(config, NestedBotAction.NestedBotIdKey);
        if (string.IsNullOrWhiteSpace(idText) || !Guid.TryParse(idText, out var nestedId))
        {
            return ActionResult.Fail("This Nested Bot card has no bot assigned.");
        }

        var run = context.Context;
        if (!run.NestedBots.TryGetValue(nestedId, out var nestedBot) || nestedBot is null)
        {
            return ActionResult.Fail($"Nested bot '{nestedId}' was not found in this bot's library.");
        }

        if (run.NestedAncestry.Contains(nestedId))
        {
            return ActionResult.Fail(
                $"Nested bot cycle detected: '{nestedBot.Name}' is already running in this call chain.");
        }

        var sendVars = ConfigValues.GetBool(config, NestedBotAction.SendVarsKey);
        var sendTargets = ConfigValues.GetBool(config, NestedBotAction.SendTargetsKey);
        var receiveVars = ConfigValues.GetBool(config, NestedBotAction.ReceiveVarsKey);

        var childOptions = new ExecutionOptions
        {
            Log = context.Log,
            NestedBotLibrary = run.NestedBots,
            NestedAncestry = run.NestedAncestry.Append(nestedId).ToList(),
            InitialVariables = sendVars ? new Dictionary<string, object>(run.Variables) : null,
            ResolvedTargets = sendTargets
                ? OverlayParentTargetsByName(nestedBot, run)
                : new Dictionary<Guid, ResolvedTarget>(),
        };

        var result = await new BotExecutor(_executors).RunAsync(nestedBot, childOptions, progress: null, ct);

        if (receiveVars)
        {
            foreach (var kv in result.FinalVariables)
            {
                run.Variables[kv.Key] = kv.Value;
            }
        }

        return result.Success
            ? ActionResult.Ok(NestedBotAction.SuccessPort)
            : ActionResult.Fail(result.ErrorMessage ?? "Nested bot failed.");
    }

    /// <summary>Maps each nested target whose NAME matches a parent target to the parent's already-resolved
    /// handle, keyed by the NESTED target's id (nested actions reference nested target ids). Nested targets with
    /// no name match are omitted — own-target binding arrives in the lazy-binder slice.</summary>
    private static IReadOnlyDictionary<Guid, ResolvedTarget> OverlayParentTargetsByName(Bot nestedBot, BotExecutionContext run)
    {
        var parentByName = new Dictionary<string, ResolvedTarget>(StringComparer.Ordinal);
        foreach (var kv in run.TargetNames)
        {
            if (run.Targets.TryGetValue(kv.Key, out var resolved))
            {
                parentByName[kv.Value] = resolved;
            }
        }

        var map = new Dictionary<Guid, ResolvedTarget>();
        foreach (var t in nestedBot.Targets)
        {
            if (!string.IsNullOrEmpty(t.Name) && parentByName.TryGetValue(t.Name, out var resolved))
            {
                map[t.Id] = resolved;
            }
        }
        return map;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotExecutorTests"`
Expected: PASS (7).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Execution/NestedBotExecutor.cs AdbCore.Tests/Execution/NestedBotExecutorTests.cs
git commit -m "Add NestedBotExecutor: child run, var sharing, target-by-name overlay, cycle guard"
```

---

### Task 5: Register in `BuiltInActions` + end-to-end integration test

**Files:**
- Modify: `AdbCore/Actions/BuiltIn/BuiltInActions.cs`
- Test: `AdbCore.Tests/Execution/NestedBotEndToEndTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `AdbCore.Tests/Execution/NestedBotEndToEndTests.cs`:

```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Execution;

public class NestedBotEndToEndTests
{
    [Fact]
    public async Task ParentRunsNestedCard_AndReceivesVar()
    {
        // Child: Start -> SetVariable(result=ok) -> (end of path)
        var cStart = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.start" };
        var cSet = new BotAction { Id = Guid.NewGuid(), TypeKey = "data.setVariable", Config = { ["name"] = "result", ["value"] = "ok" } };
        var child = new Bot { Id = Guid.NewGuid(), Name = "Child", Actions = { cStart, cSet } };
        child.Connections.Add(new ActionConnection { FromActionId = cStart.Id, FromPort = "out", ToActionId = cSet.Id, ToPort = "in" });

        // Parent: Start -> NestedBot(receiveVars) -> (end)
        var pStart = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.start" };
        var card = new BotAction
        {
            Id = Guid.NewGuid(),
            TypeKey = "control.nestedBot",
            Config = { ["nestedBotId"] = child.Id.ToString(), ["receiveVars"] = true },
        };
        var parent = new Bot { Id = Guid.NewGuid(), Name = "Parent", NestedBots = { child }, Actions = { pStart, card } };
        parent.Connections.Add(new ActionConnection { FromActionId = pStart.Id, FromPort = "out", ToActionId = card.Id, ToPort = "in" });

        var defs = new ActionRegistry();
        var execs = new ActionExecutorRegistry();
        BuiltInActions.Register(defs, execs);

        var result = await new BotExecutor(execs).RunAsync(parent, new ExecutionOptions(), null, CancellationToken.None);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("ok", result.FinalVariables["result"]);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotEndToEndTests"`
Expected: FAIL — no executor registered for `control.nestedBot`.

- [ ] **Step 3: Register**

In `AdbCore/Actions/BuiltIn/BuiltInActions.cs`, after the Run Parallel / Join registration block (around the engine-native registrations near the end, before `return ocrEngine;`), add:
```csharp
        // Nested Bot: a leaf card that runs another bot from the library as a child executor. The executor
        // captures the executor registry so it can build child BotExecutors (including for deeper nesting).
        definitions.Register(new NestedBotAction());
        executors.Register(new NestedBotExecutor(executors));
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotEndToEndTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Actions/BuiltIn/BuiltInActions.cs AdbCore.Tests/Execution/NestedBotEndToEndTests.cs
git commit -m "Register NestedBotAction + NestedBotExecutor; end-to-end nested run test"
```

---

### Task 6: Full suite green

- [ ] **Step 1: Run the whole suite**

Run: `dotnet test ADB.slnx`
Expected: PASS, no regressions. (If the palette/dependency-probe tests assert an exact action count, update them to include the new "Nested Bot" action — search the test projects for a failing count assertion and adjust it to the new total, committing that fix.)

- [ ] **Step 2: Commit any test-count fixups** (only if needed)

```bash
git add -A
git commit -m "Update action-count assertions for the new Nested Bot action"
```

---

## Self-Review

- **Spec coverage (Feature B, this slice):** flat root library + serialization (Task 1, B0); card definition with in/onSuccess/onFailure + 3 per-card boolean flags (Task 2, B1/B6); child run with parent paused via await + var seed/merge + cycle guard (Tasks 3-4, B6); send-targets-by-name overlay (Task 4, partial B7 — own-target lazy binding is the explicit follow-up slice); registration + end-to-end (Task 5). ✓
- **Deferred (next slice, B2):** `ITargetBinder` and lazy binding of a nested bot's OWN (non-shared) targets. Documented as out of scope here.
- **Placeholders:** none — every step has concrete code/commands. ✓
- **Type consistency:** `NestedBotAction.NestedBotTypeKey`/`NestedBotIdKey`/`SendVarsKey`/`SendTargetsKey`/`ReceiveVarsKey`/`SuccessPort` referenced identically across Tasks 2/4/5; `BotExecutionContext.NestedBots`/`NestedAncestry`/`TargetNames`, `ExecutionOptions.InitialVariables`/`NestedAncestry`/`NestedBotLibrary`, `ExecutionResult.FinalVariables` consistent across Tasks 3/4. ✓
- **Note for executor:** if any existing test asserts an exact built-in action count (e.g. palette/dependency-probe tests), Task 6 covers updating it.
