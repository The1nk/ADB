---
name: xunit
description: |
  Structures and runs unit tests with test discovery and assertion validation.
  Use when: writing or modifying any test class, adding fakes/test doubles, choosing assertion style, running dotnet test, or debugging failing tests in ADB's *.Tests projects.
allowed-tools: Read, Edit, Write, Glob, Grep, Bash
---

# xUnit Skill

xUnit is the test framework used across all ADB projects (`AdbCore.Tests`, `BotBuilder.Core.Tests`, `BotCapture.Core.Tests`, `BotRunner.Tests`, `AdbUi.Theme.Tests`). Tests use hand-rolled fakes — no mocking framework. All test projects target `net10.0-windows` with strict nullable enabled.

## Before You Code (REQUIRED)

This skill's content was captured at generation time and MAY be stale. For ANY non-trivial change involving xunit, verify against current docs FIRST:



Then:

1. **Match the installed version.** Cross-reference against the version installed in this repo. APIs change across minor versions; do not assume.
2. **Discover provider best practices.** If the task touches a production-sensitive capability, inspect the provider service catalog, official docs, and project docs before choosing an implementation.
3. **Respect explicit direction.** If the user explicitly asks for a specific mechanism, follow it. If project docs clearly mandate a mechanism, follow the project. In both cases, mention the provider-recommended alternative and make the chosen path safe.
4. **Prefer provider-native primitives by default.** If no explicit user/project override exists and the change involves caching, rate limiting, background work, scheduled jobs, shared state, queues, or secrets, use the provider-recommended binding/API. Do not hand-roll an in-memory or polyfill solution that "works" locally but breaks under the provider's execution model — derive the need→native-primitive mapping yourself from this provider's docs.

## Skill Advantage Protocol

Using this skill should produce a meaningfully better result than an unskilled baseline. Apply this loop before and during implementation:

1. **Clarify only when it changes the outcome.** Ask the smallest useful set of questions when the request is ambiguous, preference-heavy, or could change architecture, user-visible behavior, data shape, security posture, analytics, or external side effects. If the safe assumption is obvious, state it and proceed. When asked to surface data that no existing code path captures, state up front the assumption that capture starts now (no backfill) or ask if a backfill source exists — do not silently build net-new storage without surfacing this.
2. **Inspect the nearest real patterns.** Read adjacent files, routes, components, tests, schema, infra, copy, and analytics surfaces before inventing structure. Treat local conventions as the starting point.
3. **Optimize the task's highest-leverage axis.** Identify what would make the result win a review: user-visible correctness, integration quality, accessibility, security, reliability, maintainability, operability, or speed of future change.
4. **Reuse before reimplementing.** Prefer existing components, hooks, helpers, formatting/utility functions, data registries, metadata builders, analytics, pricing, checkout, auth, routing utilities, and API procedures/endpoints/data sources over local one-off clones. Before adding a new API procedure, query, or data fetch, search for one that already returns this data and extend it in place — a surface that fetches data and only logs or partially uses it is a reuse target, not an absent one; never author a parallel endpoint or leave the original orphaned. Before importing for a data fetch, grep the screen for the call it already makes and reuse that exact client/singleton import path and endpoint/procedure name; never create a second client, transport, or parallel endpoint for data an existing call returns, and confirm every imported path and symbol actually exists in the repo before writing it.
5. **Use semantic structures.** Tables, lists, forms, buttons, links, headings, and disclosure controls should use native/project accessible primitives instead of div-only lookalikes.
6. **Prevent drift by construction.** Centralize repeated facts, labels, claims, product defaults, and shared table cells in registries or helpers when multiple surfaces need the same answer.
7. **Synthesize, do not merely comply.** Combine this skill's guidance with repo evidence and the user's goal. When two good approaches exist, borrow the strongest parts of each instead of blindly choosing one.
8. **Check claims against code.** Product copy, docs, and comments must not imply automation, integrations, performance, security, refresh cadence, counts, or data flow that the implementation does not actually provide. Any claim that one component writes, records, updates, calls, or is the source of truth for another is allowed only if the edit performing it is in this same change; before finishing, check each such cross-component claim against the actual edits and downgrade unbacked ones to an explicit TODO or implement them now.
9. **Ship the complete slice.** Include every adjacent artifact needed for the change to be usable and maintainable: wiring, state handling, validation, analytics, tests, docs, migrations, or infra when those surfaces are part of the behavior. When the task shows, displays, or lists user data, deliver the full vertical slice and do not stop at an internal/API/CLI layer: the data-model/schema change AND its migration (a schema change without a migration is incomplete), the path that writes or populates the data, an authenticated endpoint scoped to the current user, and the primary user-facing surface wired through the project's typed data client. Before declaring done, trace one record end-to-end (triggering event → write → read → render); if any hop exists only in a comment or docstring rather than edited code, the slice is NOT done. Shipping only the persistence layer (a schema/migration with no writer, reader, or surface) is an incomplete slice, not a milestone.

