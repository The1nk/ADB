# Math Action Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A visual **Math** built-in action (`data.math`, Data category) that computes `operation(left, right)` over literals/`${var}` operands and stores a numeric result in a run variable — arithmetic + standard-library functions (rounding, abs, sqrt, min/max, power, random).

**Architecture:** One `MathAction` implementing `IActionDefinition`+`IActionExecutor`, mirroring `BranchAction` (enum config field with `Options`, `ConfigValues.GetString`, `double.TryParse` w/ `InvariantCulture`). `${var}` is interpolated upstream by `ConfigInterpolator` (no work needed in the action). `Random.Shared` (thread-safe) backs the random ops. Errors route to `onFailure`.

**Tech Stack:** C# / .NET 10, AdbCore, xUnit.

**Reference spec:** `Docs/Specs/2026-06-05-math-calculate-action-design.md`.

**Merge handling:** backend-only AdbCore action, deterministic unit tests, no live deps, no custom UI → built compile-clean + unit-green and **self-merged** via `gh` (user-authorized). Independent of open PR #34 (isolated to `AdbCore/Scripting/**`).

**`<WT>` = worktree path the controller provides (e.g. `C:\git\ADB\.claude\worktrees\math-action`). Windows: use PowerShell for `dotnet`/`git`; NEVER redirect to `/dev/null`/`NUL`.**

---

## File Structure

- Create `AdbCore/Actions/BuiltIn/MathAction.cs` — the action.
- Create `AdbCore.Tests/Actions/BuiltIn/MathActionTests.cs` — behavior + metadata tests.
- Create `AdbCore.Tests/Actions/BuiltIn/MathRegistrationTests.cs` — registry resolution.
- Modify `AdbCore/Actions/BuiltIn/BuiltInActions.cs` — register it (Data group, near `SetVariableAction`).
- Modify `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs` — bump def/exec counts +1 each.
- Modify `BotBuilder.Core.Tests/PaletteViewModelTests.cs` — bump total +1 and the Data category count +1.

---

## Task 1: `MathAction` + behavior tests

**Files:** Create `AdbCore/Actions/BuiltIn/MathAction.cs`, `AdbCore.Tests/Actions/BuiltIn/MathActionTests.cs`.

