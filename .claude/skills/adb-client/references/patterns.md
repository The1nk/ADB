# ADB Client Patterns Reference

## Contents
- Interface Boundary
- Target Resolution
- Action Implementation
- Anti-Patterns
- Error Handling

---

## Interface Boundary

All Android communication is routed through `IAdbDevices` and `IAndroidDevice` in `AdbCore/Android/`. The Sharp ADB Client library is an implementation detail — nothing outside `AdbCore/Android/` should reference it directly.

```csharp
// GOOD — depend on the abstraction
public class TapAction : IActionExecutor
{
    public async Task<ActionResult> ExecuteAsync(BotExecutionContext ctx, CancellationToken ct)
    {
        var device = ctx.GetTarget<IAndroidDevice>("Main");
        await device.TapAsync(x, y, ct);
        return ActionResult.Success();
    }
}

// BAD — leaks Sharp ADB Client into action layer
using AdvancedSharpAdbClient; // NEVER in Actions/
var client = new AdbClient();
```

**Why:** The abstraction allows swapping the ADB backend in tests (fake device) and in future (e.g., USB direct). Leaking the concrete library makes every action untestable without a real device.

---

## Target Resolution

Android targets use the `serial:` prefix (e.g., `serial:emulator-5554`). Resolution lives in `AdbCore/Targets/`.

```csharp
// GOOD — fail immediately with a clear message
var device = ctx.GetTarget<IAndroidDevice>(targetName)
    ?? return ActionResult.Failure($"Target '{targetName}' is not an Android device or is disconnected.");

// BAD — silent null dereference
var device = ctx.GetTarget<IAndroidDevice>(targetName);
device.TapAsync(x, y, ct); // NullReferenceException if offline
```

**Why:** ADB devices disconnect mid-run (USB cable, emulator crash). Failing fast with a clear message is far more debuggable than a null dereference buried in a stack trace.

---

## Action Implementation

Every Android action in `AdbCore/Actions/BuiltIn/Android/` follows the same pattern:

```csharp
// new code to add — canonical Android action skeleton
public sealed class LaunchAppAction : IActionExecutor
{
    // 1. Read config fields via ctx.Config (never use magic strings inside Execute)
    // 2. Resolve target — fail fast if missing or wrong type
    // 3. Execute async operation with ct
    // 4. Return ActionResult.Success() or ActionResult.Failure(reason)

    public async Task<ActionResult> ExecuteAsync(BotExecutionContext ctx, CancellationToken ct)
    {
        var packageName = ctx.Config.GetRequired<string>("PackageName");
        var device = ctx.GetTarget<IAndroidDevice>("Main")
            ?? return ActionResult.Failure("Target 'Main' is not an Android device.");

        await device.LaunchAppAsync(packageName, ct);
        return ActionResult.Success();
    }
}
```

---

## Anti-Patterns

### WARNING: Static ADB State

**The Problem:**
```csharp
// BAD — static mutable state in executor
public class ScreenshotAction : IActionExecutor
{
    private static AdbClient _client = new(); // shared across all bot runs
}
```

**Why This Breaks:**
1. Concurrent bot runs share one client instance — race conditions on device enumeration.
2. If the ADB server restarts, the static client never reconnects.
3. Tests cannot isolate device state.

**The Fix:**
```csharp
// GOOD — receive device through BotExecutionContext
var device = ctx.GetTarget<IAndroidDevice>("Main");
```

---

### WARNING: Raw Shell Commands for Typed Operations

**The Problem:**
```csharp
// BAD — shelling out when a typed API exists
await device.ExecuteShellAsync("input tap 100 200", ct);
```

**Why This Breaks:**
1. Shell string parsing is fragile — coordinate injection if values come from user config.
2. No return value validation; shell exits 0 even when the input daemon crashes.
3. Typed APIs handle DPI scaling and coordinate mapping correctly.

**The Fix:**
```csharp
// GOOD — use the typed method
await device.TapAsync(100, 200, ct);
```

---

## Error Handling

ADB operations fail for reasons outside the bot's control (device offline, USB flap, emulator OOM). Always:

1. Treat `null` target as an immediate `ActionResult.Failure` — do not retry.
2. Wrap async ADB calls in try/catch for `AdbException` and surface the message.
3. Never swallow exceptions silently — the BotExecutor needs failure signals to stop or branch.

```csharp
// new code to add
try
{
    await device.TapAsync(x, y, ct);
}
catch (AdbException ex)
{
    return ActionResult.Failure($"ADB error during tap: {ex.Message}");
}
```

See the **xunit** skill for testing these failure paths with a `FakeAndroidDevice`.