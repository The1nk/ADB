# M7a — Android Action Category Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the Android action category end-to-end: an `IAndroidDevice` adapter (over AdvancedSharpAdbClient + the ADB server), an `AndroidTargetBinder` that resolves `serial:<device>` to a bound device handle, six Android actions, and a live `adb devices` dropdown in the Test Run target picker.

**Architecture:** The bound `IAndroidDevice` adapter is stored as `ResolvedTarget.Handle` for `AndroidDevice` targets; action executors read it from the handle (no constructor injection) and call it. Tested units (actions, base, selector) use only the `IAndroidDevice` interface with a fake. The concrete AdvancedSharpAdbClient adapter + the binder are thin and verified live (a real `adb` device).

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), AdvancedSharpAdbClient, xUnit, WPF. Per `Docs/Specs/2026-06-03-m7-android-browser-design.md` §2–3.

---

## File Structure

**AdbCore (new):**
- `Android/AdbDeviceInfo.cs` — `readonly record struct AdbDeviceInfo(string Serial, string State)`.
- `Android/IAndroidDevice.cs` — the per-device operation adapter (the handle type).
- `Android/IAdbDevices.cs` — `List()` connected devices.
- `Android/AdbSelector.cs` — `serial:<device>` parse (tested).
- `Android/AdvancedSharpAdbDevice.cs`, `Android/AdvancedSharpAdbDevices.cs` — concrete adapters (live).
- `Actions/BuiltIn/Android/AndroidActionBase.cs` + `TapAction.cs`, `SwipeAction.cs`, `PressBackAction.cs`,
  `LaunchAppAction.cs`, `InstallApkAction.cs`, `AndroidScreenshotAction.cs`.

**AdbCore (modified):** `Actions/BuiltIn/BuiltInActions.cs` — register the six.

**BotRunner (new/modified):** `AndroidTargetBinder.cs`; `RunnerApp.cs` — wire the binder.

**BotBuilder (modified):** `Core/Integration/TargetSelectionRow.cs` (`IsAndroid`); `TargetPickerDialog.xaml(.cs)` — Android dropdown.

**Tests (AdbCore.Tests):** `Actions/BuiltIn/Android/*Tests.cs`, `Android/AdbSelectorTests.cs`, a
`FakeAndroidDevice`, and the registration-count bump in `Actions/BuiltIn/BuiltInActionsTests.cs`.

---

## Task 1: Android package + core types + selector

**Files:**
- Modify: `AdbCore/AdbCore.csproj`
- Create: `AdbCore/Android/AdbDeviceInfo.cs`, `AdbCore/Android/IAndroidDevice.cs`, `AdbCore/Android/IAdbDevices.cs`, `AdbCore/Android/AdbSelector.cs`
- Test: `AdbCore.Tests/Android/AdbSelectorTests.cs`

- [ ] **Step 1: Add the AdvancedSharpAdbClient package.** Run:

```bash
dotnet add AdbCore/AdbCore.csproj package AdvancedSharpAdbClient
```

Expected: the latest stable version is added to `AdbCore.csproj` and restores. (It's a managed library; it
talks to the ADB *server* over TCP — no native runtime dependency at build time.)

- [ ] **Step 2: Write the failing selector test** — `AdbCore.Tests/Android/AdbSelectorTests.cs`:

```csharp
using AdbCore.Android;

namespace AdbCore.Tests.Android;

public class AdbSelectorTests
{
    [Theory]
    [InlineData("serial:emulator-5554", "emulator-5554")]
    [InlineData("SERIAL:ABC123", "ABC123")]
    public void ParseSerial_ReturnsSerial(string selector, string expected)
        => Assert.Equal(expected, AdbSelector.ParseSerial(selector));

    [Theory]
    [InlineData("process:BlueStacks")]
    [InlineData("serial:")]
    [InlineData("emulator-5554")]
    public void ParseSerial_NonSerial_ReturnsNull(string selector)
        => Assert.Null(AdbSelector.ParseSerial(selector));
}
```

- [ ] **Step 3: Run to verify it fails** — `dotnet test AdbCore.Tests/AdbCore.Tests.csproj --filter "FullyQualifiedName~AdbSelectorTests"` → FAIL (type missing).

- [ ] **Step 4: Create the core types.**

`AdbCore/Android/AdbDeviceInfo.cs`:

```csharp
namespace AdbCore.Android;

/// <summary>A connected ADB device discovered by <see cref="IAdbDevices"/>.</summary>
public readonly record struct AdbDeviceInfo(string Serial, string State);
```

