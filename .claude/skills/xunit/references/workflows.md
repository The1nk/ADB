# xUnit Workflows Reference

## Contents
- Adding a New Test Class
- Adding a Fake for a New Interface
- Running and Filtering Tests
- Debugging a Failing Test
- Test Checklist

---

## Adding a New Test Class

1. Identify the `*.Tests` project that mirrors the production project you're testing.
2. Mirror the folder structure: `AdbCore/Execution/BotExecutor.cs` → `AdbCore.Tests/Execution/BotExecutorTests.cs`.
3. Use the file-scoped namespace matching the folder: `namespace AdbCore.Tests.Execution;`
4. Annotate with `[Fact]` (single case) or `[Theory]` + `[InlineData]` (parameterized).

```csharp
// new code to add
using Xunit;

namespace AdbCore.Tests.Execution;

public class ConfigInterpolatorTests
{
    [Theory]
    [InlineData("hello ${name}", "world", "hello world")]
    [InlineData("no vars", "anything", "no vars")]
    public void Interpolate_ReplacesKnownVars(string template, string value, string expected)
    {
        var vars = new Dictionary<string, string> { ["name"] = value };
        Assert.Equal(expected, ConfigInterpolator.Interpolate(template, vars));
    }
}
```

---

## Adding a Fake for a New Interface

When production code introduces a new interface that tests need to control:

1. Create `Fake{InterfaceName}.cs` next to the tests that will use it.
2. Mark it `internal sealed`.
3. Use `required` + `init` for configurable properties; use public mutable fields for call recording.
4. Provide a sensible default behavior (don't throw by default — return a neutral value).

```csharp
// new code to add — example for a hypothetical INotifier
internal sealed class FakeNotifier : INotifier
{
    public List<string> Sent = new();
    public bool ShouldFail { get; init; }

    public Task NotifyAsync(string message, CancellationToken ct)
    {
        if (ShouldFail) return Task.FromException(new InvalidOperationException("notify failed"));
        Sent.Add(message);
        return Task.CompletedTask;
    }
}
```

---

## Running and Filtering Tests

```bash
# All tests
dotnet test ADB.slnx

# One project
dotnet test AdbCore.Tests

# One class (substring match)
dotnet test --filter "FullyQualifiedName~BotExecutorTests"

# One method
dotnet test --filter "FullyQualifiedName~BotExecutorTests.RunAsync_LinearPath_ExecutesAllInOrderAndSucceeds"

# Verbose output with failure details
dotnet test ADB.slnx --logger "console;verbosity=normal"
```

---

## Debugging a Failing Test

See the **systematic-debugging** skill for the full workflow. For xUnit specifically:

1. **Read the full failure output** — xUnit prints `Expected` / `Actual` for `Assert.Equal` failures. Check argument order (expected first).
2. **Isolate** — run only the failing test with `--filter`.
3. **Add intermediate assertions** — assert intermediate state before the final assert to pinpoint where divergence starts.
4. **Check fake wiring** — a fake that returns the wrong default silently makes the production path take an unexpected branch. Verify `Behavior` delegates and default return values.
5. **Async pitfalls** — ensure `await` is present on every async call in the test; missing `await` yields vacuously passing tests.

```csharp
// Catching an unexpected exception to get a useful message instead of a hang
var ex = await Assert.ThrowsAsync<InvalidOperationException>(
    () => sut.RunAsync(bot, options, null, default));
Assert.Contains("expected fragment", ex.Message);
```

---

## Test Checklist

Copy this checklist for any non-trivial test addition:

- [ ] Test file mirrors production file path under the matching `*.Tests` project
- [ ] Namespace is file-scoped and matches folder
- [ ] `[Fact]` for single case, `[Theory]` + `[InlineData]` for variants
- [ ] `Assert.Equal(expected, actual)` — expected is first argument
- [ ] No mock framework — fakes are hand-rolled `internal sealed` classes
- [ ] Fake placed in the same folder as the test that uses it
- [ ] Fakes have sensible non-throwing defaults
- [ ] Async tests use `async Task` return type and `await` throughout
- [ ] `dotnet test ADB.slnx` passes with no new failures

---

## Iterate-Until-Pass

```
1. Write / modify the test
2. dotnet test --filter "FullyQualifiedName~<YourTestClass>"
3. If red: read failure output → fix production code or fix test assertion
4. Repeat step 2 until green
5. dotnet test ADB.slnx   ← full suite before committing
```