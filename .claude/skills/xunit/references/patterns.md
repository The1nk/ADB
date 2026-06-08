# xUnit Patterns Reference

## Contents
- Fake Placement Convention
- Fake Design: required init vs Constructor
- Assertion Style
- Anti-Patterns
- Naming Convention

---

## Fake Placement Convention

Each `*.Tests` project contains its fakes **alongside** the tests that use them — not in a shared library.

| Fake file | Lives in |
|-----------|----------|
| `FakeExecutor.cs` | `AdbCore.Tests/Execution/` |
| `FakeActionDefinition.cs` | `AdbCore.Tests/Actions/` |
| `FakeAndroidDevice.cs` | `AdbCore.Tests/Actions/BuiltIn/Android/` |
| `FakeBrowserPage.cs` | `AdbCore.Tests/Actions/BuiltIn/Browser/` |
| `Fakes.cs` | `BotCapture.Core.Tests/` |

Put a new fake in the same folder as the tests that use it. Never move fakes to `AdbCore` itself — fakes are test-only and must not ship in production assemblies. Mark them `internal sealed`.

---

## Fake Design: `required init` vs Constructor

**Prefer `required` + `init` for configurable fakes** — it makes misconfiguration a compile error and reads like a named-argument call site.

```csharp
// GOOD — from FakeExecutor.cs
internal sealed class FakeExecutor : IActionExecutor
{
    public required string TypeKey { get; init; }
    public Func<ActionExecutionContext, ActionResult> Behavior { get; init; } = _ => ActionResult.Ok("out");
    public int Calls { get; private set; }
    // ...
}

// Usage is self-documenting
registry.Register(new FakeExecutor { TypeKey = "a", Behavior = _ => ActionResult.Fail("boom") });
```

Use a constructor only when the fake has mandatory invariants that `required` can't express (rare).

---

## Assertion Style

xUnit has no built-in fluent chain. Stick to `Assert.*` static methods.

```csharp
// GOOD — explicit, matches xUnit convention (expected first)
Assert.Equal(new[] { "a", "b", "c" }, order);
Assert.True(result.Success);
Assert.False(reachedNever);
Assert.Contains("entry point", result.ErrorMessage);
Assert.All(reports, r => Assert.True(r.Success));

// BAD — do not add FluentAssertions or Shouldly without team alignment
result.Should().BeSuccessful(); // not used in this codebase
```

For error messages, `Assert.Contains(substring, fullMessage)` is preferred over `Assert.Equal` — it survives wording changes.

---

## Anti-Patterns

### WARNING: Mocking Frameworks

**The Problem:**

```csharp
// BAD — Moq/NSubstitute are not used in this project
var mock = new Mock<IActionExecutor>();
mock.Setup(x => x.ExecuteAsync(It.IsAny<ActionExecutionContext>(), default))
    .ReturnsAsync(ActionResult.Ok("out"));
```

**Why This Breaks:**
1. Mocks obscure which interface members you're actually exercising.
2. Setup strings/lambdas break silently when interfaces evolve.
3. Hand-rolled fakes in this codebase are already more readable — adding a mock framework creates inconsistency and a new dependency.

**The Fix:** Write a `Fake*` class implementing the interface directly. See `FakeExecutor.cs`.

---

### WARNING: Shared Mutable Fake State Across Tests

**The Problem:**

```csharp
// BAD — static or field-level fake shared between [Fact] methods
private readonly FakeExecutor _shared = new() { TypeKey = "a" };

[Fact]
public void Test1() { _shared.Calls; /* polluted by other tests */ }
[Fact]
public void Test2() { _shared.Calls; /* execution order dependent */ }
```

**Why This Breaks:**
1. xUnit creates a new test class instance per `[Fact]`, but shared statics or reused object graphs survive across tests.
2. `Calls` counters and `Behavior` lambdas accumulate state.

**The Fix:** Construct fakes inside each `[Fact]` or in a private helper method called from the test.

---

### WARNING: `Assert.Equal` Argument Order

**The Problem:**

```csharp
// BAD — actual first; failure message says "Expected: <actual> Actual: <expected>"
Assert.Equal(result.ActionsExecuted, 3);
```

**Why This Breaks:** xUnit's failure output is backwards — extremely confusing to debug.

**The Fix:** Always `Assert.Equal(expected, actual)`.

---

## Naming Convention

Follow the existing pattern: `MethodOrScenario_Context_ExpectedBehavior` — readable without needing a comment.

```csharp
// GOOD — matches repo style
[Fact]
public async Task RunAsync_MissingExecutor_FailsGracefully() { }

[Fact]
public void Execute_RunsDo_AndEnablesUndo() { }

// BAD
[Fact]
public void Test1() { }

[Fact]
public void BotExecutorTest() { }
```