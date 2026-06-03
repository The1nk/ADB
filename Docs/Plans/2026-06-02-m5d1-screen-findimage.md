# M5d1 — Config Interpolation + Screen Infra + Find Image Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the first Screen action — **Find Image** — end-to-end: capture the target window, template-match (OpenCvSharp), honor an optional region-of-interest, write the match location to run variables, and make those variables consumable via new `${variable}` config interpolation (so "find → click the match" works).

**Architecture:** `${var}` interpolation is applied centrally in `BotExecutor` right before leaf dispatch (zero per-action change). Screen capture + matching sit behind narrow interfaces (`IWindowCapture`, `ITemplateMatcher`) with `System.Drawing.Bitmap` as the image type, so `ScreenActionBase` and `FindImageAction` are fully headless-testable with fakes; only thin Win32/OpenCvSharp adapters touch the OS/native libs. "Not found" returns `ActionResult.Fail`, which the engine uses to drive retry and `onFailure` routing.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), xUnit. New deps: `OpenCvSharp4`, `OpenCvSharp4.runtime.win`, `OpenCvSharp4.Extensions`, `System.Drawing.Common`. Per `Docs/Specs/2026-06-02-m5d-screen-actions-design.md`.

---

## File Structure

- `AdbCore/Models/BotAction.cs` — add `CloneWithConfig`.
- `AdbCore/Execution/ConfigInterpolator.cs` — **new**: `${var}` substitution + `Resolve`.
- `AdbCore/Execution/BotExecutor.cs` — resolve config before leaf dispatch.
- `AdbCore/AdbCore.csproj` — add the four packages.
- `AdbCore/Screen/` — **new**: `ScreenCaptureMethod`, `MatchResult`, `IWindowCapture`, `ITemplateMatcher`, `IRandomSource`, `SystemRandomSource`, `Win32WindowCapture`, `OpenCvSharpTemplateMatcher`.
- `AdbCore/Actions/BuiltIn/ScreenActionBase.cs` — **new**: HWND resolution, capture-method + ROI fields, `CaptureAndMatch`/`ResolveRegion`.
- `AdbCore/Actions/BuiltIn/FindImageAction.cs` — **new**.
- `AdbCore/Actions/BuiltIn/BuiltInActions.cs` — register Find Image.
- Tests: `AdbCore.Tests/Execution/ConfigInterpolatorTests.cs`, `BotExecutorInterpolationTests.cs`, `AdbCore.Tests/Screen/*`, `AdbCore.Tests/Actions/BuiltIn/ScreenActionBaseTests.cs`, `FindImageActionTests.cs`, plus fakes.

---

## Task 1: Config interpolation core (`ConfigInterpolator` + `BotAction.CloneWithConfig`)

**Files:**
- Modify: `AdbCore/Models/BotAction.cs`
- Create: `AdbCore/Execution/ConfigInterpolator.cs`
- Test: `AdbCore.Tests/Execution/ConfigInterpolatorTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `AdbCore.Tests/Execution/ConfigInterpolatorTests.cs`:

```csharp
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Execution;

public class ConfigInterpolatorTests
{
    private static Dictionary<string, object> Vars(params (string k, object v)[] pairs)
    {
        var d = new Dictionary<string, object>();
        foreach (var (k, v) in pairs) d[k] = v;
        return d;
    }

    [Theory]
    [InlineData("no tokens", "no tokens")]
    [InlineData("", "")]
    public void Interpolate_NoToken_ReturnsSame(string input, string expected)
        => Assert.Equal(expected, ConfigInterpolator.Interpolate(input, Vars()));

    [Fact]
    public void Interpolate_SingleToken_Substitutes()
        => Assert.Equal("x=12", ConfigInterpolator.Interpolate("x=${a}", Vars(("a", "12"))));

    [Fact]
    public void Interpolate_MultipleTokens_AndSurroundingText()
        => Assert.Equal("(3,4)", ConfigInterpolator.Interpolate("(${x},${y})", Vars(("x", "3"), ("y", "4"))));

    [Fact]
    public void Interpolate_UnknownVariable_BecomesEmpty()
        => Assert.Equal("a=", ConfigInterpolator.Interpolate("a=${missing}", Vars()));

    [Fact]
    public void Interpolate_CoercesNonStringValue()
        => Assert.Equal("42", ConfigInterpolator.Interpolate("${n}", Vars(("n", 42))));

    [Fact]
    public void Resolve_NoToken_ReturnsSameInstance()
    {
        var action = new BotAction { TypeKey = "x", Config = { ["a"] = "plain", ["b"] = 5 } };
        Assert.Same(action, ConfigInterpolator.Resolve(action, Vars(("a", "ignored"))));
    }

