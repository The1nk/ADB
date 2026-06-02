# M5a1 — Engine v2 + Control Flow (Branch / Loop / Delay) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rework the bot execution engine from a single-pointer sequential walker into a recursive sub-walk walker, and add the first Control Flow actions — Delay, Branch, and a Blueprints-style Loop (Body/Done ports, count + for-each).

**Architecture:** `BotExecutor` is restructured around a private recursive `WalkAsync` that walks a sub-path until it dead-ends. Loop is **engine-native** — the engine recognizes its `TypeKey` and re-walks the Body sub-path per iteration, then follows Done; no back-edges, the graph stays a DAG. Delay and Branch are ordinary leaf `IActionExecutor`s (they need no engine re-entry), reusing the existing one-class definition+executor pattern (`LogAction`). The recursive walker is the seam that M5a2 will extend additively for Run Parallel / Join. A new `ConfigValues` helper reads boxed-primitive and `JsonElement` config values (the two shapes `BotAction.Config` takes in memory vs. loaded from disk).

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), xUnit. Project: `AdbCore` + `AdbCore.Tests`. Solution: `ADB.slnx`.

**Design reference:** `Docs/Specs/2026-06-01-m5-built-in-actions-design.md` (§2 Engine v2, §3 Control Flow, §8 slice M5a1).

---

## Background the implementer needs

- **`BotAction.Config` is `Dictionary<string, object>`.** Built in memory the values are boxed primitives (`"hi"`, `0.9` as `double`, `true` as `bool`). After a `.bot` file is loaded via `BotSerializer`, the same values come back as `System.Text.Json.JsonElement`. Any code reading config must handle both. `BotBuilder.Core` already solved this for the UI (`ConfigFieldViewModel.Normalize`) but that lives in a WPF-adjacent project and is not reachable from `AdbCore`; hence the new `AdbCore` helper in Task 1.
- **Existing engine behavior (must be preserved by the Task 4 refactor):** finds one entry point (the first action with no incoming connection), runs each action's executor, follows `result.OutputPort` to the next node, applies `RetryPolicy` per action, follows a wired `onFailure` port on failure (else halts), reports `IProgress<ExecutionProgress>` once per action, and counts `ActionsExecuted`. The eight tests in `AdbCore.Tests/Execution/BotExecutorTests.cs` are the regression net — they must stay green unchanged.
- **Leaf vs. engine-native:** Start/End/Log/Delay/Branch are leaf `IActionExecutor`s registered in both registries. Loop is **definition-only** (`IActionDefinition` with no executor); the engine handles its execution. There is no test asserting "every definition has an executor", so a definition-only action is fine.
- **`ConfigFieldType` values available:** `String, MultilineString, Number, Boolean, Enum, FilePath, ImagePath`.
- **Test conventions:** xUnit `[Fact]`/`[Theory]`; namespace mirrors folder (`AdbCore.Tests.Execution`, `AdbCore.Tests.Actions`). The `FakeExecutor` test double (`AdbCore.Tests/Execution/FakeExecutor.cs`) has `required string TypeKey` and a `Func<ActionExecutionContext, ActionResult> Behavior`; its behavior delegate can read/write `c.Context.Variables`.

## Build / test commands

- Full build (gate requires **0 warnings**): `dotnet build ADB.slnx`
- Full test run: `dotnet test ADB.slnx`
- Single test class: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Actions.ConfigValuesTests"`
- Single test: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Actions.ConfigValuesTests.GetInt_FromJsonElement_Reads"`

Run all commands from the worktree root `C:\git\ADB\.claude\worktrees\m5a1-engine-controlflow`.

---

## File Structure

- **Create** `AdbCore/Actions/ConfigValues.cs` — static helper coercing config/variable values (boxed primitives + `JsonElement`) to `string`/`int`/`double`/`bool`. Reused by every M5 action.
- **Create** `AdbCore/Actions/BuiltIn/DelayAction.cs` — `control.delay` leaf action.
- **Create** `AdbCore/Actions/BuiltIn/BranchAction.cs` — `control.branch` leaf action.
- **Create** `AdbCore/Actions/BuiltIn/LoopAction.cs` — `control.loop` **definition-only** metadata (ports + config fields + key constants).
- **Modify** `AdbCore/Execution/BotExecutor.cs` — refactor to recursive `WalkAsync` (Task 4), then add engine-native Loop dispatch + `ExecuteLoopAsync` (Tasks 5–6).
- **Modify** `AdbCore/Actions/BuiltIn/BuiltInActions.cs` — register Delay, Branch (both registries) and Loop (definitions only).
- **Create** `AdbCore.Tests/Actions/ConfigValuesTests.cs`
- **Create** `AdbCore.Tests/Actions/BuiltIn/DelayActionTests.cs`
- **Create** `AdbCore.Tests/Actions/BuiltIn/BranchActionTests.cs`
- **Create** `AdbCore.Tests/Execution/LoopExecutionTests.cs`
- **Modify** `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs` — update registration assertions.
- **Modify** `BotBuilder.Core.Tests/PaletteViewModelTests.cs` — update Control-Flow / total counts (the palette is seeded from `BuiltInActions.Register`).

---

## Task 1: `ConfigValues` config-reading helper

**Files:**
- Create: `AdbCore/Actions/ConfigValues.cs`
- Test: `AdbCore.Tests/Actions/ConfigValuesTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `AdbCore.Tests/Actions/ConfigValuesTests.cs`:

```csharp
using System.Text.Json;
using AdbCore.Actions;
using Xunit;

namespace AdbCore.Tests.Actions;

public class ConfigValuesTests
{
    private static Dictionary<string, object> Config(string key, object value)
        => new() { [key] = value };

    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement;

    [Fact]
    public void GetString_BoxedString_Reads()
        => Assert.Equal("hi", ConfigValues.GetString(Config("k", "hi"), "k"));

    [Fact]
    public void GetString_FromJsonElement_Reads()
        => Assert.Equal("hi", ConfigValues.GetString(Config("k", Json("\"hi\"")), "k"));

    [Fact]
    public void GetString_Missing_ReturnsFallback()
        => Assert.Equal("fb", ConfigValues.GetString(new Dictionary<string, object>(), "k", "fb"));

    [Fact]
    public void GetInt_BoxedDouble_Truncates()
        => Assert.Equal(3, ConfigValues.GetInt(Config("k", 3.0), "k"));

    [Fact]
    public void GetInt_FromJsonElement_Reads()
        => Assert.Equal(5, ConfigValues.GetInt(Config("k", Json("5")), "k"));

    [Fact]
    public void GetInt_FromNumericString_Reads()
        => Assert.Equal(7, ConfigValues.GetInt(Config("k", "7"), "k"));