- [ ] **Step 1: Write the failing tests.** Create `AdbCore.Tests/Actions/BuiltIn/MathActionTests.cs`:
```csharp
using System.Linq;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class MathActionTests
{
    private static ActionExecutionContext Exec(BotAction a, BotExecutionContext c) => new(a, c, _ => { });

    private static (ActionResult result, BotExecutionContext ctx) Run(string op, string left, string right, string resultVar = "r")
    {
        var ctx = new BotExecutionContext();
        var action = new BotAction
        {
            Config =
            {
                [MathAction.OperationKey] = op,
                [MathAction.LeftKey] = left,
                [MathAction.RightKey] = right,
                [MathAction.ResultKey] = resultVar,
            },
        };
        var r = new MathAction().ExecuteAsync(Exec(action, ctx), default).GetAwaiter().GetResult();
        return (r, ctx);
    }

    [Theory]
    [InlineData(MathAction.OpAdd, "2", "3", 5d)]
    [InlineData(MathAction.OpSubtract, "10", "4", 6d)]
    [InlineData(MathAction.OpMultiply, "6", "7", 42d)]
    [InlineData(MathAction.OpDivide, "9", "2", 4.5d)]
    [InlineData(MathAction.OpModulo, "7", "3", 1d)]
    [InlineData(MathAction.OpPower, "2", "10", 1024d)]
    [InlineData(MathAction.OpMin, "3", "7", 3d)]
    [InlineData(MathAction.OpMax, "3", "7", 7d)]
    public void Binary_Ops_Compute(string op, string l, string r, double expected)
    {
        var (res, ctx) = Run(op, l, r);
        Assert.True(res.Success);
        Assert.Equal("onSuccess", res.OutputPort);
        Assert.Equal(expected, (double)ctx.Variables["r"]);
    }

    [Theory]
    [InlineData(MathAction.OpFloor, "2.9", 2d)]
    [InlineData(MathAction.OpCeil, "2.1", 3d)]
    [InlineData(MathAction.OpRound, "2.5", 3d)]   // AwayFromZero
    [InlineData(MathAction.OpAbs, "-4", 4d)]
    [InlineData(MathAction.OpSqrt, "9", 3d)]
    [InlineData(MathAction.OpNegate, "5", -5d)]
    public void Unary_Ops_Compute(string op, string l, double expected)
    {
        var (res, ctx) = Run(op, l, "");   // right ignored
        Assert.True(res.Success);
        Assert.Equal(expected, (double)ctx.Variables["r"]);
    }

    [Fact]
    public void Random_InUnitInterval()
    {
        for (var i = 0; i < 50; i++)
        {
            var (res, ctx) = Run(MathAction.OpRandom, "", "");
            Assert.True(res.Success);
            var v = (double)ctx.Variables["r"];
            Assert.InRange(v, 0d, 1d);
            Assert.True(v < 1d);   // [0,1)
        }
    }

    [Fact]
    public void RandomInt_InInclusiveRange_AndIntegral()
    {
        for (var i = 0; i < 100; i++)
        {
            var (res, ctx) = Run(MathAction.OpRandomInt, "1", "6");
            Assert.True(res.Success);
            var v = (double)ctx.Variables["r"];
            Assert.InRange(v, 1d, 6d);
            Assert.Equal(v, System.Math.Floor(v)); // integral
        }
    }

    [Fact]
    public void RandomInt_SingleValue()
    {
        var (res, ctx) = Run(MathAction.OpRandomInt, "4", "4");
        Assert.True(res.Success);
        Assert.Equal(4d, (double)ctx.Variables["r"]);
    }

    [Fact]
    public void DivideByZero_Fails()
    {
        var (res, _) = Run(MathAction.OpDivide, "5", "0");
        Assert.False(res.Success);
        Assert.Contains("divide by zero", res.ErrorMessage);
    }

    [Fact]
    public void ModuloByZero_Fails()
    {
        var (res, _) = Run(MathAction.OpModulo, "5", "0");
        Assert.False(res.Success);
        Assert.Contains("modulo by zero", res.ErrorMessage);
    }

    [Fact]
    public void SqrtOfNegative_Fails_NonFinite()
    {
        var (res, _) = Run(MathAction.OpSqrt, "-1", "");
        Assert.False(res.Success);
        Assert.Contains("not a finite number", res.ErrorMessage);
    }

    [Fact]
    public void RandomInt_MinGreaterThanMax_Fails()
    {
        var (res, _) = Run(MathAction.OpRandomInt, "10", "2");
        Assert.False(res.Success);
        Assert.Contains("min", res.ErrorMessage);
    }

    [Fact]
    public void NonNumericOperand_Fails()
    {
        var (res, _) = Run(MathAction.OpAdd, "abc", "3");
        Assert.False(res.Success);
        Assert.Contains("not a number", res.ErrorMessage);
    }

    [Fact]
    public void EmptyResultVariable_Fails()
    {
        var (res, _) = Run(MathAction.OpAdd, "1", "2", resultVar: "");
        Assert.False(res.Success);
        Assert.Contains("result variable", res.ErrorMessage);
    }

    [Fact]
    public void Definition_Metadata()
    {
        var def = new MathAction();
        Assert.Equal("data.math", def.TypeKey);
        Assert.Equal("Math", def.DisplayName);
        Assert.Equal("Data", def.Category);
        Assert.Equal(new[] { "onSuccess", "onFailure" }, def.OutputPorts.Select(p => p.Name));
        var opField = Assert.Single(def.ConfigFields, f => f.Key == MathAction.OperationKey);
        Assert.Equal(ConfigFieldType.Enum, opField.Type);
        Assert.Equal(16, opField.Options!.Count);
        Assert.Contains(def.ConfigFields, f => f.Key == MathAction.LeftKey);
        Assert.Contains(def.ConfigFields, f => f.Key == MathAction.RightKey);
        Assert.Contains(def.ConfigFields, f => f.Key == MathAction.ResultKey);
    }
}
```
**Adaptation:** confirm `BotExecutionContext.Variables` is a get-only dictionary (collection-initializer on `Config` works as in `RunLuaScriptActionTests`); confirm `ActionResult.OutputPort`/`ErrorMessage`/`Ok(port)`/`Fail(msg)` and `ActionExecutionContext(BotAction, BotExecutionContext, Action<string>)` (all confirmed used across the suite — match the real API). If `Assert.Single(collection, predicate)` overload differs, use `def.ConfigFields.Single(f => ...)`.