`AdbCore/Android/IAndroidDevice.cs`:

```csharp
namespace AdbCore.Android;

/// <summary>Operations on one connected Android device, bound to it over the ADB server. Stored as the
/// <c>ResolvedTarget.Handle</c> for AndroidDevice targets; the Android actions call it.</summary>
public interface IAndroidDevice
{
    void Tap(int x, int y);
    void Swipe(int x1, int y1, int x2, int y2, int durationMs);

    /// <summary>Captures the screen as PNG bytes.</summary>
    byte[] Screenshot();

    void PressBack();

    /// <summary>Launches an app by package name (its launcher activity).</summary>
    void LaunchApp(string package);

    void InstallApk(string apkPath);
}
```

`AdbCore/Android/IAdbDevices.cs`:

```csharp
namespace AdbCore.Android;

/// <summary>Enumerates the devices currently visible to the ADB server (for the target picker and
/// selector resolution).</summary>
public interface IAdbDevices
{
    IReadOnlyList<AdbDeviceInfo> List();
}
```

`AdbCore/Android/AdbSelector.cs`:

```csharp
namespace AdbCore.Android;

/// <summary>Parses an Android device target selector of the form <c>serial:&lt;device&gt;</c>.</summary>
public static class AdbSelector
{
    private const string Prefix = "serial:";

    /// <summary>The device serial from a <c>serial:&lt;device&gt;</c> selector, or null when the selector
    /// isn't a (non-empty) serial selector.</summary>
    public static string? ParseSerial(string selector)
        => selector.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) && selector.Length > Prefix.Length
            ? selector[Prefix.Length..]
            : null;
}
```

- [ ] **Step 5: Run to verify it passes** — same filter → PASS (5 cases). Then `dotnet build AdbCore/AdbCore.csproj -c Debug --nologo` → 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add AdbCore/AdbCore.csproj AdbCore/Android/AdbDeviceInfo.cs AdbCore/Android/IAndroidDevice.cs AdbCore/Android/IAdbDevices.cs AdbCore/Android/AdbSelector.cs AdbCore.Tests/Android/AdbSelectorTests.cs
git commit -m "feat(android): add AdvancedSharpAdbClient + Android adapter interfaces + AdbSelector"
```

---

## Task 2: AndroidActionBase + Tap / Swipe / Press Back

**Files:**
- Create: `AdbCore/Actions/BuiltIn/Android/AndroidActionBase.cs`, `TapAction.cs`, `SwipeAction.cs`, `PressBackAction.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/Android/FakeAndroidDevice.cs`, `AndroidInputActionTests.cs`

- [ ] **Step 1: Write the fake + failing tests.**

`AdbCore.Tests/Actions/BuiltIn/Android/FakeAndroidDevice.cs`:

```csharp
using System.Collections.Generic;
using AdbCore.Android;

namespace AdbCore.Tests.Actions.BuiltIn.Android;

internal sealed class FakeAndroidDevice : IAndroidDevice
{
    public List<string> Calls { get; } = new();
    public byte[] ScreenshotBytes { get; set; } = System.Array.Empty<byte>();

    public void Tap(int x, int y) => Calls.Add($"tap {x} {y}");
    public void Swipe(int x1, int y1, int x2, int y2, int durationMs) => Calls.Add($"swipe {x1} {y1} {x2} {y2} {durationMs}");
    public byte[] Screenshot() { Calls.Add("screenshot"); return ScreenshotBytes; }
    public void PressBack() => Calls.Add("back");
    public void LaunchApp(string package) => Calls.Add($"launch {package}");
    public void InstallApk(string apkPath) => Calls.Add($"install {apkPath}");
}
```

`AdbCore.Tests/Actions/BuiltIn/Android/AndroidInputActionTests.cs`:

```csharp
using AdbCore.Actions.BuiltIn.Android;
using AdbCore.Execution;
using AdbCore.Models;

namespace AdbCore.Tests.Actions.BuiltIn.Android;

public class AndroidInputActionTests
{
    private static (ActionExecutionContext ctx, FakeAndroidDevice dev) WithDevice(BotAction action)
    {
        var dev = new FakeAndroidDevice();
        var ctx = new BotExecutionContext();
        var id = action.TargetId ?? Guid.NewGuid();
        action.TargetId = id;
        ctx.Targets[id] = new ResolvedTarget { Type = BotTargetType.AndroidDevice, Selector = "serial:x", Handle = dev };
        return (new ActionExecutionContext(action, ctx, _ => { }), dev);
    }