    [Fact]
    public void GetInt_Missing_ReturnsFallback()
        => Assert.Equal(2, ConfigValues.GetInt(new Dictionary<string, object>(), "k", 2));

    [Fact]
    public void GetDouble_FromJsonElement_Reads()
        => Assert.Equal(0.75, ConfigValues.GetDouble(Config("k", Json("0.75")), "k"));

    [Fact]
    public void GetBool_BoxedTrue_Reads()
        => Assert.True(ConfigValues.GetBool(Config("k", true), "k"));

    [Fact]
    public void GetBool_FromJsonElementTrue_Reads()
        => Assert.True(ConfigValues.GetBool(Config("k", Json("true")), "k"));

    [Fact]
    public void GetBool_FromString_Reads()
        => Assert.True(ConfigValues.GetBool(Config("k", "true"), "k"));

    [Fact]
    public void GetBool_FromNonZeroNumber_IsTrue()
        => Assert.True(ConfigValues.GetBool(Config("k", 1.0), "k"));

    [Fact]
    public void AsString_Null_ReturnsEmpty()
        => Assert.Equal(string.Empty, ConfigValues.AsString(null));

    [Fact]
    public void TryAsDouble_NonNumericString_ReturnsFalse()
        => Assert.False(ConfigValues.TryAsDouble("abc", out _));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Actions.ConfigValuesTests"`
Expected: FAIL to compile — `ConfigValues` does not exist.

- [ ] **Step 3: Write the implementation**

Create `AdbCore/Actions/ConfigValues.cs`:

```csharp
using System.Globalization;
using System.Text.Json;

namespace AdbCore.Actions;

/// <summary>Reads values out of an action's <c>Config</c> (or run variables), coercing the boxed
/// primitives stored in memory and the <see cref="JsonElement"/> values produced when a `.bot`
/// is loaded from disk.</summary>
public static class ConfigValues
{
    public static string GetString(IReadOnlyDictionary<string, object> config, string key, string fallback = "")
        => config.TryGetValue(key, out var raw) ? AsString(raw) : fallback;

    public static int GetInt(IReadOnlyDictionary<string, object> config, string key, int fallback = 0)
        => config.TryGetValue(key, out var raw) && TryAsDouble(raw, out var d) ? (int)d : fallback;

    public static double GetDouble(IReadOnlyDictionary<string, object> config, string key, double fallback = 0)
        => config.TryGetValue(key, out var raw) && TryAsDouble(raw, out var d) ? d : fallback;

    public static bool GetBool(IReadOnlyDictionary<string, object> config, string key, bool fallback = false)
        => config.TryGetValue(key, out var raw) ? AsBool(raw) : fallback;

    /// <summary>Coerces any config/variable value to its string form.</summary>
    public static string AsString(object? raw) => raw switch
    {
        null => string.Empty,
        string s => s,
        JsonElement je => je.ValueKind == JsonValueKind.String ? je.GetString() ?? string.Empty : je.ToString(),
        _ => raw.ToString() ?? string.Empty,
    };

    /// <summary>Attempts to read a numeric value, handling boxed numbers, JSON numbers, and numeric strings.</summary>
    public static bool TryAsDouble(object? raw, out double value)
    {
        switch (raw)
        {
            case double d: value = d; return true;
            case int i: value = i; return true;
            case long l: value = l; return true;
            case float f: value = f; return true;
            case decimal m: value = (double)m; return true;
            case JsonElement je when je.ValueKind == JsonValueKind.Number: value = je.GetDouble(); return true;
            case JsonElement je when je.ValueKind == JsonValueKind.String:
                return double.TryParse(je.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
            case string s:
                return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
            default: value = 0; return false;
        }
    }

    /// <summary>Coerces to bool: real bools, JSON true/false, "true"/"false" strings, or non-zero numbers.</summary>
    public static bool AsBool(object? raw)
    {
        switch (raw)
        {
            case bool b: return b;
            case JsonElement je when je.ValueKind == JsonValueKind.True: return true;
            case JsonElement je when je.ValueKind == JsonValueKind.False: return false;
        }

        if (bool.TryParse(AsString(raw), out var parsed))
        {
            return parsed;
        }

        return TryAsDouble(raw, out var number) && number != 0;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Actions.ConfigValuesTests"`
Expected: PASS (14 tests).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Actions/ConfigValues.cs AdbCore.Tests/Actions/ConfigValuesTests.cs
git commit -m "feat(core): add ConfigValues helper for reading action config"
```

---

## Task 2: Delay action (`control.delay`)

**Files:**
- Create: `AdbCore/Actions/BuiltIn/DelayAction.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/DelayActionTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `AdbCore.Tests/Actions/BuiltIn/DelayActionTests.cs`:

```csharp
using System.Text.Json;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class DelayActionTests
{
    private static ActionExecutionContext Ctx(BotAction action)
        => new(action, new BotExecutionContext(), _ => { });

    [Fact]
    public async Task NoDuration_ReturnsOutImmediately()
    {
        var result = await new DelayAction().ExecuteAsync(Ctx(new BotAction()), default);

        Assert.True(result.Success);
        Assert.Equal("out", result.OutputPort);
    }

    [Fact]
    public async Task ReadsDurationFromJsonElement_AndReturnsOut()
    {
        var action = new BotAction();
        action.Config[DelayAction.DurationMsKey] = JsonDocument.Parse("1").RootElement;

        var result = await new DelayAction().ExecuteAsync(Ctx(action), default);

        Assert.True(result.Success);
        Assert.Equal("out", result.OutputPort);
    }

    [Fact]
    public async Task PositiveDuration_CancelledToken_Throws()
    {
        var action = new BotAction();
        action.Config[DelayAction.DurationMsKey] = 60000;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new DelayAction().ExecuteAsync(Ctx(action), cts.Token));
    }

    [Fact]
    public void Definition_HasInOutPorts_AndNoRetry()
    {
        var def = new DelayAction();

        Assert.Equal("control.delay", def.TypeKey);
        Assert.Equal("Control Flow", def.Category);
        Assert.Equal(new[] { "in" }, def.InputPorts.Select(p => p.Name));
        Assert.Equal(new[] { "out" }, def.OutputPorts.Select(p => p.Name));
        Assert.False(def.SupportsRetry);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Actions.BuiltIn.DelayActionTests"`
Expected: FAIL to compile — `DelayAction` does not exist.

- [ ] **Step 3: Write the implementation**

Create `AdbCore/Actions/BuiltIn/DelayAction.cs`:

```csharp
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Waits a configured number of milliseconds, then continues. Cancellation-aware.</summary>
public sealed class DelayAction : IActionDefinition, IActionExecutor
{
    public const string DurationMsKey = "durationMs";

    public string TypeKey => "control.delay";
    public string DisplayName => "Delay";
    public string Category => "Control Flow";
    public string Description => "Waits for a fixed duration before continuing.";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public List<PortDefinition> OutputPorts { get; } = new() { new PortDefinition { Name = "out", Label = "Out" } };
    public List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = DurationMsKey, Label = "Duration (ms)", Type = ConfigFieldType.Number, DefaultValue = 0 },
    };
    public bool SupportsRetry => false;

    public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        var durationMs = ConfigValues.GetInt(context.Action.Config, DurationMsKey, 0);
        if (durationMs > 0)
        {
            await Task.Delay(durationMs, ct);
        }

        return ActionResult.Ok("out");
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Actions.BuiltIn.DelayActionTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Actions/BuiltIn/DelayAction.cs AdbCore.Tests/Actions/BuiltIn/DelayActionTests.cs
git commit -m "feat(actions): add Delay control-flow action"
```

---

## Task 3: Branch action (`control.branch`)

**Files:**
- Create: `AdbCore/Actions/BuiltIn/BranchAction.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/BranchActionTests.cs`

Condition semantics (evaluated against the named variable's value in `Context.Variables`; a missing variable reads as `null` → empty string / 0 / false):
- `Equals` / `NotEquals` — ordinal string comparison of the variable's string form against the configured operand.
- `GreaterThan` / `LessThan` — numeric comparison; **false** if either side is non-numeric.
- `IsTrue` / `IsFalse` — boolean coercion of the variable (operand ignored).
- `IsEmpty` / `IsNotEmpty` — null-or-whitespace test of the variable's string form (operand ignored).
- Unknown operator → `false` (follows `false` port).

- [ ] **Step 1: Write the failing tests**

Create `AdbCore.Tests/Actions/BuiltIn/BranchActionTests.cs`:

```csharp
using System.Text.Json;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class BranchActionTests
{
    private static async Task<string> RunAsync(string op, string operand, string variable = "v", object? variableValue = null)
    {
        var action = new BotAction();
        action.Config[BranchAction.VariableKey] = variable;
        action.Config[BranchAction.OperatorKey] = op;
        action.Config[BranchAction.ValueKey] = operand;

        var context = new BotExecutionContext();
        if (variableValue is not null)
        {
            context.Variables[variable] = variableValue;
        }

        var result = await new BranchAction().ExecuteAsync(new ActionExecutionContext(action, context, _ => { }), default);
        Assert.True(result.Success);
        return result.OutputPort;
    }

    [Fact]
    public async Task Equals_MatchingString_FollowsTrue()
        => Assert.Equal("true", await RunAsync(BranchAction.OpEquals, "yes", variableValue: "yes"));

    [Fact]
    public async Task Equals_NonMatching_FollowsFalse()
        => Assert.Equal("false", await RunAsync(BranchAction.OpEquals, "yes", variableValue: "no"));

    [Fact]
    public async Task NotEquals_NonMatching_FollowsTrue()
        => Assert.Equal("true", await RunAsync(BranchAction.OpNotEquals, "yes", variableValue: "no"));

    [Fact]
    public async Task GreaterThan_BoxedNumber_FollowsTrue()
        => Assert.Equal("true", await RunAsync(BranchAction.OpGreaterThan, "3", variableValue: 5.0));

    [Fact]
    public async Task GreaterThan_FromJsonElement_FollowsFalse()
        => Assert.Equal("false", await RunAsync(BranchAction.OpGreaterThan, "9", variableValue: JsonDocument.Parse("5").RootElement));

    [Fact]
    public async Task LessThan_FollowsTrue()
        => Assert.Equal("true", await RunAsync(BranchAction.OpLessThan, "10", variableValue: 4.0));

    [Fact]
    public async Task GreaterThan_NonNumericVariable_FollowsFalse()
        => Assert.Equal("false", await RunAsync(BranchAction.OpGreaterThan, "3", variableValue: "abc"));

    [Fact]
    public async Task IsTrue_BoolVariable_FollowsTrue()
        => Assert.Equal("true", await RunAsync(BranchAction.OpIsTrue, "", variableValue: true));

    [Fact]
    public async Task IsFalse_BoolVariable_FollowsTrue()
        => Assert.Equal("true", await RunAsync(BranchAction.OpIsFalse, "", variableValue: false));

    [Fact]
    public async Task IsEmpty_MissingVariable_FollowsTrue()
        => Assert.Equal("true", await RunAsync(BranchAction.OpIsEmpty, ""));

    [Fact]
    public async Task IsNotEmpty_PresentVariable_FollowsTrue()
        => Assert.Equal("true", await RunAsync(BranchAction.OpIsNotEmpty, "", variableValue: "x"));

    [Fact]
    public void Definition_HasTrueFalsePorts()
    {
        var def = new BranchAction();

        Assert.Equal("control.branch", def.TypeKey);
        Assert.Equal(new[] { "true", "false" }, def.OutputPorts.Select(p => p.Name));
        Assert.False(def.SupportsRetry);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Actions.BuiltIn.BranchActionTests"`
Expected: FAIL to compile — `BranchAction` does not exist.

- [ ] **Step 3: Write the implementation**

Create `AdbCore/Actions/BuiltIn/BranchAction.cs`:

```csharp
using System.Globalization;
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Evaluates a simple condition over a run variable and follows the "true" or "false" port.</summary>
public sealed class BranchAction : IActionDefinition, IActionExecutor
{
    public const string VariableKey = "variable";
    public const string OperatorKey = "operator";
    public const string ValueKey = "value";

    public const string TruePort = "true";
    public const string FalsePort = "false";

    public const string OpEquals = "Equals";
    public const string OpNotEquals = "NotEquals";
    public const string OpGreaterThan = "GreaterThan";
    public const string OpLessThan = "LessThan";
    public const string OpIsTrue = "IsTrue";
    public const string OpIsFalse = "IsFalse";
    public const string OpIsEmpty = "IsEmpty";
    public const string OpIsNotEmpty = "IsNotEmpty";

    public string TypeKey => "control.branch";
    public string DisplayName => "Branch";
    public string Category => "Control Flow";
    public string Description => "Follows the True or False path based on a condition over a variable.";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public List<PortDefinition> OutputPorts { get; } = new()
    {
        new PortDefinition { Name = TruePort, Label = "True" },
        new PortDefinition { Name = FalsePort, Label = "False" },
    };
    public List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = VariableKey, Label = "Variable", Type = ConfigFieldType.String },
        new ConfigField
        {
            Key = OperatorKey,
            Label = "Operator",
            Type = ConfigFieldType.Enum,
            DefaultValue = OpEquals,
            Options = new() { OpEquals, OpNotEquals, OpGreaterThan, OpLessThan, OpIsTrue, OpIsFalse, OpIsEmpty, OpIsNotEmpty },
        },
        new ConfigField { Key = ValueKey, Label = "Value", Type = ConfigFieldType.String },
    };
    public bool SupportsRetry => false;

    public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        var variable = ConfigValues.GetString(context.Action.Config, VariableKey);
        var op = ConfigValues.GetString(context.Action.Config, OperatorKey, OpEquals);
        var operand = ConfigValues.GetString(context.Action.Config, ValueKey);

        var variableValue = context.Context.Variables.TryGetValue(variable, out var v) ? v : null;
        var matched = Evaluate(variableValue, op, operand);

        return Task.FromResult(ActionResult.Ok(matched ? TruePort : FalsePort));
    }

    private static bool Evaluate(object? variableValue, string op, string operand) => op switch
    {
        OpEquals => string.Equals(ConfigValues.AsString(variableValue), operand, StringComparison.Ordinal),
        OpNotEquals => !string.Equals(ConfigValues.AsString(variableValue), operand, StringComparison.Ordinal),
        OpGreaterThan => CompareNumbers(variableValue, operand, out var c) && c > 0,
        OpLessThan => CompareNumbers(variableValue, operand, out var c) && c < 0,
        OpIsTrue => ConfigValues.AsBool(variableValue),
        OpIsFalse => !ConfigValues.AsBool(variableValue),
        OpIsEmpty => string.IsNullOrWhiteSpace(ConfigValues.AsString(variableValue)),
        OpIsNotEmpty => !string.IsNullOrWhiteSpace(ConfigValues.AsString(variableValue)),
        _ => false,
    };

    private static bool CompareNumbers(object? variableValue, string operand, out int comparison)
    {
        comparison = 0;
        if (ConfigValues.TryAsDouble(variableValue, out var a)
            && double.TryParse(operand, NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
        {
            comparison = a.CompareTo(b);
            return true;
        }

        return false;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Actions.BuiltIn.BranchActionTests"`
Expected: PASS (12 tests).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Actions/BuiltIn/BranchAction.cs AdbCore.Tests/Actions/BuiltIn/BranchActionTests.cs
git commit -m "feat(actions): add Branch control-flow action"
```

---

## Task 4: Refactor `BotExecutor` into a recursive sub-walk (behavior-preserving)

This task changes **no observable behavior**. The eight existing tests in `AdbCore.Tests/Execution/BotExecutorTests.cs` are the specification; they must stay green with **no edits**. The point is to introduce the recursive `WalkAsync` seam that Loop (Task 5) and, later, Run Parallel / Join (M5a2) build on.

**Files:**
- Modify: `AdbCore/Execution/BotExecutor.cs` (full rewrite below)

- [ ] **Step 1: Confirm the regression net is green before touching anything**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Execution.BotExecutorTests"`
Expected: PASS (8 tests).

- [ ] **Step 2: Replace `BotExecutor.cs` with the recursive structure**

Overwrite `AdbCore/Execution/BotExecutor.cs` with:

```csharp
using AdbCore.Models;

namespace AdbCore.Execution;

/// <summary>Walks a bot's action graph from its entry point, executing each leaf action and following
/// the output port its executor returns. The walk is recursive (<see cref="WalkAsync"/>) so engine-native
/// control-flow nodes can drive sub-walks. Halts on failure unless an <c>onFailure</c> port is wired.</summary>
public class BotExecutor
{
    private const string FailurePort = "onFailure";

    private readonly ActionExecutorRegistry _executors;

    public BotExecutor(ActionExecutorRegistry executors)
    {
        ArgumentNullException.ThrowIfNull(executors);
        _executors = executors;
    }

    public async Task<ExecutionResult> RunAsync(
        Bot bot,
        ExecutionOptions options,
        IProgress<ExecutionProgress>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(bot);
        ArgumentNullException.ThrowIfNull(options);

        var context = new BotExecutionContext();
        foreach (var kvp in options.ResolvedTargets)
        {
            context.Targets[kvp.Key] = kvp.Value;
        }

        var entry = FindEntryPoint(bot);
        if (entry is null)
        {
            return new ExecutionResult
            {
                Success = false,
                ErrorMessage = "No entry point: every action has an incoming connection.",
            };
        }

        var state = new RunState(bot, _executors, context, options.Log ?? (_ => { }), progress);
        var outcome = await WalkAsync(state, entry, ct);

        return new ExecutionResult
        {
            Success = outcome.Success,
            ErrorMessage = outcome.ErrorMessage,
            FailedActionId = outcome.FailedActionId,
            ActionsExecuted = state.ActionsExecuted,
        };
    }

    /// <summary>Walks forward from <paramref name="start"/>, following output ports until the path
    /// dead-ends (no matching connection). Returns the first unhandled failure, or completion.</summary>
    private async Task<WalkOutcome> WalkAsync(RunState state, BotAction? start, CancellationToken ct)
    {
        var current = start;
        while (current is not null)
        {
            ct.ThrowIfCancellationRequested();

            if (!state.Executors.TryGet(current.TypeKey, out var executor) || executor is null)
            {
                return WalkOutcome.Failed($"No executor registered for TypeKey '{current.TypeKey}'.", current.Id);
            }

            var result = await ExecuteWithRetryAsync(executor, current, state, ct);
            state.ActionsExecuted++;

            state.Progress?.Report(new ExecutionProgress
            {
                ActionId = current.Id,
                ActionLabel = current.Label,
                TypeKey = current.TypeKey,
                Success = result.Success,
                ErrorMessage = result.ErrorMessage,
            });

            if (!result.Success)
            {
                var failureNext = FindNext(state.Bot, current.Id, FailurePort);
                if (failureNext is not null)
                {
                    current = failureNext;
                    continue;
                }

                return WalkOutcome.Failed(result.ErrorMessage, current.Id);
            }

            current = FindNext(state.Bot, current.Id, result.OutputPort);
        }

        return WalkOutcome.Completed();
    }

    private async Task<ActionResult> ExecuteWithRetryAsync(
        IActionExecutor executor,
        BotAction action,
        RunState state,
        CancellationToken ct)
    {
        var attempts = action.Retry?.MaxAttempts ?? 1;
        if (attempts < 1)
        {
            attempts = 1;
        }

        var delayMs = action.Retry?.DelayMs ?? 0;
        var result = ActionResult.Fail("Action did not execute.");

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (attempt > 0 && delayMs > 0)
            {
                await Task.Delay(delayMs, ct);
            }

            try
            {
                var actionContext = new ActionExecutionContext(action, state.Context, state.Log);
                result = await executor.ExecuteAsync(actionContext, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result = ActionResult.Fail(ex.Message);
            }

            if (result.Success)
            {
                return result;
            }
        }

        return result;
    }

    private static BotAction? FindEntryPoint(Bot bot)
    {
        var withIncoming = bot.Connections.Select(c => c.TargetActionId).ToHashSet();
        return bot.Actions.FirstOrDefault(a => !withIncoming.Contains(a.Id));
    }

    private static BotAction? FindNext(Bot bot, Guid fromActionId, string sourcePort)
    {
        var edge = bot.Connections.FirstOrDefault(
            c => c.SourceActionId == fromActionId && c.SourcePort == sourcePort);
        return edge is null ? null : bot.Actions.FirstOrDefault(a => a.Id == edge.TargetActionId);
    }

    /// <summary>Mutable per-run state threaded through the recursive walk.</summary>
    private sealed class RunState
    {
        public RunState(
            Bot bot,
            ActionExecutorRegistry executors,
            BotExecutionContext context,
            Action<string> log,
            IProgress<ExecutionProgress>? progress)
        {
            Bot = bot;
            Executors = executors;
            Context = context;
            Log = log;
            Progress = progress;
        }

        public Bot Bot { get; }
        public ActionExecutorRegistry Executors { get; }
        public BotExecutionContext Context { get; }
        public Action<string> Log { get; }
        public IProgress<ExecutionProgress>? Progress { get; }
        public int ActionsExecuted { get; set; }
    }

    /// <summary>Result of walking a sub-path: completed, or failed at a specific action.</summary>
    private sealed class WalkOutcome
    {
        public bool Success { get; private init; }
        public string? ErrorMessage { get; private init; }
        public Guid? FailedActionId { get; private init; }

        public static WalkOutcome Completed() => new() { Success = true };

        public static WalkOutcome Failed(string? errorMessage, Guid failedActionId)
            => new() { Success = false, ErrorMessage = errorMessage, FailedActionId = failedActionId };
    }
}
```

- [ ] **Step 3: Run the existing engine tests to verify they still pass unchanged**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Execution.BotExecutorTests"`
Expected: PASS (8 tests), no test edits made.

- [ ] **Step 4: Run the full suite to confirm nothing else regressed**

Run: `dotnet test ADB.slnx`
Expected: PASS (all previously-passing tests + the Task 1–3 tests).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Execution/BotExecutor.cs
git commit -m "refactor(core): restructure BotExecutor around recursive WalkAsync"
```

---

## Task 5: Loop — definition + engine-native Count mode

Loop is engine-native. `LoopAction` supplies only palette/panel metadata (ports + config fields + key constants); the engine recognizes `control.loop` and re-walks the Body sub-path per iteration, then follows Done.

**Files:**
- Create: `AdbCore/Actions/BuiltIn/LoopAction.cs`
- Modify: `AdbCore/Execution/BotExecutor.cs` (add Loop dispatch + `ExecuteLoopAsync`)
- Test: `AdbCore.Tests/Execution/LoopExecutionTests.cs`

- [ ] **Step 1: Write the `LoopAction` definition (no test yet — it is exercised via the engine tests below)**

Create `AdbCore/Actions/BuiltIn/LoopAction.cs`:

```csharp
namespace AdbCore.Actions.BuiltIn;

/// <summary>Repeats its Body sub-path N times (count) or once per item (for-each), then follows Done.
/// Execution is engine-native (see <c>BotExecutor.ExecuteLoopAsync</c>); this type supplies palette
/// and properties-panel metadata only and has no executor.</summary>
public sealed class LoopAction : IActionDefinition
{
    public const string LoopTypeKey = "control.loop";
    public const string BodyPort = "body";
    public const string DonePort = "done";

    public const string ModeKey = "mode";
    public const string CountKey = "count";
    public const string CollectionVariableKey = "collectionVariable";
    public const string IndexVariableKey = "indexVariable";
    public const string ItemVariableKey = "itemVariable";

    public const string ModeCount = "Count";
    public const string ModeForEach = "ForEach";

    public string TypeKey => LoopTypeKey;
    public string DisplayName => "Loop";
    public string Category => "Control Flow";
    public string Description => "Repeats the Body path by count or for each item, then follows Done.";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public List<PortDefinition> OutputPorts { get; } = new()
    {
        new PortDefinition { Name = BodyPort, Label = "Body" },
        new PortDefinition { Name = DonePort, Label = "Done" },
    };
    public List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField
        {
            Key = ModeKey, Label = "Mode", Type = ConfigFieldType.Enum,
            DefaultValue = ModeCount, Options = new() { ModeCount, ModeForEach },
        },
        new ConfigField { Key = CountKey, Label = "Count", Type = ConfigFieldType.Number, DefaultValue = 1 },
        new ConfigField { Key = CollectionVariableKey, Label = "Collection Variable", Type = ConfigFieldType.String },
        new ConfigField { Key = IndexVariableKey, Label = "Index Variable", Type = ConfigFieldType.String },
        new ConfigField { Key = ItemVariableKey, Label = "Item Variable", Type = ConfigFieldType.String },
    };
    public bool SupportsRetry => false;
}
```

- [ ] **Step 2: Write the failing engine tests (Count mode)**

Create `AdbCore.Tests/Execution/LoopExecutionTests.cs`:

```csharp
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Execution;

public class LoopExecutionTests
{
    private static BotAction Node(string typeKey, out Guid id)
    {
        id = Guid.NewGuid();
        return new BotAction { Id = id, TypeKey = typeKey, Label = typeKey };
    }