    [Fact]
    public void Resolve_WithToken_ClonesAndInterpolates_OriginalUntouched()
    {
        var id = Guid.NewGuid();
        var action = new BotAction { Id = id, TypeKey = "input.click", Label = "Click", TargetId = id, Config = { ["x"] = "${cx}", ["y"] = 7 } };

        var resolved = ConfigInterpolator.Resolve(action, Vars(("cx", "120")));

        Assert.NotSame(action, resolved);
        Assert.Equal("120", resolved.Config["x"]);
        Assert.Equal(7, resolved.Config["y"]);            // non-string passes through
        Assert.Equal("${cx}", action.Config["x"]);        // original untouched
        Assert.Equal(id, resolved.Id);
        Assert.Equal("input.click", resolved.TypeKey);
        Assert.Equal("Click", resolved.Label);
        Assert.Equal(id, resolved.TargetId);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test AdbCore.Tests/AdbCore.Tests.csproj --filter "FullyQualifiedName~ConfigInterpolatorTests"`
Expected: FAIL to compile (`ConfigInterpolator`, `CloneWithConfig` missing).

- [ ] **Step 3: Add `CloneWithConfig` to `BotAction`**

In `AdbCore/Models/BotAction.cs`, add inside the class:

```csharp
    /// <summary>Returns a shallow copy of this action with <paramref name="config"/> in place of
    /// <see cref="Config"/> — used to execute an action against an interpolated config without
    /// mutating the stored node.</summary>
    public BotAction CloneWithConfig(Dictionary<string, object> config) => new()
    {
        Id = Id,
        TypeKey = TypeKey,
        Label = Label,
        TargetId = TargetId,
        Config = config,
        Retry = Retry,
        CanvasPosition = CanvasPosition,
    };
```

- [ ] **Step 4: Create `ConfigInterpolator`**

Create `AdbCore/Execution/ConfigInterpolator.cs`:

```csharp
using System.Text.RegularExpressions;
using AdbCore.Actions;
using AdbCore.Models;

namespace AdbCore.Execution;

/// <summary>Resolves <c>${variableName}</c> tokens in an action's string config values against the run
/// variables, immediately before the action executes. Unknown variables resolve to empty string;
/// non-string config values pass through untouched; the original action/config is never mutated.</summary>
public static partial class ConfigInterpolator
{
    [GeneratedRegex(@"\$\{([^}]+)\}")]
    private static partial Regex TokenRegex();

    /// <summary>Replaces each <c>${name}</c> in <paramref name="template"/> with the string form of
    /// <c>variables[name]</c> (name trimmed; unknown ⇒ empty). A value with no <c>${</c> is returned as-is.</summary>
    public static string Interpolate(string template, IReadOnlyDictionary<string, object> variables)
    {
        if (string.IsNullOrEmpty(template) || !template.Contains("${", StringComparison.Ordinal))
        {
            return template;
        }

        return TokenRegex().Replace(template, m =>
        {
            var name = m.Groups[1].Value.Trim();
            return variables.TryGetValue(name, out var value) ? ConfigValues.AsString(value) : string.Empty;
        });
    }

    /// <summary>Returns <paramref name="action"/> unchanged when no string config value contains a token;
    /// otherwise a clone whose Config has string values interpolated (non-strings pass through).</summary>
    public static BotAction Resolve(BotAction action, IReadOnlyDictionary<string, object> variables)
    {
        var hasToken = false;
        foreach (var v in action.Config.Values)
        {
            if (v is string s && s.Contains("${", StringComparison.Ordinal))
            {
                hasToken = true;
                break;
            }
        }

        if (!hasToken)
        {
            return action;
        }

        var resolved = new Dictionary<string, object>(action.Config.Count);
        foreach (var (key, value) in action.Config)
        {
            resolved[key] = value is string s ? Interpolate(s, variables) : value;
        }

        return action.CloneWithConfig(resolved);
    }
}
```

- [ ] **Step 5: Run tests — expect green**

Run: `dotnet test AdbCore.Tests/AdbCore.Tests.csproj --filter "FullyQualifiedName~ConfigInterpolatorTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add AdbCore/Models/BotAction.cs AdbCore/Execution/ConfigInterpolator.cs AdbCore.Tests/Execution/ConfigInterpolatorTests.cs
git commit -m "feat(core): add ${var} config interpolation + BotAction.CloneWithConfig"
```

---

## Task 2: Wire interpolation into `BotExecutor`

**Files:**
- Modify: `AdbCore/Execution/BotExecutor.cs` (`ExecuteWithRetryAsync`, ~line 356-399)
- Test: `AdbCore.Tests/Execution/BotExecutorInterpolationTests.cs`

- [ ] **Step 1: Write the failing integration test**

`RunAsync(Bot, ExecutionOptions, IProgress?, CancellationToken)` builds its own internal context, so run variables aren't readable afterward — observe through the **Log sink** instead. The bot sets `foo=bar`, then a Log action whose message is `${foo}`; the sink must receive `bar`.

Create `AdbCore.Tests/Execution/BotExecutorInterpolationTests.cs`:

```csharp
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Execution;

public class BotExecutorInterpolationTests
{
    [Fact]
    public async Task LeafConfig_IsInterpolated_FromRunVariables()
    {
        var execs = new ActionExecutorRegistry();
        execs.Register(new StartAction());
        execs.Register(new EndAction());
        execs.Register(new SetVariableAction());
        execs.Register(new LogAction());

        var start = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.start" };
        var setFoo = new BotAction { Id = Guid.NewGuid(), TypeKey = "data.setVariable", Config = { [SetVariableAction.NameKey] = "foo", [SetVariableAction.ValueKey] = "bar" } };
        var log = new BotAction { Id = Guid.NewGuid(), TypeKey = "data.log", Config = { [LogAction.MessageKey] = "${foo}" } };
        var end = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.end" };

        var bot = new Bot { Actions = { start, setFoo, log, end }, Connections =
        {
            new ActionConnection { SourceActionId = start.Id, SourcePort = "out", TargetActionId = setFoo.Id, TargetPort = "in" },
            new ActionConnection { SourceActionId = setFoo.Id, SourcePort = "out", TargetActionId = log.Id, TargetPort = "in" },
            new ActionConnection { SourceActionId = log.Id, SourcePort = "out", TargetActionId = end.Id, TargetPort = "in" },
        } };

        var logs = new List<string>();
        var result = await new BotExecutor(execs).RunAsync(bot, new ExecutionOptions { Log = logs.Add }, null, default);

        Assert.True(result.Success);
        Assert.Contains("bar", logs);   // ${foo} resolved before the Log action ran
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test AdbCore.Tests/AdbCore.Tests.csproj --filter "FullyQualifiedName~BotExecutorInterpolationTests"`
Expected: FAIL — the log sink receives the literal `"${foo}"` (interpolation not wired yet).

- [ ] **Step 3: Resolve config before dispatch**

In `AdbCore/Execution/BotExecutor.cs`, `ExecuteWithRetryAsync`, add the resolve just before the retry loop and use it when building the context. Replace:

```csharp
        var delayMs = action.Retry?.DelayMs ?? 0;
        var result = ActionResult.Fail("Action did not execute.");

        for (var attempt = 0; attempt < attempts; attempt++)
        {
```

with:

```csharp
        var delayMs = action.Retry?.DelayMs ?? 0;
        var result = ActionResult.Fail("Action did not execute.");

        // Resolve ${var} tokens in config against the current run variables, once per execution
        // (variables are stable across this action's retry attempts). Retry policy is read from the original.
        var resolvedAction = ConfigInterpolator.Resolve(action, state.Context.Variables);

        for (var attempt = 0; attempt < attempts; attempt++)
        {
```

and change the context construction inside the loop from `new ActionExecutionContext(action, ...)` to:

```csharp
                var actionContext = new ActionExecutionContext(resolvedAction, state.Context, state.Log);
```

- [ ] **Step 4: Run tests — expect green (and no regressions)**

Run: `dotnet test AdbCore.Tests/AdbCore.Tests.csproj --filter "FullyQualifiedName~BotExecutor"`
Expected: PASS (the new test + all existing BotExecutor tests).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Execution/BotExecutor.cs AdbCore.Tests/Execution/BotExecutorInterpolationTests.cs
git commit -m "feat(core): interpolate action config from run variables before dispatch"
```

---

## Task 3: Add OpenCvSharp + System.Drawing packages with a native-load smoke test

**Files:**
- Modify: `AdbCore/AdbCore.csproj`
- Test: `AdbCore.Tests/Screen/NativeDependencyTests.cs`

- [ ] **Step 1: Add the package references**

In `AdbCore/AdbCore.csproj`, add an `<ItemGroup>` (use the latest stable 4.x OpenCvSharp and current `System.Drawing.Common`; if a version is unavailable, pick the nearest stable and note it):

```xml
  <ItemGroup>
    <PackageReference Include="OpenCvSharp4" Version="4.10.0.20240616" />
    <PackageReference Include="OpenCvSharp4.runtime.win" Version="4.10.0.20240616" />
    <PackageReference Include="OpenCvSharp4.Extensions" Version="4.10.0.20240616" />
    <PackageReference Include="System.Drawing.Common" Version="9.0.0" />
  </ItemGroup>
```

- [ ] **Step 2: Write the smoke test**

Create `AdbCore.Tests/Screen/NativeDependencyTests.cs`:

```csharp
using OpenCvSharp;
using Xunit;

namespace AdbCore.Tests.Screen;

public class NativeDependencyTests
{
    [Fact]
    public void OpenCvSharp_NativeRuntime_Loads()
    {
        // Constructing a Mat forces the native runtime to load; if the runtime package is missing
        // or incompatible with net10.0-windows, this throws (DllNotFound/TypeInitialization).
        using var mat = new Mat(2, 2, MatType.CV_8UC3, Scalar.All(0));
        Assert.Equal(2, mat.Rows);
        Assert.Equal(2, mat.Cols);
    }
}
```

- [ ] **Step 3: Build + run the smoke test**

Run: `dotnet test AdbCore.Tests/AdbCore.Tests.csproj --filter "FullyQualifiedName~NativeDependencyTests"`
Expected: PASS. If it fails with a native-load error, adjust the `OpenCvSharp4.runtime.win` version to match `OpenCvSharp4` and retry.

- [ ] **Step 4: Commit**

```bash
git add AdbCore/AdbCore.csproj AdbCore.Tests/Screen/NativeDependencyTests.cs
git commit -m "build(core): add OpenCvSharp + System.Drawing deps with native-load smoke test"
```

---

## Task 4: Screen contracts (`ScreenCaptureMethod`, `MatchResult`, interfaces, `SystemRandomSource`)

**Files:**
- Create: `AdbCore/Screen/ScreenCaptureMethod.cs`, `MatchResult.cs`, `IWindowCapture.cs`, `ITemplateMatcher.cs`, `IRandomSource.cs`, `SystemRandomSource.cs`
- Test: `AdbCore.Tests/Screen/SystemRandomSourceTests.cs`

- [ ] **Step 1: Write the failing test for `SystemRandomSource`**

Create `AdbCore.Tests/Screen/SystemRandomSourceTests.cs`:

```csharp
using AdbCore.Screen;
using Xunit;

namespace AdbCore.Tests.Screen;

public class SystemRandomSourceTests
{
    [Fact]
    public void Next_StaysWithinInclusiveBounds()
    {
        var rng = new SystemRandomSource();
        for (var i = 0; i < 1000; i++)
        {
            var v = rng.Next(10, 12);
            Assert.InRange(v, 10, 12);
        }
    }

    [Fact]
    public void Next_MinEqualsMax_ReturnsThatValue()
        => Assert.Equal(7, new SystemRandomSource().Next(7, 7));
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test AdbCore.Tests/AdbCore.Tests.csproj --filter "FullyQualifiedName~SystemRandomSourceTests"`
Expected: FAIL to compile (types missing).

- [ ] **Step 3: Create the contracts**

`AdbCore/Screen/ScreenCaptureMethod.cs`:
```csharp
namespace AdbCore.Screen;

/// <summary>How <see cref="IWindowCapture"/> grabs the target window.</summary>
public enum ScreenCaptureMethod
{
    /// <summary>PrintWindow (works for non-foreground/standard apps), falling back to screen BitBlt on a blank frame.</summary>
    Auto,
    /// <summary>Force screen-region BitBlt (captures visible pixels incl. GPU/DirectX; window must be unoccluded).</summary>
    BitBlt,
}
```

`AdbCore/Screen/MatchResult.cs`:
```csharp
namespace AdbCore.Screen;

/// <summary>A template match in haystack (window-client) pixels. <see cref="X"/>,<see cref="Y"/> is the
/// top-left of the matched region; <see cref="Score"/> is the 0–1 match confidence.</summary>
public readonly record struct MatchResult(int X, int Y, int Width, int Height, double Score);
```

`AdbCore/Screen/IWindowCapture.cs`:
```csharp
using System.Drawing;

namespace AdbCore.Screen;

/// <summary>Captures a target window's client area into a bitmap. The caller owns and disposes the result.</summary>
public interface IWindowCapture
{
    Bitmap Capture(IntPtr windowHandle, ScreenCaptureMethod method);
}
```

`AdbCore/Screen/ITemplateMatcher.cs`:
```csharp
using System.Drawing;

namespace AdbCore.Screen;

/// <summary>Finds a template image within a haystack bitmap. Returns the single best match when its score
/// meets <paramref name="minConfidence"/> (0–1), else null. Throws if the template can't be read.</summary>
public interface ITemplateMatcher
{
    MatchResult? Match(Bitmap haystack, string templatePath, double minConfidence);
}
```

`AdbCore/Screen/IRandomSource.cs`:
```csharp
namespace AdbCore.Screen;

/// <summary>Indirection over RNG so image-search randomness (e.g. a random click point within a match)
/// is deterministic in tests.</summary>
public interface IRandomSource
{
    /// <summary>Returns a random integer in the inclusive range [<paramref name="minInclusive"/>, <paramref name="maxInclusive"/>].</summary>
    int Next(int minInclusive, int maxInclusive);
}
```

`AdbCore/Screen/SystemRandomSource.cs`:
```csharp
namespace AdbCore.Screen;

/// <summary>Production <see cref="IRandomSource"/> over <see cref="Random.Shared"/>.</summary>
public sealed class SystemRandomSource : IRandomSource
{
    public int Next(int minInclusive, int maxInclusive) => Random.Shared.Next(minInclusive, maxInclusive + 1);
}
```

- [ ] **Step 4: Run tests — expect green**

Run: `dotnet test AdbCore.Tests/AdbCore.Tests.csproj --filter "FullyQualifiedName~SystemRandomSourceTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Screen/ScreenCaptureMethod.cs AdbCore/Screen/MatchResult.cs AdbCore/Screen/IWindowCapture.cs AdbCore/Screen/ITemplateMatcher.cs AdbCore/Screen/IRandomSource.cs AdbCore/Screen/SystemRandomSource.cs AdbCore.Tests/Screen/SystemRandomSourceTests.cs
git commit -m "feat(screen): add capture/match contracts + SystemRandomSource"
```

---

## Task 5: `ScreenActionBase` (HWND resolution, capture-method + ROI fields, `CaptureAndMatch`)

**Files:**
- Create: `AdbCore/Actions/BuiltIn/ScreenActionBase.cs`
- Test: `AdbCore.Tests/Screen/FakeScreenDependencies.cs` (fakes), `AdbCore.Tests/Actions/BuiltIn/ScreenActionBaseTests.cs`

- [ ] **Step 1: Write the fakes + failing tests**

Create `AdbCore.Tests/Screen/FakeScreenDependencies.cs`:

```csharp
using System.Drawing;
using AdbCore.Screen;

namespace AdbCore.Tests.Screen;

internal sealed class FakeWindowCapture(int width, int height) : IWindowCapture
{
    public int Calls { get; private set; }
    public ScreenCaptureMethod LastMethod { get; private set; }

    public Bitmap Capture(IntPtr windowHandle, ScreenCaptureMethod method)
    {
        Calls++;
        LastMethod = method;
        return new Bitmap(width, height);
    }
}

internal sealed class FakeTemplateMatcher(MatchResult? result) : ITemplateMatcher
{
    public int LastHaystackWidth { get; private set; }
    public int LastHaystackHeight { get; private set; }
    public string? LastTemplatePath { get; private set; }
    public double LastConfidence { get; private set; }

    public MatchResult? Match(Bitmap haystack, string templatePath, double minConfidence)
    {
        LastHaystackWidth = haystack.Width;
        LastHaystackHeight = haystack.Height;
        LastTemplatePath = templatePath;
        LastConfidence = minConfidence;
        return result;
    }
}

internal sealed class FixedRandomSource(int value) : IRandomSource
{
    public int Next(int minInclusive, int maxInclusive) => value;
}
```

Create `AdbCore.Tests/Actions/BuiltIn/ScreenActionBaseTests.cs` — a tiny concrete subclass exposes the protected helper so the crop/offset/clamp logic can be tested directly:

```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Screen;
using AdbCore.Tests.Screen;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class ScreenActionBaseTests
{
    private sealed class TestScreenAction(IWindowCapture capture, ITemplateMatcher matcher) : ScreenActionBase(capture, matcher)
    {
        public override string TypeKey => "screen.test";
        public override string DisplayName => "Test Screen";
        public override string Description => "";
        public override List<PortDefinition> OutputPorts { get; } = new() { new PortDefinition { Name = SuccessPort, Label = "On Success" } };
        protected override IEnumerable<ConfigField> ActionConfigFields => [];
        public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct) => Task.FromResult(ActionResult.Ok(SuccessPort));

        public MatchResult? CallCaptureAndMatch(ActionExecutionContext ctx, IntPtr hwnd, string template, double confidence)
            => CaptureAndMatch(ctx, hwnd, template, confidence);
    }

    private static ActionExecutionContext Exec(BotAction action) => new(action, new BotExecutionContext(), _ => { });

    [Fact]
    public void CaptureAndMatch_NoRegion_PassesFullHaystack_AndReturnsMatchUnchanged()
    {
        var capture = new FakeWindowCapture(1920, 1080);
        var matcher = new FakeTemplateMatcher(new MatchResult(50, 60, 10, 8, 0.95));
        var action = new TestScreenAction(capture, matcher);

        var result = action.CallCaptureAndMatch(Exec(new BotAction()), (IntPtr)1, "t.png", 0.8);

        Assert.Equal(1920, matcher.LastHaystackWidth);
        Assert.Equal(1080, matcher.LastHaystackHeight);
        Assert.Equal(new MatchResult(50, 60, 10, 8, 0.95), result);
    }

    [Fact]
    public void CaptureAndMatch_WithRegion_CropsHaystack_AndOffsetsResultBack()
    {
        var capture = new FakeWindowCapture(1920, 1080);
        var matcher = new FakeTemplateMatcher(new MatchResult(5, 7, 10, 8, 0.9)); // crop-local coords
        var action = new TestScreenAction(capture, matcher);
        var botAction = new BotAction { Config =
        {
            [ScreenActionBase.RegionXKey] = 100, [ScreenActionBase.RegionYKey] = 40,
            [ScreenActionBase.RegionWidthKey] = 300, [ScreenActionBase.RegionHeightKey] = 200,
        } };

        var result = action.CallCaptureAndMatch(Exec(botAction), (IntPtr)1, "t.png", 0.8);

        Assert.Equal(300, matcher.LastHaystackWidth);   // matcher saw the crop
        Assert.Equal(200, matcher.LastHaystackHeight);
        Assert.Equal(new MatchResult(105, 47, 10, 8, 0.9), result); // 5+100, 7+40
    }

    [Fact]
    public void CaptureAndMatch_RegionClampedToWindow()
    {
        var capture = new FakeWindowCapture(200, 150);
        var matcher = new FakeTemplateMatcher(new MatchResult(0, 0, 1, 1, 0.9));
        var action = new TestScreenAction(capture, matcher);
        var botAction = new BotAction { Config =
        {
            [ScreenActionBase.RegionXKey] = 180, [ScreenActionBase.RegionYKey] = 140,
            [ScreenActionBase.RegionWidthKey] = 999, [ScreenActionBase.RegionHeightKey] = 999,
        } };

        action.CallCaptureAndMatch(Exec(botAction), (IntPtr)1, "t.png", 0.8);

        Assert.Equal(20, matcher.LastHaystackWidth);   // 200-180
        Assert.Equal(10, matcher.LastHaystackHeight);  // 150-140
    }

    [Fact]
    public void CaptureMethod_DefaultsAuto_AndHonorsBitBltOverride()
    {
        var capture = new FakeWindowCapture(10, 10);
        var action = new TestScreenAction(capture, new FakeTemplateMatcher(null));

        action.CallCaptureAndMatch(Exec(new BotAction()), (IntPtr)1, "t.png", 0.8);
        Assert.Equal(ScreenCaptureMethod.Auto, capture.LastMethod);

        var bitblt = new BotAction { Config = { [ScreenActionBase.CaptureMethodKey] = nameof(ScreenCaptureMethod.BitBlt) } };
        action.CallCaptureAndMatch(Exec(bitblt), (IntPtr)1, "t.png", 0.8);
        Assert.Equal(ScreenCaptureMethod.BitBlt, capture.LastMethod);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test AdbCore.Tests/AdbCore.Tests.csproj --filter "FullyQualifiedName~ScreenActionBaseTests"`
Expected: FAIL to compile (`ScreenActionBase` missing).

- [ ] **Step 3: Create `ScreenActionBase`**

Create `AdbCore/Actions/BuiltIn/ScreenActionBase.cs`:

```csharp
using System.Drawing;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Shared base for Screen actions: resolves the target window HWND, exposes the Capture Method
/// + region-of-interest config fields, and provides a capture→(crop)→match helper that returns matches in
/// full-window client coordinates. Mirrors <see cref="InputActionBase"/>'s structure.</summary>
public abstract class ScreenActionBase : IActionDefinition, IActionExecutor
{
    public const string CaptureMethodKey = "captureMethod";
    public const string RegionXKey = "regionX";
    public const string RegionYKey = "regionY";
    public const string RegionWidthKey = "regionWidth";
    public const string RegionHeightKey = "regionHeight";
    public const string SuccessPort = "onSuccess";
    public const string FailurePort = "onFailure";

    private readonly IWindowCapture _capture;
    private readonly ITemplateMatcher _matcher;
    private List<ConfigField>? _configFields;

    protected ScreenActionBase(IWindowCapture capture, ITemplateMatcher matcher)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(matcher);
        _capture = capture;
        _matcher = matcher;
    }

    public abstract string TypeKey { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }
    public string Category => "Screen";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public abstract List<PortDefinition> OutputPorts { get; }
    public virtual bool SupportsRetry => true;

    /// <summary>The action's own config fields, shown before the shared Capture Method + ROI fields.</summary>
    protected abstract IEnumerable<ConfigField> ActionConfigFields { get; }

    public List<ConfigField> ConfigFields => _configFields ??=
    [
        .. ActionConfigFields,
        new ConfigField
        {
            Key = CaptureMethodKey, Label = "Capture Method", Type = ConfigFieldType.Enum,
            DefaultValue = nameof(ScreenCaptureMethod.Auto),
            Options = new() { nameof(ScreenCaptureMethod.Auto), nameof(ScreenCaptureMethod.BitBlt) },
        },
        new ConfigField { Key = RegionXKey, Label = "Region X", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField { Key = RegionYKey, Label = "Region Y", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField { Key = RegionWidthKey, Label = "Region Width", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField { Key = RegionHeightKey, Label = "Region Height", Type = ConfigFieldType.Number, DefaultValue = 0 },
    ];

    public abstract Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct);

    /// <summary>Resolves the action's target HWND: the explicit TargetId, or the sole target if unset.</summary>
    protected static IntPtr? ResolveWindow(ActionExecutionContext context)
    {
        var targets = context.Context.Targets;
        ResolvedTarget? target = context.Action.TargetId is Guid id
            ? targets.TryGetValue(id, out var t) ? t : null
            : targets.Count == 1 ? targets.Values.First() : null;

        return target?.Handle as IntPtr?;
    }

    private ScreenCaptureMethod CaptureMethodOf(ActionExecutionContext context)
        => string.Equals(
               ConfigValues.GetString(context.Action.Config, CaptureMethodKey, nameof(ScreenCaptureMethod.Auto)),
               nameof(ScreenCaptureMethod.BitBlt), StringComparison.OrdinalIgnoreCase)
           ? ScreenCaptureMethod.BitBlt : ScreenCaptureMethod.Auto;

    /// <summary>Reads + clamps the ROI fields against the client size; null when no usable region.</summary>
    protected static Rectangle? ResolveRegion(ActionExecutionContext context, int clientWidth, int clientHeight)
    {
        var w = ConfigValues.GetInt(context.Action.Config, RegionWidthKey, 0);
        var h = ConfigValues.GetInt(context.Action.Config, RegionHeightKey, 0);
        if (w <= 0 || h <= 0 || clientWidth <= 0 || clientHeight <= 0)
        {
            return null;
        }

        var x = Math.Clamp(ConfigValues.GetInt(context.Action.Config, RegionXKey, 0), 0, clientWidth - 1);
        var y = Math.Clamp(ConfigValues.GetInt(context.Action.Config, RegionYKey, 0), 0, clientHeight - 1);
        w = Math.Min(w, clientWidth - x);
        h = Math.Min(h, clientHeight - y);
        return w > 0 && h > 0 ? new Rectangle(x, y, w, h) : null;
    }

    /// <summary>Captures the window, applies any ROI crop, matches the template, and returns the match in
    /// full-window client coordinates (null if none ≥ confidence). Disposes the capture.</summary>
    protected MatchResult? CaptureAndMatch(ActionExecutionContext context, IntPtr hwnd, string templatePath, double confidence)
    {
        using var shot = _capture.Capture(hwnd, CaptureMethodOf(context));
        var region = ResolveRegion(context, shot.Width, shot.Height);
        if (region is not Rectangle roi)
        {
            return _matcher.Match(shot, templatePath, confidence);
        }

        using var crop = shot.Clone(roi, shot.PixelFormat);
        var hit = _matcher.Match(crop, templatePath, confidence);
        return hit is MatchResult m ? m with { X = m.X + roi.X, Y = m.Y + roi.Y } : null;
    }
}
```

- [ ] **Step 4: Run tests — expect green**

Run: `dotnet test AdbCore.Tests/AdbCore.Tests.csproj --filter "FullyQualifiedName~ScreenActionBaseTests"`
Expected: PASS (all four tests).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Actions/BuiltIn/ScreenActionBase.cs AdbCore.Tests/Screen/FakeScreenDependencies.cs AdbCore.Tests/Actions/BuiltIn/ScreenActionBaseTests.cs
git commit -m "feat(actions): add ScreenActionBase with capture-method + ROI crop/offset helper"
```

---

## Task 6: `FindImageAction`

**Files:**
- Create: `AdbCore/Actions/BuiltIn/FindImageAction.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/FindImageActionTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `AdbCore.Tests/Actions/BuiltIn/FindImageActionTests.cs`:

```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Screen;
using AdbCore.Tests.Screen;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class FindImageActionTests
{
    private static BotExecutionContext WindowContext(Guid id, IntPtr handle)
    {
        var ctx = new BotExecutionContext();
        ctx.Targets[id] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "hwnd:1", Handle = handle };
        return ctx;
    }

    private static ActionExecutionContext Exec(BotAction a, BotExecutionContext c) => new(a, c, _ => { });

    private static FindImageAction Action(MatchResult? result, int rand = 0)
        => new(new FakeWindowCapture(800, 600), new FakeTemplateMatcher(result), new FixedRandomSource(rand));

    [Fact]
    public async Task Match_WritesAllVariables_AndRoutesSuccess()
    {
        var id = Guid.NewGuid();
        var ctx = WindowContext(id, (IntPtr)5);
        var action = new BotAction { TargetId = id, Config = { [FindImageAction.TemplatePathKey] = "btn.png" } };

        // match top-left (100,40), size 30x20 → right=130, bottom=60, center=(115,50)
        var result = await Action(new MatchResult(100, 40, 30, 20, 0.97), rand: 123)
            .ExecuteAsync(Exec(action, ctx), default);

        Assert.True(result.Success);
        Assert.Equal("onSuccess", result.OutputPort);
        Assert.Equal("100", ctx.Variables["matchLeft"]);
        Assert.Equal("40", ctx.Variables["matchTop"]);
        Assert.Equal("130", ctx.Variables["matchRight"]);
        Assert.Equal("60", ctx.Variables["matchBottom"]);
        Assert.Equal("115", ctx.Variables["matchCenterX"]);
        Assert.Equal("50", ctx.Variables["matchCenterY"]);
        Assert.Equal("123", ctx.Variables["matchRandX"]);
        Assert.Equal("123", ctx.Variables["matchRandY"]);
        Assert.Equal("0.97", ctx.Variables["matchConfidence"]);
    }

    [Fact]
    public async Task RandomPoint_IsWithinRegion()
    {
        var id = Guid.NewGuid();
        var ctx = WindowContext(id, (IntPtr)5);
        var action = new BotAction { TargetId = id, Config = { [FindImageAction.TemplatePathKey] = "btn.png" } };

        // Use the real RNG via a found region and assert bounds.
        var find = new FindImageAction(new FakeWindowCapture(800, 600), new FakeTemplateMatcher(new MatchResult(100, 40, 30, 20, 0.9)), new SystemRandomSource());
        await find.ExecuteAsync(Exec(action, ctx), default);

        Assert.InRange(int.Parse(ctx.Variables["matchRandX"].ToString()!), 100, 130);
        Assert.InRange(int.Parse(ctx.Variables["matchRandY"].ToString()!), 40, 60);
    }

    [Fact]
    public async Task CustomResultVar_PrefixesVariables()
    {
        var id = Guid.NewGuid();
        var ctx = WindowContext(id, (IntPtr)5);
        var action = new BotAction { TargetId = id, Config =
        {
            [FindImageAction.TemplatePathKey] = "btn.png",
            [FindImageAction.ResultVarKey] = "btn",
        } };

        await Action(new MatchResult(1, 2, 4, 6, 0.9)).ExecuteAsync(Exec(action, ctx), default);

        Assert.Equal("3", ctx.Variables["btnCenterX"]); // 1 + 4/2
        Assert.True(ctx.Variables.ContainsKey("btnConfidence"));
    }

    [Fact]
    public async Task NoMatch_FailsForRetryAndOnFailureRouting_WritesNothing()
    {
        var id = Guid.NewGuid();
        var ctx = WindowContext(id, (IntPtr)5);
        var action = new BotAction { TargetId = id, Config = { [FindImageAction.TemplatePathKey] = "btn.png" } };

        var result = await Action(null).ExecuteAsync(Exec(action, ctx), default);

        Assert.False(result.Success); // engine routes onFailure / retries on Success==false
        Assert.Empty(ctx.Variables);
    }

    [Fact]
    public async Task NoTarget_Fails()
    {
        var action = new BotAction { Config = { [FindImageAction.TemplatePathKey] = "btn.png" } };
        var result = await Action(new MatchResult(0, 0, 1, 1, 1)).ExecuteAsync(Exec(action, new BotExecutionContext()), default);
        Assert.False(result.Success);
        Assert.Contains("Window", result.ErrorMessage);
    }

    [Fact]
    public async Task BlankTemplatePath_Fails()
    {
        var id = Guid.NewGuid();
        var action = new BotAction { TargetId = id };
        var result = await Action(new MatchResult(0, 0, 1, 1, 1)).ExecuteAsync(Exec(action, WindowContext(id, (IntPtr)5)), default);
        Assert.False(result.Success);
        Assert.Contains("template", result.ErrorMessage);
    }

    [Fact]
    public void Definition_Metadata()
    {
        var def = Action(null);
        Assert.Equal("screen.findImage", def.TypeKey);
        Assert.Equal("Find Image", def.DisplayName);
        Assert.Equal("Screen", def.Category);
        Assert.Equal(new[] { "onSuccess", "onFailure" }, def.OutputPorts.Select(p => p.Name));
        Assert.True(def.SupportsRetry);
        Assert.Contains(def.ConfigFields, f => f.Key == ScreenActionBase.RegionWidthKey);
        Assert.Contains(def.ConfigFields, f => f.Key == ScreenActionBase.CaptureMethodKey);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test AdbCore.Tests/AdbCore.Tests.csproj --filter "FullyQualifiedName~FindImageActionTests"`
Expected: FAIL to compile (`FindImageAction` missing).

- [ ] **Step 3: Create `FindImageAction`**

Create `AdbCore/Actions/BuiltIn/FindImageAction.cs`:

```csharp
using System.Globalization;
using AdbCore.Execution;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Finds a template image within the target window and writes its location (region edges,
/// center, a random in-region point, and the score) to run variables under a configurable prefix.
/// "Not found" is a failed result so the engine can retry (per RetryPolicy) and route onFailure.</summary>
public sealed class FindImageAction : ScreenActionBase
{
    public const string TemplatePathKey = "templatePath";
    public const string ConfidenceKey = "confidence";
    public const string ResultVarKey = "resultVar";
    public const double DefaultConfidence = 0.8;
    public const string DefaultResultVar = "match";

    private readonly IRandomSource _random;

    public FindImageAction(IWindowCapture capture, ITemplateMatcher matcher, IRandomSource random)
        : base(capture, matcher)
    {
        ArgumentNullException.ThrowIfNull(random);
        _random = random;
    }

    public override string TypeKey => "screen.findImage";
    public override string DisplayName => "Find Image";
    public override string Description => "Finds a template image within the target window and writes its location to variables.";

    public override List<PortDefinition> OutputPorts { get; } = new()
    {
        new PortDefinition { Name = SuccessPort, Label = "On Success" },
        new PortDefinition { Name = FailurePort, Label = "On Failure" },
    };

    protected override IEnumerable<ConfigField> ActionConfigFields =>
    [
        new ConfigField { Key = TemplatePathKey, Label = "Template Image", Type = ConfigFieldType.ImagePath },
        new ConfigField { Key = ConfidenceKey, Label = "Confidence", Type = ConfigFieldType.Number, DefaultValue = DefaultConfidence },
        new ConfigField { Key = ResultVarKey, Label = "Result Variable", Type = ConfigFieldType.String, DefaultValue = DefaultResultVar },
    ];

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (ResolveWindow(context) is not IntPtr hwnd || hwnd == IntPtr.Zero)
        {
            return Task.FromResult(ActionResult.Fail($"{DisplayName} requires a resolved Window target (HWND)."));
        }

        var templatePath = ConfigValues.GetString(context.Action.Config, TemplatePathKey);
        if (string.IsNullOrWhiteSpace(templatePath))
        {
            return Task.FromResult(ActionResult.Fail("Find Image: a template image path is required."));
        }

        var confidence = ConfigValues.GetDouble(context.Action.Config, ConfidenceKey, DefaultConfidence);
        var prefix = ConfigValues.GetString(context.Action.Config, ResultVarKey, DefaultResultVar);
        if (string.IsNullOrWhiteSpace(prefix))
        {
            prefix = DefaultResultVar;
        }

        if (CaptureAndMatch(context, hwnd, templatePath, confidence) is not MatchResult m)
        {
            return Task.FromResult(ActionResult.Fail("Find Image: no match at or above the configured confidence."));
        }

        var left = m.X;
        var top = m.Y;
        var right = m.X + m.Width;
        var bottom = m.Y + m.Height;
        var vars = context.Context.Variables;
        vars[$"{prefix}Left"] = Str(left);
        vars[$"{prefix}Top"] = Str(top);
        vars[$"{prefix}Right"] = Str(right);
        vars[$"{prefix}Bottom"] = Str(bottom);
        vars[$"{prefix}CenterX"] = Str(m.X + m.Width / 2);
        vars[$"{prefix}CenterY"] = Str(m.Y + m.Height / 2);
        vars[$"{prefix}RandX"] = Str(_random.Next(left, right));
        vars[$"{prefix}RandY"] = Str(_random.Next(top, bottom));
        vars[$"{prefix}Confidence"] = m.Score.ToString(CultureInfo.InvariantCulture);

        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }

    private static string Str(int v) => v.ToString(CultureInfo.InvariantCulture);
}
```

- [ ] **Step 4: Run tests — expect green**

Run: `dotnet test AdbCore.Tests/AdbCore.Tests.csproj --filter "FullyQualifiedName~FindImageActionTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Actions/BuiltIn/FindImageAction.cs AdbCore.Tests/Actions/BuiltIn/FindImageActionTests.cs
git commit -m "feat(actions): add Find Image (screen.findImage) with region+center+random outputs"
```

---

## Task 7: Win32 + OpenCvSharp adapters (build-only, manually verified)

These thin adapters touch the OS/native libs and are verified by the user via real run (no unit tests, consistent with the project's OS-adapter pattern).

**Files:**
- Create: `AdbCore/Screen/Win32WindowCapture.cs`, `AdbCore/Screen/OpenCvSharpTemplateMatcher.cs`

- [ ] **Step 1: Create `Win32WindowCapture`**

Create `AdbCore/Screen/Win32WindowCapture.cs`:

```csharp
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace AdbCore.Screen;

/// <summary>Foreground/standard capture via PrintWindow (PW_RENDERFULLCONTENT) with a screen-region
/// BitBlt fallback when PrintWindow yields a blank frame; or forced BitBlt. Captures the client area.</summary>
public sealed class Win32WindowCapture : IWindowCapture
{
    private const uint PW_RENDERFULLCONTENT = 0x00000002;
    private const int SRCCOPY = 0x00CC0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr hdcDest, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, int rop);

    public Bitmap Capture(IntPtr windowHandle, ScreenCaptureMethod method)
    {
        GetClientRect(windowHandle, out var rc);
        var width = Math.Max(1, rc.Right - rc.Left);
        var height = Math.Max(1, rc.Bottom - rc.Top);

        if (method == ScreenCaptureMethod.BitBlt)
        {
            return CaptureViaScreenBitBlt(windowHandle, width, height);
        }

        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            var hdc = g.GetHdc();
            try
            {
                PrintWindow(windowHandle, hdc, PW_RENDERFULLCONTENT);
            }
            finally
            {
                g.ReleaseHdc(hdc);
            }
        }

        if (IsBlank(bmp))
        {
            bmp.Dispose();
            return CaptureViaScreenBitBlt(windowHandle, width, height);
        }

        return bmp;
    }

    private static Bitmap CaptureViaScreenBitBlt(IntPtr windowHandle, int width, int height)
    {
        var origin = new POINT { X = 0, Y = 0 };
        ClientToScreen(windowHandle, ref origin);

        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        var destHdc = g.GetHdc();
        var screenDc = GetDC(IntPtr.Zero);
        try
        {
            BitBlt(destHdc, 0, 0, width, height, screenDc, origin.X, origin.Y, SRCCOPY);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
            g.ReleaseHdc(destHdc);
        }

        return bmp;
    }

    // Sample a handful of pixels; PrintWindow on GPU-composited windows returns an all-zero (transparent/black) frame.
    private static bool IsBlank(Bitmap bmp)
    {
        var first = bmp.GetPixel(0, 0);
        for (var i = 0; i < 5; i++)
        {
            var p = bmp.GetPixel((bmp.Width - 1) * i / 4, (bmp.Height - 1) * i / 4);
            if (p.ToArgb() != first.ToArgb())
            {
                return false;
            }
        }

        return first.A == 0 || (first.R == 0 && first.G == 0 && first.B == 0);
    }
}
```

- [ ] **Step 2: Create `OpenCvSharpTemplateMatcher`**

Create `AdbCore/Screen/OpenCvSharpTemplateMatcher.cs`:

```csharp
using System.Drawing;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace AdbCore.Screen;

/// <summary>Template matching via OpenCvSharp (<c>TM_CCOEFF_NORMED</c>, single best match).</summary>
public sealed class OpenCvSharpTemplateMatcher : ITemplateMatcher
{
    public MatchResult? Match(Bitmap haystack, string templatePath, double minConfidence)
    {
        if (string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath))
        {
            throw new FileNotFoundException($"Template image not found: '{templatePath}'.", templatePath);
        }

        using var template = Cv2.ImRead(templatePath, ImreadModes.Color);
        if (template.Empty())
        {
            throw new InvalidOperationException($"Template image could not be read: '{templatePath}'.");
        }

        using var source = haystack.ToMat();          // BGRA from a 32bpp bitmap
        using var sourceBgr = new Mat();
        Cv2.CvtColor(source, sourceBgr, ColorConversionCodes.BGRA2BGR);

        if (template.Width > sourceBgr.Width || template.Height > sourceBgr.Height)
        {
            return null; // template larger than haystack (e.g. ROI smaller than template)
        }

        using var result = new Mat();
        Cv2.MatchTemplate(sourceBgr, template, result, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out var maxLoc);

        if (maxVal < minConfidence)
        {
            return null;
        }

        return new MatchResult(maxLoc.X, maxLoc.Y, template.Width, template.Height, maxVal);
    }
}
```

- [ ] **Step 3: Build (no unit tests for OS/native adapters)**

Run: `dotnet build AdbCore/AdbCore.csproj`
Expected: Build succeeded, 0 errors, 0 warnings.

- [ ] **Step 4: Commit**

```bash
git add AdbCore/Screen/Win32WindowCapture.cs AdbCore/Screen/OpenCvSharpTemplateMatcher.cs
git commit -m "feat(screen): add Win32 window-capture + OpenCvSharp template-matcher adapters"
```

---

## Task 8: Register Find Image + retry integration test

**Files:**
- Modify: `AdbCore/Actions/BuiltIn/BuiltInActions.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs` (or the existing registration test file — verify name) and a retry integration test in `AdbCore.Tests/Execution/`.

- [ ] **Step 1: Write the failing registration + retry tests**

Create `AdbCore.Tests/Actions/BuiltIn/ScreenRegistrationTests.cs` (both registries expose `bool TryGet(string, out ...)`):

```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class ScreenRegistrationTests
{
    [Fact]
    public void FindImage_IsRegistered_AsDefinitionAndExecutor()
    {
        var defs = new ActionRegistry();
        var execs = new ActionExecutorRegistry();
        BuiltInActions.Register(defs, execs);

        Assert.True(defs.TryGet("screen.findImage", out _));
        Assert.True(execs.TryGet("screen.findImage", out var exec) && exec is not null);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test AdbCore.Tests/AdbCore.Tests.csproj --filter "FullyQualifiedName~ScreenRegistrationTests"`
Expected: FAIL (`screen.findImage` not registered).

- [ ] **Step 3: Register Find Image**

In `AdbCore/Actions/BuiltIn/BuiltInActions.cs`, add `using AdbCore.Screen;` and register after the Input actions:

```csharp
        // Screen actions share one capture + matcher + RNG (OpenCvSharp/Win32 adapters; foreground-bound).
        var windowCapture = new Win32WindowCapture();
        var templateMatcher = new OpenCvSharpTemplateMatcher();
        var randomSource = new SystemRandomSource();
        Add(new FindImageAction(windowCapture, templateMatcher, randomSource), definitions, executors);
```

- [ ] **Step 4: Run tests — expect green**

Run: `dotnet test AdbCore.Tests/AdbCore.Tests.csproj --filter "FullyQualifiedName~ScreenRegistrationTests"`
Expected: PASS.

- [ ] **Step 5: Add a retry integration test (Find Image keeps trying then succeeds)**

A stateful fake matcher returns `null` for the first 2 attempts then a hit; the action carries a 3-attempt RetryPolicy. Register Start/End/Find Image **individually** with fake screen deps (`ActionExecutorRegistry.Register` throws on duplicate keys, and this avoids the real OpenCvSharp adapter). Reaching End (`result.Success`) is only possible if the engine retried past the misses.

Create `AdbCore.Tests/Execution/FindImageRetryTests.cs`:

```csharp
using System.Drawing;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Screen;
using Xunit;

namespace AdbCore.Tests.Execution;

public class FindImageRetryTests
{
    private sealed class FlakyMatcher(int failuresBeforeHit, MatchResult hit) : ITemplateMatcher
    {
        private int _calls;
        public MatchResult? Match(Bitmap haystack, string templatePath, double minConfidence)
            => ++_calls > failuresBeforeHit ? hit : null;
    }

    private sealed class StubCapture : IWindowCapture
    {
        public Bitmap Capture(IntPtr windowHandle, ScreenCaptureMethod method) => new(64, 64);
    }

    [Fact]
    public async Task FindImage_WithRetry_KeepsTryingUntilFound()
    {
        var targetId = Guid.NewGuid();
        var execs = new ActionExecutorRegistry();
        execs.Register(new StartAction());
        execs.Register(new EndAction());
        execs.Register(new FindImageAction(new StubCapture(), new FlakyMatcher(2, new MatchResult(10, 10, 4, 4, 0.99)), new SystemRandomSource()));

        var start = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.start" };
        var find = new BotAction
        {
            Id = Guid.NewGuid(), TypeKey = "screen.findImage", TargetId = targetId,
            Config = { [FindImageAction.TemplatePathKey] = "x.png" },
            Retry = new RetryPolicy { MaxAttempts = 3, DelayMs = 0 },
        };
        var end = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.end" };

        var bot = new Bot { Actions = { start, find, end }, Connections =
        {
            new ActionConnection { SourceActionId = start.Id, SourcePort = "out", TargetActionId = find.Id, TargetPort = "in" },
            new ActionConnection { SourceActionId = find.Id, SourcePort = "onSuccess", TargetActionId = end.Id, TargetPort = "in" },
        } };

        var options = new ExecutionOptions
        {
            ResolvedTargets = new Dictionary<Guid, ResolvedTarget>
            {
                [targetId] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "hwnd:1", Handle = (IntPtr)5 },
            },
        };

        var result = await new BotExecutor(execs).RunAsync(bot, options, null, default);

        // Reaching End (Success) is only possible because the action retried past the 2 misses to the hit.
        Assert.True(result.Success);
    }
}
```

- [ ] **Step 6: Run the full suite**

Run: `dotnet test ADB.slnx`
Expected: all green.

- [ ] **Step 7: Commit**

```bash
git add AdbCore/Actions/BuiltIn/BuiltInActions.cs AdbCore.Tests/Actions/BuiltIn/ScreenRegistrationTests.cs AdbCore.Tests/Execution/FindImageRetryTests.cs
git commit -m "feat(actions): register Find Image; cover retry-until-found integration"
```

---

## Final verification

- [ ] `dotnet test ADB.slnx` — all green (interpolation + screen infra + Find Image + retry + smoke test).
- [ ] `dotnet build BotBuilder/BotBuilder.csproj` — the new ImagePath/Number/Enum fields surface in the Properties Panel with no shell change (they reuse existing field templates).

### Manual Verification Checklist (user)
1. A bot: Window target + **Find Image** (template captured from that window) → routes `onSuccess` when present, `onFailure` when absent.
2. Raising **Confidence** past the real score flips success→failure; a **RetryPolicy** makes it keep trying until the image appears.
3. **Masking flow:** Find Image → Click `X=${matchRandX}, Y=${matchRandY}` clicks inside the matched region; repeated runs land on different in-region points.
4. `Auto` capture works for a normal app; `BitBlt` override works for a foreground GPU/DirectX window.
5. **ROI:** an ROI around the expected location still finds the image and is noticeably faster on a large/maximized window; `matchCenterX/Y` match a full-window search; an ROI excluding the image yields `onFailure`.