    [Fact]
    public async Task Tap_CallsDeviceWithCoords()
    {
        var action = new BotAction { Config = { ["x"] = 120, ["y"] = 240 } };
        var (ctx, dev) = WithDevice(action);

        var r = await new TapAction().ExecuteAsync(ctx, default);

        Assert.True(r.Success);
        Assert.Equal("onSuccess", r.OutputPort);
        Assert.Equal("tap 120 240", dev.Calls.Single());
    }

    [Fact]
    public async Task Swipe_CallsDeviceWithAllArgs()
    {
        var action = new BotAction { Config = { ["x1"] = 10, ["y1"] = 20, ["x2"] = 30, ["y2"] = 40, ["durationMs"] = 250 } };
        var (ctx, dev) = WithDevice(action);

        await new SwipeAction().ExecuteAsync(ctx, default);

        Assert.Equal("swipe 10 20 30 40 250", dev.Calls.Single());
    }

    [Fact]
    public async Task PressBack_CallsDevice()
    {
        var action = new BotAction();
        var (ctx, dev) = WithDevice(action);

        await new PressBackAction().ExecuteAsync(ctx, default);

        Assert.Equal("back", dev.Calls.Single());
    }

    [Fact]
    public async Task NoAndroidDeviceBound_Fails()
    {
        var ctx = new BotExecutionContext(); // no targets
        var exec = new ActionExecutionContext(new BotAction { Config = { ["x"] = 1, ["y"] = 1 } }, ctx, _ => { });

        var r = await new TapAction().ExecuteAsync(exec, default);

        Assert.False(r.Success);
        Assert.Contains("Android", r.ErrorMessage);
    }
}
```

- [ ] **Step 2: Run to verify they fail** — `dotnet test AdbCore.Tests/AdbCore.Tests.csproj --filter "FullyQualifiedName~AndroidInputActionTests"` → FAIL.

- [ ] **Step 3: Create `AndroidActionBase`** — `AdbCore/Actions/BuiltIn/Android/AndroidActionBase.cs`:

```csharp
using System.Linq;
using AdbCore.Android;
using AdbCore.Execution;
using AdbCore.Models;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Shared base for Android actions: resolves the action's target to the bound
/// <see cref="IAndroidDevice"/> handle, and exposes the standard success/failure ports.</summary>
public abstract class AndroidActionBase : IActionDefinition, IActionExecutor
{
    public const string SuccessPort = "onSuccess";
    public const string FailurePort = "onFailure";

    public abstract string TypeKey { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }
    public string Category => "Android";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public virtual List<PortDefinition> OutputPorts { get; } = new()
    {
        new PortDefinition { Name = SuccessPort, Label = "On Success" },
        new PortDefinition { Name = FailurePort, Label = "On Failure" },
    };
    public abstract List<ConfigField> ConfigFields { get; }
    public virtual bool SupportsRetry => false;

    public abstract Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct);

    /// <summary>The bound Android device for this action's target (explicit TargetId, or the sole target
    /// when unset); null when the target isn't a bound Android device.</summary>
    protected static IAndroidDevice? ResolveDevice(ActionExecutionContext context)
    {
        var targets = context.Context.Targets;
        ResolvedTarget? target = context.Action.TargetId is Guid id
            ? targets.TryGetValue(id, out var t) ? t : null
            : targets.Count == 1 ? targets.Values.First() : null;
        return target?.Handle as IAndroidDevice;
    }

    /// <summary>Standard "no device" failure message.</summary>
    protected ActionResult RequiresDevice() => ActionResult.Fail($"{DisplayName} requires a connected Android device target.");
}
```

- [ ] **Step 4: Create the three actions.**

`AdbCore/Actions/BuiltIn/Android/TapAction.cs`:

```csharp
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Taps the Android screen at (x, y).</summary>
public sealed class TapAction : AndroidActionBase
{
    public override string TypeKey => "android.tap";
    public override string DisplayName => "Tap";
    public override string Description => "Taps the device screen at the given coordinates.";

    public override List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = "x", Label = "X", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField { Key = "y", Label = "Y", Type = ConfigFieldType.Number, DefaultValue = 0 },
    };

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return Task.FromResult(RequiresDevice());
        }

        device.Tap(
            ConfigValues.GetInt(context.Action.Config, "x"),
            ConfigValues.GetInt(context.Action.Config, "y"));
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
```

`AdbCore/Actions/BuiltIn/Android/SwipeAction.cs`:

```csharp
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Swipes from (x1, y1) to (x2, y2) over a duration.</summary>
public sealed class SwipeAction : AndroidActionBase
{
    public override string TypeKey => "android.swipe";
    public override string DisplayName => "Swipe";
    public override string Description => "Swipes between two points over the given duration.";