    private static ActionConnection Edge(Guid from, string port, Guid to)
        => new() { Id = Guid.NewGuid(), SourceActionId = from, SourcePort = port, TargetActionId = to, TargetPort = "in" };

    [Fact]
    public async Task Loop_Count_RunsBodyNTimesThenFollowsDone()
    {
        var loop = Node(LoopAction.LoopTypeKey, out var loopId);
        loop.Config[LoopAction.ModeKey] = LoopAction.ModeCount;
        loop.Config[LoopAction.CountKey] = 3;
        var body = Node("body", out var bodyId);
        var done = Node("done", out var doneId);

        var bot = new Bot { Name = "loop-count" };
        bot.Actions.AddRange(new[] { loop, body, done });
        bot.Connections.Add(Edge(loopId, LoopAction.BodyPort, bodyId));
        bot.Connections.Add(Edge(loopId, LoopAction.DonePort, doneId));

        var bodyCalls = 0;
        var doneReached = false;
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "body", Behavior = c => { bodyCalls++; return ActionResult.Ok(string.Empty); } });
        registry.Register(new FakeExecutor { TypeKey = "done", Behavior = c => { doneReached = true; return ActionResult.Ok(string.Empty); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);
        Assert.Equal(3, bodyCalls);
        Assert.True(doneReached);
        Assert.Equal(4, result.ActionsExecuted); // 3 body + 1 done; the loop node itself is not counted
    }

    [Fact]
    public async Task Loop_CountZero_SkipsBodyButFollowsDone()
    {
        var loop = Node(LoopAction.LoopTypeKey, out var loopId);
        loop.Config[LoopAction.ModeKey] = LoopAction.ModeCount;
        loop.Config[LoopAction.CountKey] = 0;
        var body = Node("body", out var bodyId);
        var done = Node("done", out var doneId);

        var bot = new Bot { Name = "loop-zero" };
        bot.Actions.AddRange(new[] { loop, body, done });
        bot.Connections.Add(Edge(loopId, LoopAction.BodyPort, bodyId));
        bot.Connections.Add(Edge(loopId, LoopAction.DonePort, doneId));

        var bodyCalls = 0;
        var doneReached = false;
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "body", Behavior = c => { bodyCalls++; return ActionResult.Ok(string.Empty); } });
        registry.Register(new FakeExecutor { TypeKey = "done", Behavior = c => { doneReached = true; return ActionResult.Ok(string.Empty); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);
        Assert.Equal(0, bodyCalls);
        Assert.True(doneReached);
    }

    [Fact]
    public async Task Loop_SetsIndexVariableEachIteration()
    {
        var loop = Node(LoopAction.LoopTypeKey, out var loopId);
        loop.Config[LoopAction.ModeKey] = LoopAction.ModeCount;
        loop.Config[LoopAction.CountKey] = 3;
        loop.Config[LoopAction.IndexVariableKey] = "i";
        var body = Node("body", out var bodyId);

        var bot = new Bot { Name = "loop-index" };
        bot.Actions.AddRange(new[] { loop, body });
        bot.Connections.Add(Edge(loopId, LoopAction.BodyPort, bodyId));

        var seen = new List<int>();
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor
        {
            TypeKey = "body",
            Behavior = c => { seen.Add(ConfigValues.GetIntVar(c.Context.Variables, "i")); return ActionResult.Ok(string.Empty); },
        });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);
        Assert.Equal(new[] { 0, 1, 2 }, seen);
    }

    [Fact]
    public async Task Loop_EmptyBody_StillCompletes()
    {
        var loop = Node(LoopAction.LoopTypeKey, out var loopId);
        loop.Config[LoopAction.ModeKey] = LoopAction.ModeCount;
        loop.Config[LoopAction.CountKey] = 2;
        var done = Node("done", out var doneId);

        var bot = new Bot { Name = "loop-empty-body" };
        bot.Actions.AddRange(new[] { loop, done });
        bot.Connections.Add(Edge(loopId, LoopAction.DonePort, doneId)); // no body edge

        var doneReached = false;
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "done", Behavior = c => { doneReached = true; return ActionResult.Ok(string.Empty); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);
        Assert.True(doneReached);
    }

    [Fact]
    public async Task Loop_BodyFailure_HaltsRun()
    {
        var loop = Node(LoopAction.LoopTypeKey, out var loopId);
        loop.Config[LoopAction.ModeKey] = LoopAction.ModeCount;
        loop.Config[LoopAction.CountKey] = 3;
        var body = Node("body", out var bodyId);
        var done = Node("done", out var doneId);

        var bot = new Bot { Name = "loop-fail" };
        bot.Actions.AddRange(new[] { loop, body, done });
        bot.Connections.Add(Edge(loopId, LoopAction.BodyPort, bodyId));
        bot.Connections.Add(Edge(loopId, LoopAction.DonePort, doneId));

        var bodyCalls = 0;
        var doneReached = false;
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "body", Behavior = c => { bodyCalls++; return ActionResult.Fail("boom"); } });
        registry.Register(new FakeExecutor { TypeKey = "done", Behavior = c => { doneReached = true; return ActionResult.Ok(string.Empty); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.False(result.Success);
        Assert.Equal("boom", result.ErrorMessage);
        Assert.Equal(bodyId, result.FailedActionId);
        Assert.Equal(1, bodyCalls);          // halts on the first iteration's failure
        Assert.False(doneReached);
    }

    [Fact]
    public async Task Loop_Nested_RunsInnerBodyForEachOuterIteration()
    {
        var outer = Node(LoopAction.LoopTypeKey, out var outerId);
        outer.Config[LoopAction.ModeKey] = LoopAction.ModeCount;
        outer.Config[LoopAction.CountKey] = 2;
        var inner = Node(LoopAction.LoopTypeKey, out var innerId);
        inner.Config[LoopAction.ModeKey] = LoopAction.ModeCount;
        inner.Config[LoopAction.CountKey] = 3;
        var innerBody = Node("innerBody", out var innerBodyId);

        var bot = new Bot { Name = "loop-nested" };
        bot.Actions.AddRange(new[] { outer, inner, innerBody });
        bot.Connections.Add(Edge(outerId, LoopAction.BodyPort, innerId));   // outer body -> inner loop
        bot.Connections.Add(Edge(innerId, LoopAction.BodyPort, innerBodyId)); // inner body -> leaf

        var innerCalls = 0;
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "innerBody", Behavior = c => { innerCalls++; return ActionResult.Ok(string.Empty); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);
        Assert.Equal(6, innerCalls); // 2 outer * 3 inner
    }
}
```

> Note: `ConfigValues.GetIntVar` is a small convenience added in Step 3 below for reading an `int` out of the variables dictionary in tests and (later) actions. It is implemented as part of this task.

- [ ] **Step 3: Add the variable convenience reader to `ConfigValues`**

Add this method to `AdbCore/Actions/ConfigValues.cs` (inside the `ConfigValues` class, after `GetBool`):

```csharp
    /// <summary>Reads an int out of a run-variables dictionary, with the same coercion as config.</summary>
    public static int GetIntVar(IReadOnlyDictionary<string, object> variables, string key, int fallback = 0)
        => GetInt(variables, key, fallback);
```

- [ ] **Step 4: Run the loop tests to verify they fail**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Execution.LoopExecutionTests"`
Expected: FAIL — the engine does not yet handle `control.loop`, so the loop node hits the "No executor registered for TypeKey 'control.loop'" path and the run fails.

- [ ] **Step 5: Add engine-native Loop handling to `BotExecutor`**

In `AdbCore/Execution/BotExecutor.cs`:

(a) Add the using at the top, after `using AdbCore.Models;`:

```csharp
using AdbCore.Actions.BuiltIn;
```

(b) In `WalkAsync`, insert the Loop dispatch as the **first** statement inside the `while (current is not null)` block, immediately after `ct.ThrowIfCancellationRequested();` and before the executor lookup:

```csharp
            if (current.TypeKey == LoopAction.LoopTypeKey)
            {
                var loopOutcome = await ExecuteLoopAsync(state, current, ct);
                if (!loopOutcome.Success)
                {
                    return loopOutcome;
                }

                current = FindNext(state.Bot, current.Id, LoopAction.DonePort);
                continue;
            }
```

(c) Add these two methods to the `BotExecutor` class (e.g. directly after `WalkAsync`):

```csharp
    /// <summary>Engine-native Loop: re-walks the Body sub-path once per iteration (count or for-each),
    /// setting the optional index/item variables, then returns so the caller can follow Done.</summary>
    private async Task<WalkOutcome> ExecuteLoopAsync(RunState state, BotAction loop, CancellationToken ct)
    {
        var bodyStart = FindNext(state.Bot, loop.Id, LoopAction.BodyPort);
        var mode = ConfigValues.GetString(loop.Config, LoopAction.ModeKey, LoopAction.ModeCount);
        var indexVar = ConfigValues.GetString(loop.Config, LoopAction.IndexVariableKey);
        var itemVar = ConfigValues.GetString(loop.Config, LoopAction.ItemVariableKey);

        IReadOnlyList<string?> items;
        if (string.Equals(mode, LoopAction.ModeForEach, StringComparison.OrdinalIgnoreCase))
        {
            var collectionVar = ConfigValues.GetString(loop.Config, LoopAction.CollectionVariableKey);
            var raw = state.Context.Variables.TryGetValue(collectionVar, out var v) ? v : null;
            items = SplitItems(raw);
        }
        else
        {
            var count = Math.Max(0, ConfigValues.GetInt(loop.Config, LoopAction.CountKey, 0));
            var placeholders = new string?[count];
            items = placeholders;
        }

        for (var i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (!string.IsNullOrEmpty(indexVar))
            {
                state.Context.Variables[indexVar] = i;
            }

            if (!string.IsNullOrEmpty(itemVar) && items[i] is not null)
            {
                state.Context.Variables[itemVar] = items[i]!;
            }

            var bodyOutcome = await WalkAsync(state, bodyStart, ct);
            if (!bodyOutcome.Success)
            {
                return bodyOutcome;
            }
        }

        return WalkOutcome.Completed();
    }

    /// <summary>For-each item source: a comma-separated string. Empty/whitespace yields no items;
    /// each item is trimmed.</summary>
    private static IReadOnlyList<string?> SplitItems(object? raw)
    {
        var text = ConfigValues.AsString(raw);
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string?>();
        }

        return text.Split(',').Select(part => (string?)part.Trim()).ToList();
    }
```

- [ ] **Step 6: Run the loop tests to verify they pass**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Execution.LoopExecutionTests"`
Expected: PASS (6 tests). (Note: `Loop_ForEach_*` tests are added in Task 6; the Count + nested + index tests pass here.)

- [ ] **Step 7: Commit**

```bash
git add AdbCore/Actions/BuiltIn/LoopAction.cs AdbCore/Execution/BotExecutor.cs AdbCore/Actions/ConfigValues.cs AdbCore.Tests/Execution/LoopExecutionTests.cs
git commit -m "feat(core): add engine-native Loop (count mode) with Body/Done re-walk"
```

---

## Task 6: Loop — ForEach mode + item variable

The engine already implements ForEach (Task 5 Step 5c). This task adds the tests that pin down for-each behavior. A setup leaf action seeds the collection variable (there is no Set Variable action until M5b), via the `FakeExecutor` writing into `c.Context.Variables`.

**Files:**
- Modify: `AdbCore.Tests/Execution/LoopExecutionTests.cs` (add for-each tests)

- [ ] **Step 1: Add the failing for-each tests**

Append these methods to the `LoopExecutionTests` class in `AdbCore.Tests/Execution/LoopExecutionTests.cs`:

```csharp
    [Fact]
    public async Task Loop_ForEach_IteratesItemsSettingItemVariable()
    {
        // seed -> loop(forEach over "items") -> body collects the current item
        var seed = Node("seed", out var seedId);
        var loop = Node(LoopAction.LoopTypeKey, out var loopId);
        loop.Config[LoopAction.ModeKey] = LoopAction.ModeForEach;
        loop.Config[LoopAction.CollectionVariableKey] = "items";
        loop.Config[LoopAction.ItemVariableKey] = "item";
        var body = Node("body", out var bodyId);

        var bot = new Bot { Name = "loop-foreach" };
        bot.Actions.AddRange(new[] { seed, loop, body });
        bot.Connections.Add(Edge(seedId, "out", loopId));
        bot.Connections.Add(Edge(loopId, LoopAction.BodyPort, bodyId));

        var collected = new List<string>();
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor
        {
            TypeKey = "seed",
            Behavior = c => { c.Context.Variables["items"] = "a, b , c"; return ActionResult.Ok("out"); },
        });
        registry.Register(new FakeExecutor
        {
            TypeKey = "body",
            Behavior = c => { collected.Add(ConfigValues.GetString(c.Context.Variables, "item")); return ActionResult.Ok(string.Empty); },
        });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);
        Assert.Equal(new[] { "a", "b", "c" }, collected); // items are trimmed
    }

    [Fact]
    public async Task Loop_ForEach_EmptyCollection_RunsNoIterations()
    {
        var seed = Node("seed", out var seedId);
        var loop = Node(LoopAction.LoopTypeKey, out var loopId);
        loop.Config[LoopAction.ModeKey] = LoopAction.ModeForEach;
        loop.Config[LoopAction.CollectionVariableKey] = "items";
        var body = Node("body", out var bodyId);
        var done = Node("done", out var doneId);

        var bot = new Bot { Name = "loop-foreach-empty" };
        bot.Actions.AddRange(new[] { seed, loop, body, done });
        bot.Connections.Add(Edge(seedId, "out", loopId));
        bot.Connections.Add(Edge(loopId, LoopAction.BodyPort, bodyId));
        bot.Connections.Add(Edge(loopId, LoopAction.DonePort, doneId));

        var bodyCalls = 0;
        var doneReached = false;
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "seed", Behavior = c => { c.Context.Variables["items"] = ""; return ActionResult.Ok("out"); } });
        registry.Register(new FakeExecutor { TypeKey = "body", Behavior = c => { bodyCalls++; return ActionResult.Ok(string.Empty); } });
        registry.Register(new FakeExecutor { TypeKey = "done", Behavior = c => { doneReached = true; return ActionResult.Ok(string.Empty); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);
        Assert.Equal(0, bodyCalls);
        Assert.True(doneReached);
    }
```

- [ ] **Step 2: Run the for-each tests to verify they pass**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Execution.LoopExecutionTests"`
Expected: PASS (8 tests total — the ForEach engine code from Task 5 already supports these; if any fail, the bug is in `ExecuteLoopAsync`/`SplitItems` from Task 5, not the tests).

- [ ] **Step 3: Commit**

```bash
git add AdbCore.Tests/Execution/LoopExecutionTests.cs
git commit -m "test(core): cover Loop for-each mode and item variable"
```

---

## Task 7: Register the new actions and update affected count assertions

**Files:**
- Modify: `AdbCore/Actions/BuiltIn/BuiltInActions.cs`
- Modify: `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs`
- Modify: `BotBuilder.Core.Tests/PaletteViewModelTests.cs`
- Modify: `Docs/Specs/2026-06-01-m5-built-in-actions-design.md` (status line)

- [ ] **Step 1: Update the registration assertions (failing test first)**

Replace the `Register_AddsAllBuiltInsToBothRegistries` test in `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs` with:

```csharp
    [Fact]
    public void Register_AddsAllBuiltInsToBothRegistries()
    {
        var defs = new ActionRegistry();
        var execs = new ActionExecutorRegistry();

        BuiltInActions.Register(defs, execs);

        foreach (var key in new[] { "control.start", "control.end", "data.log", "control.delay", "control.branch" })
        {
            Assert.True(defs.TryGet(key, out _));
            Assert.True(execs.TryGet(key, out _));
        }

        // Loop is engine-native: a definition (palette/panel metadata) with no executor.
        Assert.True(defs.TryGet("control.loop", out _));
        Assert.False(execs.TryGet("control.loop", out _));

        Assert.Equal(6, defs.Count);
        Assert.Equal(5, execs.Count);
    }
```

- [ ] **Step 2: Update the palette count assertions**

In `BotBuilder.Core.Tests/PaletteViewModelTests.cs`:

- In `Categories_GroupBuiltInsByCategory`, change `Assert.Equal(2, control.Items.Count);` to:

```csharp
        Assert.Equal(5, control.Items.Count); // Start, End, Delay, Branch, Loop
```

- In `ClearingSearch_RestoresAll`, change `Assert.Equal(3, palette.Categories.SelectMany(c => c.Items).Count());` to:

```csharp
        Assert.Equal(6, palette.Categories.SelectMany(c => c.Items).Count()); // 5 Control Flow + 1 Data
```

(`Assert.Single(data.Items)` and the `Search_MatchesByCategoryName` Start/End assertions remain correct.)

- [ ] **Step 3: Run those tests to verify they fail**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Actions.BuiltIn.BuiltInActionsTests|FullyQualifiedName~BotBuilder.Core.Tests.PaletteViewModelTests"`
Expected: FAIL — counts don't match yet (Delay/Branch/Loop not registered).

- [ ] **Step 4: Register the new actions**

Replace the body of `BuiltInActions.Register` in `AdbCore/Actions/BuiltIn/BuiltInActions.cs` with:

```csharp
    public static void Register(ActionRegistry definitions, ActionExecutorRegistry executors)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(executors);

        Add(new StartAction(), definitions, executors);
        Add(new EndAction(), definitions, executors);
        Add(new LogAction(), definitions, executors);
        Add(new DelayAction(), definitions, executors);
        Add(new BranchAction(), definitions, executors);

        // Loop is engine-native: register its definition only (no executor).
        definitions.Register(new LoopAction());
    }
```

(Leave the private `Add<T>` helper unchanged.)

- [ ] **Step 5: Run the full test suite**

Run: `dotnet test ADB.slnx`
Expected: PASS (all tests). If any other test asserts a built-in registry or palette count, update it to reflect the three added Control Flow actions (Delay, Branch, Loop) — none other are expected based on the current suite.

- [ ] **Step 6: Full build with zero warnings**

Run: `dotnet build ADB.slnx`
Expected: Build succeeded, **0 warnings, 0 errors**.

- [ ] **Step 7: Update the design spec status**

In `Docs/Specs/2026-06-01-m5-built-in-actions-design.md`, change the status line:

From:
```markdown
**Status:** Approved (scoping complete; pending spec review)
```
To:
```markdown
**Status:** Approved — M5a1 (engine v2 + Branch/Loop/Delay) implemented
```

- [ ] **Step 8: Commit**

```bash
git add AdbCore/Actions/BuiltIn/BuiltInActions.cs AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs BotBuilder.Core.Tests/PaletteViewModelTests.cs Docs/Specs/2026-06-01-m5-built-in-actions-design.md
git commit -m "feat(actions): register Delay/Branch/Loop control-flow actions"
```

---

## Manual Verification Checklist (for the user)

M5a1 is headless engine/action logic with no UI surface of its own, but the new Control Flow actions now appear in the Builder palette. After merge, in BotBuilder:

- [ ] Open BotBuilder. The **Control Flow** palette category lists **Start, End, Delay, Branch, Loop** (Loop shows **Body** and **Done** output ports; Branch shows **True** and **False**).
- [ ] Drag a **Loop** onto the canvas and select it — the Properties Panel shows Mode (Count/ForEach dropdown), Count, Collection Variable, Index Variable, Item Variable.
- [ ] Drag a **Branch** — the panel shows Variable, Operator (dropdown of the 8 operators), Value.
- [ ] Drag a **Delay** — the panel shows Duration (ms).
- [ ] Build a tiny bot (Start → Loop; Loop Body → Log "hi"; Loop Done → End), save it, and run it via the BotRunner — confirm "hi" logs the expected number of times. *(Set Variable lands in M5b; until then use Count mode for an easy manual check.)*

---

## Self-Review (completed by plan author)

**Spec coverage (against `2026-06-01-m5-built-in-actions-design.md` §8, slice M5a1):**
- Engine v2 recursive walker — Task 4. ✓
- Branch — Task 3. ✓
- Loop (Blueprints Body/Done, count + for-each, index/item vars) — Tasks 5–6. ✓
- Delay — Task 2. ✓
- Start/End preserved — Task 4 keeps behavior; existing tests green. ✓
- Concurrency-ready seam (`WalkAsync`) for M5a2 — Task 4 architecture note. ✓
- Config reading for JsonElement + boxed primitives (cross-cutting need) — Task 1. ✓
- Spec's deferred decisions resolved here: ForEach source = comma-separated string (Task 5 `SplitItems`); HWND resolution is **not** in M5a1 (it's M5c). ✓

**Placeholder scan:** No TBD/TODO; every code step shows complete code; no "handle edge cases" hand-waves. ✓

**Type consistency:** `LoopAction.LoopTypeKey/BodyPort/DonePort/ModeKey/CountKey/CollectionVariableKey/IndexVariableKey/ItemVariableKey/ModeCount/ModeForEach`, `BranchAction.*` constants, `DelayAction.DurationMsKey`, and `ConfigValues.GetString/GetInt/GetDouble/GetBool/AsString/TryAsDouble/AsBool/GetIntVar` are referenced consistently across tasks and tests. The engine references `LoopAction.*` via `using AdbCore.Actions.BuiltIn;` (added in Task 5 Step 5a). ✓

**Out of scope (deferred to later slices):** Run Parallel / Join (M5a2), Set Variable / Comment (M5b), Input (M5c), Screen (M5d). End-node global-termination semantics inside control flow are unchanged from current behavior (End dead-ends its sub-walk).