## Capability Contract

Use this section when the user prompt touches production risk, even if the prompt does not name this technology explicitly.




Required wiring surfaces:
- provider/runtime configuration discovered during implementation
- nearest typed request/context boundary
- handler/procedure boundary before external side effects

Side-effect barrier:
- Place guards before external APIs, auth mutations, email sends, analytics events, storage writes, and database mutations.


Fallback policy:
- Prefer provider-native/platform-managed primitives by default when no explicit override exists.
- Follow clear user/project overrides, but mention the native alternative and tradeoff.
- Fallbacks must be durable, multi-instance safe, and atomic under concurrency.

Verification rules:
- [error] native-or-explicit-override: Use the provider-native primitive first unless the user/project explicitly overrides it.
- [error] atomic-fallback: Fallback counters must be atomic under concurrency.

## Quick Start

```bash
# Run all tests
dotnet test ADB.slnx

# Run a specific project
dotnet test AdbCore.Tests

# Run a single test by name
dotnet test --filter "FullyQualifiedName~BotExecutorTests.RunAsync_LinearPath"
```

### Existing Fake Pattern (FakeExecutor)

```csharp
// Hand-rolled fake — existing pattern in AdbCore.Tests/Execution/FakeExecutor.cs
internal sealed class FakeExecutor : IActionExecutor
{
    public required string TypeKey { get; init; }
    public Func<ActionExecutionContext, ActionResult> Behavior { get; init; } = _ => ActionResult.Ok("out");
    public int Calls { get; private set; }

    public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(Behavior(context));
    }
}
```

### New Test Class Pattern

```csharp
// new code to add
using Xunit;

namespace AdbCore.Tests.Execution;

public class MyFeatureTests
{
    [Fact]
    public void Does_expected_thing()
    {
        // Arrange
        var sut = new MyFeature();

        // Act
        var result = sut.DoThing();

        // Assert
        Assert.True(result.Success);
    }

    [Theory]
    [InlineData("a", true)]
    [InlineData("", false)]
    public void Handles_various_inputs(string input, bool expected)
    {
        Assert.Equal(expected, MyFeature.IsValid(input));
    }
}
```

## Key Concepts

| Concept | Usage | Notes |
|---------|-------|-------|
| `[Fact]` | Single test case | Most common attribute |
| `[Theory]` + `[InlineData]` | Parameterized tests | Use for boundary/variant coverage |
| `Assert.Equal(expected, actual)` | Value equality | Expected first — xUnit convention |
| `Assert.Contains(substring, message)` | String containment | Used for error message checks |
| `Assert.All(collection, action)` | Bulk assertions | Asserts on every element |
| Hand-rolled fakes | Test doubles | Project standard — no Moq/NSubstitute |
| `required` + `init` | Fake configuration | Compile-time safety for fake setup |

## Common Patterns

### Fake with Configurable Behavior

**When:** Testing code that calls an interface whose side effects you want to control per-test.

```csharp
// Existing pattern from BotCapture.Core.Tests/Fakes.cs
internal sealed class FakeWindowCapture : IWindowCapture
{
    public List<(IntPtr Handle, ScreenCaptureMethod Method)> Calls = new();
    public Func<IntPtr, Bitmap>? Behavior;

    public Bitmap Capture(IntPtr windowHandle, ScreenCaptureMethod method)
    {
        Calls.Add((windowHandle, method));
        return Behavior is not null ? Behavior(windowHandle) : new Bitmap(8, 8);
    }
}
```

### Inline Progress/Callback Adapter

**When:** The production API takes `IProgress<T>` or a delegate but you need to capture calls in a test.

```csharp
// Existing pattern in BotExecutorTests.cs
private sealed class InlineTestProgress : IProgress<ExecutionProgress>
{
    private readonly Action<ExecutionProgress> _h;
    public InlineTestProgress(Action<ExecutionProgress> h) => _h = h;
    public void Report(ExecutionProgress value) => _h(value);
}
```

### Async Test

**When:** Testing `async` methods — xUnit supports `async Task` directly.

```csharp
[Fact]
public async Task RunAsync_succeeds_on_happy_path()
{
    var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);
    Assert.True(result.Success);
}
```

## See Also

- [patterns](references/patterns.md)
- [workflows](references/workflows.md)

## Related Skills

- See the **dotnet** skill for SDK commands, project file configuration, and build
- See the **csharp** skill for language patterns used in test and production code