    public override List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = "x1", Label = "From X", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField { Key = "y1", Label = "From Y", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField { Key = "x2", Label = "To X", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField { Key = "y2", Label = "To Y", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField { Key = "durationMs", Label = "Duration (ms)", Type = ConfigFieldType.Number, DefaultValue = 300 },
    };

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return Task.FromResult(RequiresDevice());
        }

        var c = context.Action.Config;
        device.Swipe(
            ConfigValues.GetInt(c, "x1"), ConfigValues.GetInt(c, "y1"),
            ConfigValues.GetInt(c, "x2"), ConfigValues.GetInt(c, "y2"),
            ConfigValues.GetInt(c, "durationMs", 300));
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
```

`AdbCore/Actions/BuiltIn/Android/PressBackAction.cs`:

```csharp
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Presses the Android Back button.</summary>
public sealed class PressBackAction : AndroidActionBase
{
    public override string TypeKey => "android.pressBack";
    public override string DisplayName => "Press Back";
    public override string Description => "Sends the Back key to the device.";
    public override List<ConfigField> ConfigFields { get; } = new();

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return Task.FromResult(RequiresDevice());
        }

        device.PressBack();
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
```

- [ ] **Step 5: Register the three** in `AdbCore/Actions/BuiltIn/BuiltInActions.cs`. After the Screen
  registrations (the `ScreenshotAction` line), add a new block (add `using AdbCore.Actions.BuiltIn.Android;`
  at the top if not present):

```csharp
        // Android (handle-based — the bound IAndroidDevice is the ResolvedTarget handle; no injection).
        Add(new TapAction(), definitions, executors);
        Add(new SwipeAction(), definitions, executors);
        Add(new PressBackAction(), definitions, executors);
```

- [ ] **Step 6: Run to verify they pass** — filter `~AndroidInputActionTests` → PASS (4 tests). (The
  registration-count test will now fail — it's fixed in Task 3 once all six are registered. If you run the
  full suite now, expect only `BuiltInActionsTests` count assertions to fail.)

- [ ] **Step 7: Commit**

```bash
git add AdbCore/Actions/BuiltIn/Android/AndroidActionBase.cs AdbCore/Actions/BuiltIn/Android/TapAction.cs AdbCore/Actions/BuiltIn/Android/SwipeAction.cs AdbCore/Actions/BuiltIn/Android/PressBackAction.cs AdbCore/Actions/BuiltIn/BuiltInActions.cs AdbCore.Tests/Actions/BuiltIn/Android/FakeAndroidDevice.cs AdbCore.Tests/Actions/BuiltIn/Android/AndroidInputActionTests.cs
git commit -m "feat(android): AndroidActionBase + Tap/Swipe/Press Back actions"
```

---

## Task 3: Launch App / Install APK / Screenshot + registration counts

**Files:**
- Create: `AdbCore/Actions/BuiltIn/Android/LaunchAppAction.cs`, `InstallApkAction.cs`, `AndroidScreenshotAction.cs`
- Modify: `AdbCore/Actions/BuiltIn/BuiltInActions.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/Android/AndroidAppActionTests.cs`; update `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs`

- [ ] **Step 1: Write the failing tests** — `AdbCore.Tests/Actions/BuiltIn/Android/AndroidAppActionTests.cs`:

```csharp
using System.IO;
using AdbCore.Actions.BuiltIn.Android;
using AdbCore.Execution;
using AdbCore.Models;

namespace AdbCore.Tests.Actions.BuiltIn.Android;

public class AndroidAppActionTests
{
    private static (ActionExecutionContext ctx, FakeAndroidDevice dev) WithDevice(BotAction action)
    {
        var dev = new FakeAndroidDevice();
        var ctx = new BotExecutionContext();
        var id = action.TargetId ?? Guid.NewGuid();
        action.TargetId = id;
        ctx.Targets[id] = new ResolvedTarget { Type = BotTargetType.AndroidDevice, Selector = "serial:x", Handle = dev };
        return (new ActionExecutionContext(action, ctx, _ => { }), dev);
    }

    [Fact]
    public async Task LaunchApp_CallsDeviceWithPackage()
    {
        var action = new BotAction { Config = { ["package"] = "com.example.app" } };
        var (ctx, dev) = WithDevice(action);

        await new LaunchAppAction().ExecuteAsync(ctx, default);

        Assert.Equal("launch com.example.app", dev.Calls.Single());
    }

    [Fact]
    public async Task InstallApk_CallsDeviceWithPath()
    {
        var action = new BotAction { Config = { ["apkPath"] = @"C:\app.apk" } };
        var (ctx, dev) = WithDevice(action);

        await new InstallApkAction().ExecuteAsync(ctx, default);

        Assert.Equal(@"install C:\app.apk", dev.Calls.Single());
    }

    [Fact]
    public async Task Screenshot_WritesDeviceBytesToPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"adbshot_{Guid.NewGuid():N}.png");
        var action = new BotAction { Config = { ["outputPath"] = path } };
        var (ctx, dev) = WithDevice(action);
        dev.ScreenshotBytes = new byte[] { 1, 2, 3, 4 };
        try
        {
            var r = await new AndroidScreenshotAction().ExecuteAsync(ctx, default);

            Assert.True(r.Success);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Screenshot_NoPath_Fails()
    {
        var action = new BotAction { Config = { } };
        var (ctx, _) = WithDevice(action);

        var r = await new AndroidScreenshotAction().ExecuteAsync(ctx, default);

        Assert.False(r.Success);
    }
}
```

- [ ] **Step 2: Run to verify they fail** — filter `~AndroidAppActionTests` → FAIL.

- [ ] **Step 3: Create the three actions.**

`AdbCore/Actions/BuiltIn/Android/LaunchAppAction.cs`:

```csharp
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Launches an app by package name.</summary>
public sealed class LaunchAppAction : AndroidActionBase
{
    public override string TypeKey => "android.launchApp";
    public override string DisplayName => "Launch App";
    public override string Description => "Launches an installed app by its package name.";

    public override List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = "package", Label = "Package Name", Type = ConfigFieldType.String, DefaultValue = "" },
    };

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return Task.FromResult(RequiresDevice());
        }

        var package = ConfigValues.GetString(context.Action.Config, "package");
        if (string.IsNullOrWhiteSpace(package))
        {
            return Task.FromResult(ActionResult.Fail("Launch App: a package name is required."));
        }

        device.LaunchApp(package);
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
```

`AdbCore/Actions/BuiltIn/Android/InstallApkAction.cs`:

```csharp
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Installs an APK file onto the device.</summary>
public sealed class InstallApkAction : AndroidActionBase
{
    public override string TypeKey => "android.installApk";
    public override string DisplayName => "Install APK";
    public override string Description => "Installs an APK file onto the device.";