- [ ] **Step 2: Run to verify it fails** — `dotnet test "<WT>\AdbCore.Tests" --filter "FullyQualifiedName~MathActionTests"` → compile FAIL.

- [ ] **Step 3: Create `AdbCore/Actions/BuiltIn/MathAction.cs`:**
```csharp
using System.Globalization;
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Computes <c>operation(left, right)</c> over numeric literals/<c>${var}</c> operands and stores the
/// result (a double) in a run variable. Arithmetic plus standard-library functions (rounding, abs, sqrt,
/// min/max, power, random). Computation errors (non-numeric operand, divide/modulo by zero, non-finite result)
/// route to onFailure.</summary>
public sealed class MathAction : IActionDefinition, IActionExecutor
{
    public const string OperationKey = "operation";
    public const string LeftKey = "left";
    public const string RightKey = "right";
    public const string ResultKey = "resultVariable";

    public const string SuccessPort = "onSuccess";
    public const string FailurePort = "onFailure";

    // Binary
    public const string OpAdd = "Add";
    public const string OpSubtract = "Subtract";
    public const string OpMultiply = "Multiply";
    public const string OpDivide = "Divide";
    public const string OpModulo = "Modulo";
    public const string OpPower = "Power";
    public const string OpMin = "Min";
    public const string OpMax = "Max";
    // Unary
    public const string OpFloor = "Floor";
    public const string OpCeil = "Ceil";
    public const string OpRound = "Round";
    public const string OpAbs = "Abs";
    public const string OpSqrt = "Sqrt";
    public const string OpNegate = "Negate";
    // Random
    public const string OpRandom = "Random";
    public const string OpRandomInt = "RandomInt";

    public string TypeKey => "data.math";
    public string DisplayName => "Math";
    public string Category => "Data";
    public string Description => "Computes an arithmetic or standard-library math operation and stores the result in a variable.";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public List<PortDefinition> OutputPorts { get; } = new()
    {
        new PortDefinition { Name = SuccessPort, Label = "On Success" },
        new PortDefinition { Name = FailurePort, Label = "On Failure" },
    };
    public List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField
        {
            Key = OperationKey,
            Label = "Operation",
            Type = ConfigFieldType.Enum,
            DefaultValue = OpAdd,
            Options = new()
            {
                OpAdd, OpSubtract, OpMultiply, OpDivide, OpModulo, OpPower, OpMin, OpMax,
                OpFloor, OpCeil, OpRound, OpAbs, OpSqrt, OpNegate,
                OpRandom, OpRandomInt,
            },
        },
        new ConfigField { Key = LeftKey, Label = "Left", Type = ConfigFieldType.String },
        new ConfigField { Key = RightKey, Label = "Right", Type = ConfigFieldType.String },
        new ConfigField { Key = ResultKey, Label = "Result Variable", Type = ConfigFieldType.String },
    };
    public bool SupportsRetry => false;

    public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var op = ConfigValues.GetString(context.Action.Config, OperationKey, OpAdd);
        var leftText = ConfigValues.GetString(context.Action.Config, LeftKey);
        var rightText = ConfigValues.GetString(context.Action.Config, RightKey);
        var resultVar = ConfigValues.GetString(context.Action.Config, ResultKey);

        if (string.IsNullOrWhiteSpace(resultVar))
        {
            return Fail("result variable name is required");
        }

        if (!Compute(op, leftText, rightText, out var value, out var error))
        {
            return Fail(error);
        }

        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return Fail("result is not a finite number");
        }

        context.Context.Variables[resultVar] = value;
        return Task.FromResult(ActionResult.Ok(SuccessPort));

        Task<ActionResult> Fail(string msg) => Task.FromResult(ActionResult.Fail($"Math: {msg}"));
    }

    private static bool TryParse(string text, out double value)
        => double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value);

    /// <summary>Computes the op into <paramref name="value"/>; returns false with a message in
    /// <paramref name="error"/> on an operand/operation error. Does NOT check finiteness (the caller does).</summary>
    private static bool Compute(string op, string leftText, string rightText, out double value, out string error)
    {
        value = 0;
        error = string.Empty;

        // Random takes no operands.
        if (op == OpRandom)
        {
            value = Random.Shared.NextDouble();
            return true;
        }

        // Everything else needs a numeric left.
        if (!TryParse(leftText, out var a))
        {
            error = $"left operand '{leftText}' is not a number";
            return false;
        }

        // Unary ops use only left.
        switch (op)
        {
            case OpFloor: value = Math.Floor(a); return true;
            case OpCeil: value = Math.Ceiling(a); return true;
            case OpRound: value = Math.Round(a, MidpointRounding.AwayFromZero); return true;
            case OpAbs: value = Math.Abs(a); return true;
            case OpSqrt: value = Math.Sqrt(a); return true;       // negative -> NaN -> non-finite (caller fails)
            case OpNegate: value = -a; return true;
        }

        // Remaining ops are binary -> need a numeric right.
        if (!TryParse(rightText, out var b))
        {
            error = $"right operand '{rightText}' is not a number";
            return false;
        }

        switch (op)
        {
            case OpAdd: value = a + b; return true;
            case OpSubtract: value = a - b; return true;
            case OpMultiply: value = a * b; return true;
            case OpDivide:
                if (b == 0) { error = "divide by zero"; return false; }
                value = a / b; return true;
            case OpModulo:
                if (b == 0) { error = "modulo by zero"; return false; }
                value = a % b; return true;
            case OpPower: value = Math.Pow(a, b); return true;
            case OpMin: value = Math.Min(a, b); return true;
            case OpMax: value = Math.Max(a, b); return true;
            case OpRandomInt:
                var lo = (long)Math.Round(a, MidpointRounding.AwayFromZero);
                var hi = (long)Math.Round(b, MidpointRounding.AwayFromZero);
                if (lo > hi) { error = "RandomInt requires left (min) <= right (max)"; return false; }
                value = Random.Shared.NextInt64(lo, hi + 1); // inclusive of hi
                return true;
            default:
                error = $"unknown operation '{op}'";
                return false;
        }
    }
}
```
**Adaptation:** match the real `IActionDefinition`/`IActionExecutor` members against a sibling (`BranchAction.cs`) — confirm `PortDefinition{Name,Label}`, `ConfigField{Key,Label,Type,DefaultValue,Options}`, `ConfigFieldType.Enum`, `ConfigValues.GetString(config, key[, default])`, `ActionResult.Ok/Fail`. Confirm `Random.Shared.NextInt64(long, long)` exists on net10.0 (it does, since .NET 6). The local `Fail` function after the `return` is a valid C# local function — if the compiler dislikes its placement, hoist it to a private static helper `ActionResult.Fail($"Math: {msg}")`. The `Definition_Metadata` test + green build are the spec.

