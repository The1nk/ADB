# C# Patterns Reference

## Contents
- Nullable Reference Types
- Records vs Classes
- Pattern Matching
- Async/Await
- Anti-Patterns

---

## Nullable Reference Types

Strict nullability is enabled project-wide. Every `?` is intentional; every missing `?` means "guaranteed non-null."

**DO — express nullability in the type:**
```csharp
// Nullable return when the result may be absent
public BotAction? FindById(Guid id) =>
    _actions.FirstOrDefault(a => a.Id == id);
```

**DON'T — suppress with `!`:**
```csharp
// BAD — hides a real null risk, fails at runtime instead of compile time
return _actions.FirstOrDefault(a => a.Id == id)!;
```

**Constructor initialization — use `required` or initialize inline:**
```csharp
// new code to add
public class NodeViewModel
{
    public required string ActionId { get; init; }   // compiler enforces caller sets this
    public string Label { get; init; } = string.Empty;
}
```

---

## Records vs Classes

Use **records** for immutable data bags (config, results, messages). Use **classes** for stateful objects (view-models, executors).

```csharp
// new code to add — record for action result (immutable value)
public record ActionResult(bool Success, string? ErrorMessage = null)
{
    public static ActionResult Ok() => new(true);
    public static ActionResult Fail(string msg) => new(false, msg);
}
```

Records give structural equality for free — critical for test assertions without custom comparers.

---

## Pattern Matching

Prefer pattern matching over `is`+cast chains and `switch` with `case` constants.

```csharp
// new code to add
ActionResult result = target switch
{
    WindowTarget wt  => await ExecuteOnWindow(wt, ct),
    AndroidTarget at => await ExecuteOnDevice(at, ct),
    BrowserTarget bt => await ExecuteOnBrowser(bt, ct),
    _                => ActionResult.Fail($"Unsupported target type: {target.GetType().Name}")
};
```

**List patterns (C# 11+) for sequence checks:**
```csharp
// new code to add
if (args is [var first, ..] && first.StartsWith("--bot"))
    LoadBot(first[6..]);
```

---

## Async/Await

All I/O in executors is async. No `.Result` or `.Wait()` — ever.

```csharp
// new code to add
// GOOD — async all the way down
public async Task<byte[]> CaptureAsync(IntPtr hwnd, CancellationToken ct)
{
    await Task.Yield(); // yield to caller before CPU-bound work if needed
    return Win32WindowCapture.Capture(hwnd);
}
```

### WARNING: Sync-over-async

**The Problem:**
```csharp
// BAD — deadlocks in WPF (SynchronizationContext + .Result)
var result = ExecuteAsync(ctx, ct).Result;
```

**Why This Breaks:**
1. WPF has a single-threaded `SynchronizationContext`; `.Result` blocks the UI thread
2. The continuation tries to resume on the same thread → deadlock
3. No exception — just a frozen UI

**The Fix:**
```csharp
// GOOD
var result = await ExecuteAsync(ctx, ct);
```

---

## Anti-Patterns

### WARNING: Mutable static state in executors

**The Problem:**
```csharp
// BAD — shared across concurrent bot runs
public class ClickActionExecutor : IActionExecutor
{
    private static int _clickCount; // global mutable state
}
```

**Why This Breaks:** Multiple bots running in parallel corrupt each other's counters. Pass transient state through `BotExecutionContext.Variables`.

**The Fix:**
```csharp
// new code to add
ctx.Variables["click_count"] = (int)ctx.Variables.GetValueOrDefault("click_count", 0) + 1;
```

### WARNING: Catching `Exception` without rethrowing

```csharp
// BAD — silently swallows programming errors, cancellation, OOM
try { await action.ExecuteAsync(ctx, ct); }
catch (Exception) { /* ignore */ }
```

Catch the narrowest type needed (`IOException`, `TimeoutException`). Let `OperationCanceledException` propagate so `CancellationToken` works correctly.