    public override List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = "apkPath", Label = "APK File", Type = ConfigFieldType.FilePath, DefaultValue = "" },
    };

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return Task.FromResult(RequiresDevice());
        }

        var apkPath = ConfigValues.GetString(context.Action.Config, "apkPath");
        if (string.IsNullOrWhiteSpace(apkPath))
        {
            return Task.FromResult(ActionResult.Fail("Install APK: an APK file path is required."));
        }

        device.InstallApk(apkPath);
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
```

`AdbCore/Actions/BuiltIn/Android/AndroidScreenshotAction.cs`:

```csharp
using System.IO;
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Captures the device screen and saves it as a PNG.</summary>
public sealed class AndroidScreenshotAction : AndroidActionBase
{
    public override string TypeKey => "android.screenshot";
    public override string DisplayName => "Screenshot (Android)";
    public override string Description => "Captures the device screen and saves it to a PNG file.";

    public override List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = "outputPath", Label = "Save To", Type = ConfigFieldType.FilePath, DefaultValue = "" },
    };

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return Task.FromResult(RequiresDevice());
        }

        var path = ConfigValues.GetString(context.Action.Config, "outputPath");
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(ActionResult.Fail("Screenshot (Android): an output file path is required."));
        }

        File.WriteAllBytes(path, device.Screenshot());
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
```

- [ ] **Step 4: Register the three** in `BuiltInActions.cs`, after the `PressBackAction` line from Task 2:

```csharp
        Add(new LaunchAppAction(), definitions, executors);
        Add(new InstallApkAction(), definitions, executors);
        Add(new AndroidScreenshotAction(), definitions, executors);
```

- [ ] **Step 5: Update the registration-count assertions.** In `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs`,
  the assertions currently read `Assert.Equal(20, defs.Count);` and `Assert.Equal(17, execs.Count);`. The
  six Android actions add six definitions and six executors, so change them to:

```csharp
        Assert.Equal(26, defs.Count);
        Assert.Equal(23, execs.Count);