- [ ] **Step 4: Run to verify it passes** — `dotnet test "<WT>\AdbCore.Tests" --filter "FullyQualifiedName~MathActionTests"` → all pass.

- [ ] **Step 5: Commit:**
```
git -C "<WT>" add AdbCore/Actions/BuiltIn/MathAction.cs AdbCore.Tests/Actions/BuiltIn/MathActionTests.cs
git -C "<WT>" commit -m "feat(data): Math action (arithmetic + stdlib functions + random)"
```

---

## Task 2: Register + counts + registration test + sweep

**Files:** Modify `AdbCore/Actions/BuiltIn/BuiltInActions.cs`; create `AdbCore.Tests/Actions/BuiltIn/MathRegistrationTests.cs`; modify `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs` + `BotBuilder.Core.Tests/PaletteViewModelTests.cs`.

- [ ] **Step 1: Register.** In `BuiltInActions.cs`, alongside `SetVariableAction` (the Data group, no external deps), add:
```csharp
        Add(new MathAction(), definitions, executors);
```
(Match the real `Add(...)` signature/pattern there.)

- [ ] **Step 2: Registration test.** Create `AdbCore.Tests/Actions/BuiltIn/MathRegistrationTests.cs`:
```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class MathRegistrationTests
{
    [Fact]
    public void Math_IsRegistered()
    {
        var defs = new ActionRegistry();
        var execs = new ActionExecutorRegistry();
        BuiltInActions.Register(defs, execs);
        Assert.True(defs.TryGet("data.math", out _));
        Assert.True(execs.TryGet("data.math", out var e) && e is not null);
    }
}
```
(Confirm `ActionRegistry`/`ActionExecutorRegistry`/`Register`/`TryGet` names against `RunLuaRegistrationTests.cs`/`BuiltInActionsTests.cs`; adapt if needed.)

