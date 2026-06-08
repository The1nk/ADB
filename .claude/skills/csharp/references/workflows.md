# C# Workflows Reference

## Contents
- Adding a New Action
- Adding a New Model
- Test Workflow
- Refactor Checklist

---

## Adding a New Action

New actions implement `IActionDefinition` (metadata/fields) and `IActionExecutor` (runtime logic), then register in the action registry. No hardcoding in the palette — discovery is automatic.

```csharp
// new code to add — AdbCore/Actions/BuiltIn/MyNewAction.cs
public class MyNewActionDefinition : IActionDefinition
{
    public string Id => "builtin.my_new_action";
    public string DisplayName => "My New Action";
    public string Category => "Utility";
    public IReadOnlyList<ConfigField> Fields => [
        new ConfigField("Delay", ConfigFieldType.Integer, defaultValue: 500)
    ];
}

public class MyNewActionExecutor : IActionExecutor
{
    public async Task<ActionResult> ExecuteAsync(
        BotAction action, BotExecutionContext ctx, CancellationToken ct)
    {
        int delay = action.Config.GetInt("Delay");
        await Task.Delay(delay, ct);
        return ActionResult.Ok();
    }
}
```

**Checklist:**
- [ ] Implement `IActionDefinition` with a unique stable `Id`
- [ ] Implement `IActionExecutor`; keep it stateless
- [ ] Register both in the DI/registry setup
- [ ] Add an xUnit test in `AdbCore.Tests` using a `FakeExecutor` context
- [ ] Validate: `dotnet test ADB.slnx`

---

## Adding a New Model

Prefer `record` for config/result types, `class` for view-models.

```csharp
// new code to add — AdbCore/Models/RunSummary.cs
public record RunSummary(
    Guid BotId,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    bool Succeeded,
    IReadOnlyList<string> Errors);
```

Serialize with `System.Text.Json` (already used for `.bot` files). Add `[JsonConstructor]` if needed for deserialization.

---

## Test Workflow

ADB uses xUnit + hand-rolled fakes. No Moq. See the **xunit** skill.

```csharp
// new code to add — AdbCore.Tests/Actions/MyNewActionTests.cs
public class MyNewActionTests
{
    [Fact]
    public async Task ExecuteAsync_DelaysAndSucceeds()
    {
        var action = new BotAction { Config = new() { ["Delay"] = 100 } };
        var ctx = FakeBotExecutionContext.Create();
        var executor = new MyNewActionExecutor();

        var result = await executor.ExecuteAsync(action, ctx, CancellationToken.None);

        Assert.True(result.Success);
    }
}
```

**Feedback loop:**
1. Write failing test
2. Implement feature
3. `dotnet test ADB.slnx --filter FullyQualifiedName~MyNewAction`
4. Fix until green, then run full suite: `dotnet test ADB.slnx`

---

## Refactor Checklist

Copy this checklist when touching existing C# files:

- [ ] No new `!` null-forgiving operators introduced
- [ ] No `.Result` / `.Wait()` on tasks
- [ ] No static mutable fields added to executors
- [ ] `catch` clauses catch specific exception types; `OperationCanceledException` not swallowed
- [ ] New public types have XML doc comments only if they form a public API surface
- [ ] `dotnet build ADB.slnx` — zero warnings (treat-warnings-as-errors is typical for this stack)
- [ ] `dotnet test ADB.slnx` — all green

---

## DO/DON'T Summary

| DO | DON'T |
|----|-------|
| Use `required` init properties for mandatory fields | Use `!` to suppress nullable warnings |
| Use `record` for config/result value objects | Use mutable classes for immutable data |
| Use `switch` expressions with exhaustive patterns | Use long `if/else if` type chains |
| Let `OperationCanceledException` propagate | Catch `Exception` without rethrowing |
| Keep executor classes stateless | Store run state in fields or statics |
| Use `ctx.Variables` for per-run state | Use static fields for inter-step data |