```

- [ ] **Step 6: Run the Android + registration tests** — `dotnet test AdbCore.Tests/AdbCore.Tests.csproj --filter "FullyQualifiedName~AndroidAppActionTests|FullyQualifiedName~BuiltInActionsTests"` → PASS. Then run the FULL AdbCore.Tests project and fix any OTHER count assertion that now fails (e.g. a palette/category test in `BotBuilder.Core.Tests` may count action categories — if a test asserts a category or action count, update it to include the new `Android` category / six actions). Re-run until green.

- [ ] **Step 7: Commit**

```bash
git add AdbCore/Actions/BuiltIn/Android/LaunchAppAction.cs AdbCore/Actions/BuiltIn/Android/InstallApkAction.cs AdbCore/Actions/BuiltIn/Android/AndroidScreenshotAction.cs AdbCore/Actions/BuiltIn/BuiltInActions.cs AdbCore.Tests/Actions/BuiltIn/Android/AndroidAppActionTests.cs AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs
git commit -m "feat(android): Launch App / Install APK / Screenshot actions (Android category complete)"
```

---

## Task 4: AdvancedSharpAdbClient adapters + AndroidTargetBinder (live)

Implements the concrete adapters against the installed AdvancedSharpAdbClient and binds Android targets at
run start. **No unit tests** (needs a real device); verified live by the user. The ADB **shell commands**
below are stable; only the C# calls that *execute* them depend on the package version — **read the
installed AdvancedSharpAdbClient public API and adapt the calls** (the package was added in Task 1). If the
API differs so much you can't map these operations, STOP and report BLOCKED with what you found.

**Files:**
- Create: `AdbCore/Android/AdvancedSharpAdbDevice.cs`, `AdbCore/Android/AdvancedSharpAdbDevices.cs`
- Create: `BotRunner/AndroidTargetBinder.cs`
- Modify: `BotRunner/RunnerApp.cs`

- [ ] **Step 1: Implement the concrete device adapter** — `AdbCore/Android/AdvancedSharpAdbDevice.cs`.
  It wraps an `AdbClient` + the target `DeviceData`. The intended operations (map each to the package's
  "run a shell command" / install API):

  - `Tap(x,y)` → shell `input tap {x} {y}`
  - `Swipe(...)` → shell `input swipe {x1} {y1} {x2} {y2} {durationMs}`
  - `PressBack()` → shell `input keyevent 4`
  - `LaunchApp(package)` → shell `monkey -p {package} -c android.intent.category.LAUNCHER 1`
  - `Screenshot()` → shell `screencap -p` capturing **raw stdout bytes** as the PNG (or the package's
    framebuffer API), returned as `byte[]`
  - `InstallApk(apkPath)` → the package's `Install(device, File.OpenRead(apkPath))` API

  Sketch (adjust method names/signatures to the installed package — e.g. `ExecuteRemoteCommand` /
  `ExecuteServerCommand` / async variants):

```csharp
using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;

namespace AdbCore.Android;

/// <summary>An <see cref="IAndroidDevice"/> backed by AdvancedSharpAdbClient talking to the ADB server.
/// Thin adapter over shell commands + the install API; verified live (a real device).</summary>
public sealed class AdvancedSharpAdbDevice : IAndroidDevice
{
    private readonly IAdbClient _client;
    private readonly DeviceData _device;

    public AdvancedSharpAdbDevice(IAdbClient client, DeviceData device)
    {
        _client = client;
        _device = device;
    }

    private void Shell(string command) => _client.ExecuteRemoteCommand(command, _device);

    public void Tap(int x, int y) => Shell($"input tap {x} {y}");
    public void Swipe(int x1, int y1, int x2, int y2, int durationMs) => Shell($"input swipe {x1} {y1} {x2} {y2} {durationMs}");
    public void PressBack() => Shell("input keyevent 4");
    public void LaunchApp(string package) => Shell($"monkey -p {package} -c android.intent.category.LAUNCHER 1");

    public byte[] Screenshot()
    {
        // Prefer the package's framebuffer/screenshot API if available; otherwise capture `screencap -p`
        // stdout bytes. Return PNG bytes.
        var image = _client.GetFrameBuffer(_device);
        using var ms = new MemoryStream();
        image.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }

    public void InstallApk(string apkPath)
    {
        using var apk = File.OpenRead(apkPath);
        _client.Install(_device, apk);
    }
}
```

- [ ] **Step 2: Implement the devices enumerator** — `AdbCore/Android/AdvancedSharpAdbDevices.cs`:

```csharp
using AdvancedSharpAdbClient;