- [ ] **Step 3: Bump counts.** Read the CURRENT counts in `BuiltInActionsTests.cs` (post-M12a these are defs `43` / execs `40`) and bump each by exactly +1 → `44` / `41`. In `BotBuilder.Core.Tests/PaletteViewModelTests.cs`, read the current total (`43`) and bump +1 → `44`, and bump the **Data** category assertion (or add one) by +1 to reflect the new Math item. (If the base numbers differ from 43/40/43, +1 each from whatever is actually there and report.)

- [ ] **Step 4: Run to verify it passes:**
```
dotnet test "<WT>\AdbCore.Tests" --filter "FullyQualifiedName~MathRegistrationTests|FullyQualifiedName~BuiltInActionsTests"
dotnet test "<WT>\BotBuilder.Core.Tests" --filter "FullyQualifiedName~PaletteViewModelTests"
```
Both → PASS.

- [ ] **Step 5: Full sweep.** `dotnet build "<WT>\ADB.slnx" -warnaserror -v q --nologo` → 0 warnings; `dotnet test "<WT>\ADB.slnx"` → all green. Report totals.

- [ ] **Step 6: Commit:**
```
git -C "<WT>" add AdbCore/Actions/BuiltIn/BuiltInActions.cs AdbCore.Tests/Actions/BuiltIn/MathRegistrationTests.cs AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs BotBuilder.Core.Tests/PaletteViewModelTests.cs
git -C "<WT>" commit -m "feat(data): register Math action (Data category)"
```

---

## Self-Review Notes (addressed)

- **Spec coverage:** all 16 ops (binary/unary/random) with the arity dispatch (Task 1 `Compute`); `${var}` via upstream interpolation (no action work); double result; `onFailure` error model incl. div0/mod0/non-finite/min>max/non-numeric/empty-result; metadata + registration + counts (Task 2). ✓
- **Thread-safety:** `Random.Shared` is thread-safe → safe under Run Parallel (no shared-RNG hazard, per the M11a lesson). ✓
- **Type consistency:** `MathAction.OperationKey/LeftKey/RightKey/ResultKey`, the 16 `Op*` constants, ports `onSuccess`/`onFailure`, result stored as `double`. ✓
- **No new external deps; no custom UI** (generic node/properties templates) → backend-only, self-mergeable.