namespace AdbCore.Android;

/// <summary>Lists devices visible to the ADB server (starts the server if needed). Verified live.</summary>
public sealed class AdvancedSharpAdbDevices : IAdbDevices
{
    public IReadOnlyList<AdbDeviceInfo> List()
    {
        // Ensure the ADB server is running, then enumerate. Adapt to the installed API
        // (e.g. AdbServer.Instance.StartServer / new AdbClient().GetDevices()).
        AdbServer.Instance.StartServer(null, restartServerIfNewer: false);
        var client = new AdbClient();
        return client.GetDevices()
            .Select(d => new AdbDeviceInfo(d.Serial, d.State.ToString()))
            .ToList();
    }
}
```

- [ ] **Step 3: Create `AndroidTargetBinder`** — `BotRunner/AndroidTargetBinder.cs` (mirrors
  `WindowTargetBinder`): for each `AndroidDevice` target, parse the `serial:` selector, find the device,
  and store an `AdvancedSharpAdbDevice` as the handle. An unreachable server / unknown device is a
  `CommandLineException` (exit 2) with a README-pointing message.

```csharp
using AdbCore.Android;
using AdbCore.Execution;
using AdbCore.Models;
using AdvancedSharpAdbClient;

namespace BotRunner;

/// <summary>At run start, resolves each Android target's <c>serial:</c> selector to a bound
/// <see cref="IAndroidDevice"/> handle. A missing server/device is a CLI usage error (exit 2).</summary>
public static class AndroidTargetBinder
{
    public static void Bind(IReadOnlyDictionary<Guid, ResolvedTarget> targets)
    {
        var hasAndroid = targets.Values.Any(t => t.Type == BotTargetType.AndroidDevice);
        if (!hasAndroid)
        {
            return;
        }

        AdbClient client;
        try
        {
            AdbServer.Instance.StartServer(null, restartServerIfNewer: false);
            client = new AdbClient();
        }
        catch (Exception ex)
        {
            throw new CommandLineException(
                $"Could not reach the ADB server (is `adb` installed and a device connected? see README): {ex.Message}");
        }

        var devices = client.GetDevices().ToList();

        foreach (var target in targets.Values.Where(t => t.Type == BotTargetType.AndroidDevice))
        {
            var serial = AdbSelector.ParseSerial(target.Selector)
                ?? throw new CommandLineException(
                    $"Android target selector '{target.Selector}' must be 'serial:<device>'.");

            var device = devices.FirstOrDefault(d => d.Serial == serial);
            if (device.Serial is null)
            {
                throw new CommandLineException(
                    $"No connected Android device with serial '{serial}'. Run `adb devices`; see README.");
            }

            target.Handle = new AdvancedSharpAdbDevice(client, device);
        }
    }
}
```

- [ ] **Step 4: Wire it in `RunnerApp.RunAsync`.** In `BotRunner/RunnerApp.cs`, immediately after the line
  `WindowTargetBinder.Bind(resolvedTargets, new Win32WindowResolver());`, add:

```csharp
        AndroidTargetBinder.Bind(resolvedTargets);
```

- [ ] **Step 5: Build** — `dotnet build ADB.slnx -c Debug --nologo`. Expected: `Build succeeded`, 0 errors.
  **If AdvancedSharpAdbClient's API doesn't match the sketch** (method names, `DeviceData` shape,
  `GetFrameBuffer`/`Install` signatures), adjust the adapter/binder calls to the installed API until it
  builds; the IAndroidDevice/IAdbDevices *interfaces* and all of Tasks 1–3 must remain unchanged. If you
  cannot map an operation, STOP and report BLOCKED with the package's actual API surface.

- [ ] **Step 6: Commit**

```bash
git add AdbCore/Android/AdvancedSharpAdbDevice.cs AdbCore/Android/AdvancedSharpAdbDevices.cs BotRunner/AndroidTargetBinder.cs BotRunner/RunnerApp.cs
git commit -m "feat(android): AdvancedSharpAdbClient adapters + AndroidTargetBinder (run-start binding)"
```

---

## Task 5: Target-picker Android dropdown (WPF, visual)

Adds a live device dropdown for Android targets in the M8a Test Run target picker. No unit tests.

**Files:**
- Modify: `BotBuilder.Core/Integration/TargetSelectionRow.cs`, `BotBuilder/TargetPickerDialog.xaml`, `BotBuilder/TargetPickerDialog.xaml.cs`

- [ ] **Step 1: Add `IsAndroid` to `TargetSelectionRow`.** In `BotBuilder.Core/Integration/TargetSelectionRow.cs`,
  next to the existing `IsWindow`, add:

```csharp
    public bool IsAndroid => Type == BotTargetType.AndroidDevice;
```

- [ ] **Step 2: Add an Android dropdown to `TargetPickerDialog.xaml`.** In the row `DataTemplate` (the Grid
  with the Name/Type, the Selector TextBox, and the Window-only ComboBox), add a second ComboBox in the
  same `Grid.Column="2"` slot, visible only for Android rows. Replace the single Window ComboBox with the
  two stacked options (only one is visible per row):

```xml
                        <StackPanel Grid.Column="2" Orientation="Horizontal">
                            <ComboBox Width="180" Margin="8,0,0,0" VerticalAlignment="Center"
                                      Visibility="{Binding IsWindow, Converter={StaticResource BoolToVis}}"
                                      Tag="{Binding}" Loaded="OnWindowComboLoaded" SelectionChanged="OnWindowChosen"
                                      DisplayMemberPath="Display" />
                            <ComboBox Width="180" Margin="8,0,0,0" VerticalAlignment="Center"
                                      Visibility="{Binding IsAndroid, Converter={StaticResource BoolToVis}}"
                                      Tag="{Binding}" Loaded="OnAndroidComboLoaded" SelectionChanged="OnAndroidChosen"
                                      DisplayMemberPath="Display" />
                        </StackPanel>
```

  (Keep the Name/Type StackPanel in column 0 and the Selector TextBox in column 1 exactly as they are.)

- [ ] **Step 3: Add the Android combo handlers to `TargetPickerDialog.xaml.cs`.** Add a devices field and
  the two handlers (keep the existing Window handlers + `WindowChoice`):

```csharp
    private readonly AdbCore.Android.IAdbDevices _adbDevices = new AdbCore.Android.AdvancedSharpAdbDevices();

    private sealed record DeviceChoice(AdbCore.Android.AdbDeviceInfo Info)
    {
        public string Display => $"{Info.Serial} ({Info.State})";
    }

    private void OnAndroidComboLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox combo)
        {
            try
            {
                combo.ItemsSource = _adbDevices.List().Select(d => new DeviceChoice(d)).ToList();
            }
            catch
            {
                combo.ItemsSource = System.Array.Empty<DeviceChoice>(); // no ADB server / devices — leave manual entry
            }
        }
    }

    private void OnAndroidChosen(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: DeviceChoice choice, Tag: TargetSelectionRow row })
        {
            row.Selector = $"serial:{choice.Info.Serial}";
        }
    }
```

- [ ] **Step 4: Build** — `dotnet build ADB.slnx -c Debug --nologo` → 0 warnings, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add BotBuilder.Core/Integration/TargetSelectionRow.cs BotBuilder/TargetPickerDialog.xaml BotBuilder/TargetPickerDialog.xaml.cs
git commit -m "feat(android): live adb-devices dropdown in the Test Run target picker"
```

---

## Task 6: Full verification

**Files:** none (verification only).

- [ ] **Step 1: Build the whole solution** — `dotnet build ADB.slnx -c Debug --nologo` → `0 Warning(s) 0 Error(s)`.

- [ ] **Step 2: Run the whole test suite** — `dotnet test ADB.slnx -c Debug --nologo --no-build`. New
  AdbCore tests this slice: AdbSelector 5, Android input 4, Android app 4 = +13. AdbCore.Tests should total
  239 (was 226). BotCapture.Core 41 / BotRunner 19 / BotBuilder.Core 137 unchanged (unless a category/count
  test needed the Task 3 bump — if so it's updated and still green).

- [ ] **Step 3: Manual run (user visual verification — needs a real ADB device/emulator).**
  - Start an emulator (or connect a device); confirm `adb devices` lists it.
  - In BotBuilder, add an **AndroidDevice** target, build a tiny bot (e.g. Tap → Delay → Press Back), then
    **Run → Test Run**: the target picker's Android dropdown lists the device; choosing it sets
    `serial:<serial>`; **Run** taps/goes-back on the device and the log streams `✓` lines.
  - Try **Screenshot (Android)** → confirms a PNG is written; **Launch App** with a real package.
  - With no device connected, Test Run shows the friendly "no connected Android device… see README"
    message (exit 2), not a crash.

> Hand off to the user for visual confirmation (a real device is required) before opening the PR.
