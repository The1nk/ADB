# M10a — Android Image Actions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `android.findImage`, `android.waitForImage`, and `android.assertImageAbsent` — Android-target template-matching actions that mirror the existing Screen image actions exactly, capturing via the bound `IAndroidDevice` framebuffer.

**Architecture:** Extract the capture-source-independent template-matching logic (ROI resolution, crop+match+offset, match-variable writing, shared config keys/fields) out of `ScreenActionBase` into a new static `TemplateMatchCore`. `ScreenActionBase` is refactored to delegate to it (keeping its public surface so all existing Screen tests pass unchanged). A new `AndroidImageActionBase` (extending `AndroidActionBase`) captures the device framebuffer, decodes the PNG to a `Bitmap`, and calls the same `TemplateMatchCore`, guaranteeing an identical output contract by construction. No `captureMethod` field on the Android variants.

**Tech Stack:** C# / .NET 10, AdbCore class library, xUnit, OpenCvSharp (via existing `ITemplateMatcher`), System.Drawing (`Bitmap`).

**Reference spec:** `Docs/Specs/2026-06-04-m10-android-visual-coord-picker-design.md` (§3).

**Merge handling:** M10a is AdbCore-only logic with no visual surface; once green it is created AND merged via the `gh` CLI (PR link + test output surfaced), not parked for user visual verification.

---

## File Structure

**Create:**
- `AdbCore/Actions/BuiltIn/TemplateMatchCore.cs` — shared, capture-source-independent match core (keys, fields, ROI, match, variable writing).
- `AdbCore/Actions/BuiltIn/Android/AndroidImageActionBase.cs` — Android base: framebuffer capture + decode + delegate to `TemplateMatchCore`.
- `AdbCore/Actions/BuiltIn/Android/AndroidFindImageAction.cs`
- `AdbCore/Actions/BuiltIn/Android/AndroidWaitForImageAction.cs`
- `AdbCore/Actions/BuiltIn/Android/AndroidAssertImageAbsentAction.cs`
- `AdbCore.Tests/Actions/BuiltIn/TemplateMatchCoreTests.cs`
- `AdbCore.Tests/Actions/BuiltIn/Android/AndroidImageActionBaseTests.cs`
- `AdbCore.Tests/Actions/BuiltIn/Android/AndroidImageActionTests.cs` — Find/Wait/AssertAbsent behaviour.
- `AdbCore.Tests/Actions/BuiltIn/Android/AndroidImageRegistrationTests.cs`

**Modify:**
- `AdbCore/Actions/BuiltIn/ScreenActionBase.cs` — delegate ROI + variable-writing to `TemplateMatchCore`; keep `CaptureRegion` (used by `ScreenshotAction`) and all public consts/methods.
- `AdbCore/Actions/BuiltIn/BuiltInActions.cs` — register the three new actions.
- `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs` — counts 31→34 defs, 28→31 execs.
- `BotBuilder.Core.Tests/PaletteViewModelTests.cs` — total 31→34, Android 6→9.

---

## Task 1: Extract `TemplateMatchCore` with its own tests

**Files:**
- Create: `AdbCore/Actions/BuiltIn/TemplateMatchCore.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/TemplateMatchCoreTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `AdbCore.Tests/Actions/BuiltIn/TemplateMatchCoreTests.cs`:

```csharp
using System.Collections.Generic;
using System.Drawing;
using AdbCore.Actions.BuiltIn;
using AdbCore.Screen;
using AdbCore.Tests.Screen;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class TemplateMatchCoreTests
{
    [Fact]
    public void MatchInRegion_NoRegion_PassesFullHaystack_AndReturnsMatchUnchanged()
    {
        using var haystack = new Bitmap(1920, 1080);
        var matcher = new FakeTemplateMatcher(new MatchResult(50, 60, 10, 8, 0.95));

        var result = TemplateMatchCore.MatchInRegion(haystack, new Dictionary<string, object>(), matcher, "t.png", 0.8);

        Assert.Equal(1920, matcher.LastHaystackWidth);
        Assert.Equal(1080, matcher.LastHaystackHeight);
        Assert.Equal(new MatchResult(50, 60, 10, 8, 0.95), result);
    }

    [Fact]
    public void MatchInRegion_WithRegion_CropsHaystack_AndOffsetsResultBack()
    {
        using var haystack = new Bitmap(1920, 1080);
        var matcher = new FakeTemplateMatcher(new MatchResult(5, 7, 10, 8, 0.9));
        var config = new Dictionary<string, object>
        {
            [TemplateMatchCore.RegionXKey] = 100, [TemplateMatchCore.RegionYKey] = 40,
            [TemplateMatchCore.RegionWidthKey] = 300, [TemplateMatchCore.RegionHeightKey] = 200,
        };

        var result = TemplateMatchCore.MatchInRegion(haystack, config, matcher, "t.png", 0.8);

        Assert.Equal(300, matcher.LastHaystackWidth);
        Assert.Equal(200, matcher.LastHaystackHeight);
        Assert.Equal(new MatchResult(105, 47, 10, 8, 0.9), result); // 5+100, 7+40
    }

    [Fact]
    public void MatchInRegion_RegionClampedToHaystack()
    {
        using var haystack = new Bitmap(200, 150);
        var matcher = new FakeTemplateMatcher(new MatchResult(0, 0, 1, 1, 0.9));
        var config = new Dictionary<string, object>
        {
            [TemplateMatchCore.RegionXKey] = 180, [TemplateMatchCore.RegionYKey] = 140,
            [TemplateMatchCore.RegionWidthKey] = 999, [TemplateMatchCore.RegionHeightKey] = 999,
        };

        TemplateMatchCore.MatchInRegion(haystack, config, matcher, "t.png", 0.8);

        Assert.Equal(20, matcher.LastHaystackWidth);  // 200-180
        Assert.Equal(10, matcher.LastHaystackHeight); // 150-140
    }

    [Fact]
    public void WriteMatchVariables_WritesEdgesCenterRandomAndScore()
    {
        var vars = new Dictionary<string, object>();

        TemplateMatchCore.WriteMatchVariables(vars, new MatchResult(100, 40, 30, 20, 0.97), "match", new FixedRandomSource(123));

        Assert.Equal("100", vars["matchLeft"]);
        Assert.Equal("40", vars["matchTop"]);
        Assert.Equal("130", vars["matchRight"]);
        Assert.Equal("60", vars["matchBottom"]);
        Assert.Equal("115", vars["matchCenterX"]);
        Assert.Equal("50", vars["matchCenterY"]);
        Assert.Equal("123", vars["matchRandX"]);
        Assert.Equal("123", vars["matchRandY"]);
        Assert.Equal("0.97", vars["matchConfidence"]);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~TemplateMatchCoreTests"`
Expected: FAIL to compile — `TemplateMatchCore` does not exist.

- [ ] **Step 3: Create `TemplateMatchCore`**

Create `AdbCore/Actions/BuiltIn/TemplateMatchCore.cs`:

```csharp
using System.Drawing;
using System.Globalization;
using AdbCore.Actions;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Capture-source-independent template-matching core shared by the Screen (HWND capture) and
/// Android (framebuffer capture) image actions: the shared config keys/fields, ROI resolution, the
/// crop + match + offset-back step, and writing match variables. Keeps the two action families'
/// output contracts identical by construction.</summary>
public static class TemplateMatchCore
{
    public const string TemplatePathKey = "templatePath";
    public const string ConfidenceKey = "confidence";
    public const string ResultVarKey = "resultVar";
    public const string RegionXKey = "regionX";
    public const string RegionYKey = "regionY";
    public const string RegionWidthKey = "regionWidth";
    public const string RegionHeightKey = "regionHeight";
    public const string SuccessPort = "onSuccess";
    public const string FailurePort = "onFailure";
    public const double DefaultConfidence = 0.8;
    public const string DefaultResultVar = "match";

    public static ConfigField TemplatePathField() => new() { Key = TemplatePathKey, Label = "Template Image", Type = ConfigFieldType.ImagePath };
    public static ConfigField ConfidenceField() => new() { Key = ConfidenceKey, Label = "Confidence", Type = ConfigFieldType.Number, DefaultValue = DefaultConfidence };
    public static ConfigField ResultVarField() => new() { Key = ResultVarKey, Label = "Result Variable", Type = ConfigFieldType.String, DefaultValue = DefaultResultVar };

    /// <summary>The four ROI fields, shown after an action's own fields.</summary>
    public static IEnumerable<ConfigField> RegionFields() =>
    [
        new ConfigField { Key = RegionXKey, Label = "Region X", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField { Key = RegionYKey, Label = "Region Y", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField { Key = RegionWidthKey, Label = "Region Width", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField { Key = RegionHeightKey, Label = "Region Height", Type = ConfigFieldType.Number, DefaultValue = 0 },
    ];

    /// <summary>Reads + clamps the ROI fields against the haystack size; null when no usable region.</summary>
    public static Rectangle? ResolveRegion(IReadOnlyDictionary<string, object> config, int width, int height)
    {
        var w = ConfigValues.GetInt(config, RegionWidthKey, 0);
        var h = ConfigValues.GetInt(config, RegionHeightKey, 0);
        if (w <= 0 || h <= 0 || width <= 0 || height <= 0)
        {
            return null;
        }

        var x = Math.Clamp(ConfigValues.GetInt(config, RegionXKey, 0), 0, width - 1);
        var y = Math.Clamp(ConfigValues.GetInt(config, RegionYKey, 0), 0, height - 1);
        w = Math.Min(w, width - x);
        h = Math.Min(h, height - y);
        return w > 0 && h > 0 ? new Rectangle(x, y, w, h) : null;
    }

    /// <summary>Crops the haystack to the configured ROI (if any), matches the template, and returns the
    /// match in full-haystack coordinates (null when none ≥ confidence). Does not dispose the haystack.</summary>
    public static MatchResult? MatchInRegion(Bitmap haystack, IReadOnlyDictionary<string, object> config, ITemplateMatcher matcher, string templatePath, double confidence)
    {
        var region = ResolveRegion(config, haystack.Width, haystack.Height);
        if (region is not Rectangle roi)
        {
            return matcher.Match(haystack, templatePath, confidence);
        }

        using var crop = haystack.Clone(roi, haystack.PixelFormat);
        var hit = matcher.Match(crop, templatePath, confidence);
        return hit is MatchResult m ? m with { X = m.X + roi.X, Y = m.Y + roi.Y } : null;
    }

    /// <summary>Writes a match's region edges, center, a random in-region point, and the score to
    /// <paramref name="variables"/> under <paramref name="prefix"/> (integers as InvariantCulture strings).</summary>
    public static void WriteMatchVariables(IDictionary<string, object> variables, MatchResult m, string prefix, IRandomSource random)
    {
        var left = m.X;
        var top = m.Y;
        var right = m.X + m.Width;
        var bottom = m.Y + m.Height;
        variables[$"{prefix}Left"] = Str(left);
        variables[$"{prefix}Top"] = Str(top);
        variables[$"{prefix}Right"] = Str(right);
        variables[$"{prefix}Bottom"] = Str(bottom);
        variables[$"{prefix}CenterX"] = Str(m.X + m.Width / 2);
        variables[$"{prefix}CenterY"] = Str(m.Y + m.Height / 2);
        variables[$"{prefix}RandX"] = Str(random.Next(left, right));
        variables[$"{prefix}RandY"] = Str(random.Next(top, bottom));
        variables[$"{prefix}Confidence"] = m.Score.ToString(CultureInfo.InvariantCulture);
    }

    private static string Str(int v) => v.ToString(CultureInfo.InvariantCulture);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~TemplateMatchCoreTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Actions/BuiltIn/TemplateMatchCore.cs AdbCore.Tests/Actions/BuiltIn/TemplateMatchCoreTests.cs
git commit -m "feat(screen): extract shared TemplateMatchCore (ROI + match + variable writing)"
```

---

## Task 2: Refactor `ScreenActionBase` to delegate to `TemplateMatchCore`

Goal: route ROI resolution, match-offset, and variable writing through `TemplateMatchCore` while keeping `ScreenActionBase`'s public surface (const aliases, `CaptureRegion`, `CaptureAndMatch`, `WriteMatchVariables`, `ResolveRegion`) so every existing Screen test passes unchanged. This task adds NO new test — the existing Screen test suite is the safety net.

**Files:**
- Modify: `AdbCore/Actions/BuiltIn/ScreenActionBase.cs`

- [ ] **Step 1: Replace the shared-key consts and field factories with aliases to `TemplateMatchCore`**

In `ScreenActionBase.cs`, replace the const block (lines 15–28) so that `CaptureMethodKey` stays local and the shared keys alias the core (preserves `ScreenActionBase.RegionWidthKey` etc. references in tests):

```csharp
    public const string CaptureMethodKey = "captureMethod";
    public const string RegionXKey = TemplateMatchCore.RegionXKey;
    public const string RegionYKey = TemplateMatchCore.RegionYKey;
    public const string RegionWidthKey = TemplateMatchCore.RegionWidthKey;
    public const string RegionHeightKey = TemplateMatchCore.RegionHeightKey;
    public const string SuccessPort = TemplateMatchCore.SuccessPort;
    public const string FailurePort = TemplateMatchCore.FailurePort;

    public const string TemplatePathKey = TemplateMatchCore.TemplatePathKey;
    public const string ConfidenceKey = TemplateMatchCore.ConfidenceKey;
    public const string ResultVarKey = TemplateMatchCore.ResultVarKey;
    public const double DefaultConfidence = TemplateMatchCore.DefaultConfidence;
    public const string DefaultResultVar = TemplateMatchCore.DefaultResultVar;
```

- [ ] **Step 2: Use `TemplateMatchCore.RegionFields()` in `ConfigFields` and forward the field factories**

Replace the four inline Region `ConfigField`s in the `ConfigFields` collection expression (lines 59–62) with:

```csharp
        .. TemplateMatchCore.RegionFields(),
```

Replace the three `protected static ConfigField ...Field()` factory bodies (lines 68–70) with forwarders:

```csharp
    protected static ConfigField TemplatePathField() => TemplateMatchCore.TemplatePathField();
    protected static ConfigField ConfidenceField() => TemplateMatchCore.ConfidenceField();
    protected static ConfigField ResultVarField() => TemplateMatchCore.ResultVarField();
```

- [ ] **Step 3: Forward `ResolveRegion`, `CaptureAndMatch`, and `WriteMatchVariables` to the core**

Replace the `ResolveRegion` method (lines 90–104) with a forwarder (note: `CaptureRegion` calls this, so it keeps working for `ScreenshotAction`):

```csharp
    /// <summary>Reads + clamps the ROI fields against the client size; null when no usable region.</summary>
    protected static Rectangle? ResolveRegion(ActionExecutionContext context, int clientWidth, int clientHeight)
        => TemplateMatchCore.ResolveRegion(context.Action.Config, clientWidth, clientHeight);
```

Replace `CaptureAndMatch` (lines 129–134) so the crop+match+offset comes from the core (capture stays HWND-specific here):

```csharp
    /// <summary>Captures the window's client area via the chosen method, then crops to any ROI, matches the
    /// template, and returns the match in full-window client coordinates (null if none ≥ confidence).</summary>
    protected MatchResult? CaptureAndMatch(ActionExecutionContext context, IntPtr hwnd, ITemplateMatcher matcher, string templatePath, double confidence)
    {
        using var shot = _capture.Capture(hwnd, CaptureMethodOf(context));
        return TemplateMatchCore.MatchInRegion(shot, context.Action.Config, matcher, templatePath, confidence);
    }
```

Replace `WriteMatchVariables` (lines 138–154) and delete the now-unused private `Str` helper (line 156), forwarding to the core:

```csharp
    /// <summary>Writes a match's region edges, center, a random in-region point, and the score to run
    /// variables under <paramref name="prefix"/> (all client-relative integers, as InvariantCulture strings).</summary>
    protected static void WriteMatchVariables(ActionExecutionContext context, MatchResult m, string prefix, IRandomSource random)
        => TemplateMatchCore.WriteMatchVariables(context.Context.Variables, m, prefix, random);
```

Leave `CaptureRegion` (lines 106–125), `CaptureMethodOf`, `ResolveWindow`, and all abstract members untouched. Remove the now-unused `using System.Globalization;` if the compiler warns it is unused.

- [ ] **Step 4: Run the full Screen + execution test set to verify nothing regressed**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~ScreenActionBaseTests|FullyQualifiedName~FindImageActionTests|FullyQualifiedName~WaitForImageActionTests|FullyQualifiedName~AssertImageAbsentActionTests|FullyQualifiedName~ScreenshotActionTests|FullyQualifiedName~FindImageRetryTests|FullyQualifiedName~BotExecutorInterpolationTests|FullyQualifiedName~ScreenRegistrationTests"`
Expected: PASS (all existing Screen + retry + interpolation + registration tests green).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Actions/BuiltIn/ScreenActionBase.cs
git commit -m "refactor(screen): ScreenActionBase delegates ROI/match/variables to TemplateMatchCore"
```

---

## Task 3: `AndroidImageActionBase` + base test

**Files:**
- Create: `AdbCore/Actions/BuiltIn/Android/AndroidImageActionBase.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/Android/AndroidImageActionBaseTests.cs`

- [ ] **Step 1: Write the failing test**

Create `AdbCore.Tests/Actions/BuiltIn/Android/AndroidImageActionBaseTests.cs`. It defines a tiny concrete subclass exposing `CaptureAndMatch`, and a PNG-bytes helper so the framebuffer decodes to a known size:

```csharp
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using AdbCore.Actions.BuiltIn;
using AdbCore.Actions.BuiltIn.Android;
using AdbCore.Android;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Screen;
using AdbCore.Tests.Screen;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn.Android;

public class AndroidImageActionBaseTests
{
    private sealed class TestAndroidImageAction(ITemplateMatcher matcher) : AndroidImageActionBase
    {
        private readonly ITemplateMatcher _matcher = matcher;
        public override string TypeKey => "android.test";
        public override string DisplayName => "Test Android Image";
        public override string Description => "";
        protected override IEnumerable<ConfigField> ActionConfigFields => [];
        public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct) => Task.FromResult(ActionResult.Ok(SuccessPort));

        public MatchResult? CallCaptureAndMatch(ActionExecutionContext ctx, IAndroidDevice device, string template, double confidence)
            => CaptureAndMatch(ctx, device, _matcher, template, confidence);
    }

    internal static byte[] PngBytes(int w, int h)
    {
        using var bmp = new Bitmap(w, h);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static ActionExecutionContext Exec(BotAction action) => new(action, new BotExecutionContext(), _ => { });

    [Fact]
    public void CaptureAndMatch_NoRegion_PassesFullFrame_AndReturnsMatchUnchanged()
    {
        var device = new FakeAndroidDevice { ScreenshotBytes = PngBytes(1080, 1920) };
        var matcher = new FakeTemplateMatcher(new MatchResult(50, 60, 10, 8, 0.95));
        var action = new TestAndroidImageAction(matcher);

        var result = action.CallCaptureAndMatch(Exec(new BotAction()), device, "t.png", 0.8);

        Assert.Equal(1080, matcher.LastHaystackWidth);
        Assert.Equal(1920, matcher.LastHaystackHeight);
        Assert.Equal(new MatchResult(50, 60, 10, 8, 0.95), result);
        Assert.Contains("screenshot", device.Calls);
    }

    [Fact]
    public void CaptureAndMatch_WithRegion_CropsFrame_AndOffsetsResultBack()
    {
        var device = new FakeAndroidDevice { ScreenshotBytes = PngBytes(1080, 1920) };
        var matcher = new FakeTemplateMatcher(new MatchResult(5, 7, 10, 8, 0.9));
        var action = new TestAndroidImageAction(matcher);
        var botAction = new BotAction { Config =
        {
            [TemplateMatchCore.RegionXKey] = 100, [TemplateMatchCore.RegionYKey] = 40,
            [TemplateMatchCore.RegionWidthKey] = 300, [TemplateMatchCore.RegionHeightKey] = 200,
        } };

        var result = action.CallCaptureAndMatch(Exec(botAction), device, "t.png", 0.8);

        Assert.Equal(300, matcher.LastHaystackWidth);
        Assert.Equal(200, matcher.LastHaystackHeight);
        Assert.Equal(new MatchResult(105, 47, 10, 8, 0.9), result);
    }

    [Fact]
    public void Definition_HasRegionFields_ButNoCaptureMethod_AndSupportsRetry()
    {
        var action = new TestAndroidImageAction(new FakeTemplateMatcher(null));
        Assert.Equal("Android", action.Category);
        Assert.True(action.SupportsRetry);
        Assert.Contains(action.ConfigFields, f => f.Key == TemplateMatchCore.RegionWidthKey);
        Assert.DoesNotContain(action.ConfigFields, f => f.Key == ScreenActionBase.CaptureMethodKey);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~AndroidImageActionBaseTests"`
Expected: FAIL to compile — `AndroidImageActionBase` does not exist.

- [ ] **Step 3: Create `AndroidImageActionBase`**

Create `AdbCore/Actions/BuiltIn/Android/AndroidImageActionBase.cs`:

```csharp
using System.Drawing;
using System.IO;
using AdbCore.Android;
using AdbCore.Execution;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Shared base for Android image-matching actions: captures the bound device's framebuffer,
/// decodes it, and runs template matching via the shared <see cref="TemplateMatchCore"/> so the output
/// contract matches the Screen image actions exactly. Exposes the ROI fields but NO Capture Method field
/// (the framebuffer has no BitBlt/PrintWindow variants).</summary>
public abstract class AndroidImageActionBase : AndroidActionBase
{
    private List<ConfigField>? _configFields;

    public override bool SupportsRetry => true;

    /// <summary>The action's own config fields, shown before the shared ROI fields.</summary>
    protected abstract IEnumerable<ConfigField> ActionConfigFields { get; }

    public override List<ConfigField> ConfigFields => _configFields ??=
    [
        .. ActionConfigFields,
        .. TemplateMatchCore.RegionFields(),
    ];

    /// <summary>Captures the device framebuffer, crops to any ROI, matches the template, and returns the
    /// match in full-frame device-pixel coordinates (null if none ≥ confidence).</summary>
    protected static MatchResult? CaptureAndMatch(ActionExecutionContext context, IAndroidDevice device, ITemplateMatcher matcher, string templatePath, double confidence)
    {
        using var ms = new MemoryStream(device.Screenshot());
        using var frame = new Bitmap(ms);
        return TemplateMatchCore.MatchInRegion(frame, context.Action.Config, matcher, templatePath, confidence);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~AndroidImageActionBaseTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Actions/BuiltIn/Android/AndroidImageActionBase.cs AdbCore.Tests/Actions/BuiltIn/Android/AndroidImageActionBaseTests.cs
git commit -m "feat(android): AndroidImageActionBase (framebuffer capture + shared match core)"
```

---

## Task 4: `AndroidFindImageAction`

**Files:**
- Create: `AdbCore/Actions/BuiltIn/Android/AndroidFindImageAction.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/Android/AndroidImageActionTests.cs` (created here; Tasks 5–6 append to it)

- [ ] **Step 1: Write the failing test**

Create `AdbCore.Tests/Actions/BuiltIn/Android/AndroidImageActionTests.cs`:

```csharp
using System.Collections.Generic;
using AdbCore.Actions.BuiltIn;
using AdbCore.Actions.BuiltIn.Android;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Screen;
using AdbCore.Tests.Screen;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn.Android;

public class AndroidImageActionTests
{
    private static (ActionExecutionContext ctx, FakeAndroidDevice dev) WithDevice(BotAction action, int w = 1080, int h = 1920)
    {
        var dev = new FakeAndroidDevice { ScreenshotBytes = AndroidImageActionBaseTests.PngBytes(w, h) };
        var ctx = new BotExecutionContext();
        var id = action.TargetId ?? Guid.NewGuid();
        action.TargetId = id;
        ctx.Targets[id] = new ResolvedTarget { Type = BotTargetType.AndroidDevice, Selector = "serial:x", Handle = dev };
        return (new ActionExecutionContext(action, ctx, _ => { }), dev);
    }

    private static AndroidFindImageAction Find(MatchResult? result, int rand = 0)
        => new(new FakeTemplateMatcher(result), new FixedRandomSource(rand));

    [Fact]
    public async Task Find_Match_WritesAllVariables_AndRoutesSuccess()
    {
        var action = new BotAction { Config = { [TemplateMatchCore.TemplatePathKey] = "btn.png" } };
        var (ctx, _) = WithDevice(action);

        var result = await Find(new MatchResult(100, 40, 30, 20, 0.97), rand: 123).ExecuteAsync(ctx, default);

        Assert.True(result.Success);
        Assert.Equal("onSuccess", result.OutputPort);
        Assert.Equal("100", ctx.Context.Variables["matchLeft"]);
        Assert.Equal("130", ctx.Context.Variables["matchRight"]);
        Assert.Equal("115", ctx.Context.Variables["matchCenterX"]);
        Assert.Equal("123", ctx.Context.Variables["matchRandX"]);
        Assert.Equal("0.97", ctx.Context.Variables["matchConfidence"]);
    }

    [Fact]
    public async Task Find_CustomResultVar_PrefixesVariables()
    {
        var action = new BotAction { Config =
        {
            [TemplateMatchCore.TemplatePathKey] = "btn.png",
            [TemplateMatchCore.ResultVarKey] = "btn",
        } };
        var (ctx, _) = WithDevice(action);

        await Find(new MatchResult(1, 2, 4, 6, 0.9)).ExecuteAsync(ctx, default);

        Assert.Equal("3", ctx.Context.Variables["btnCenterX"]); // 1 + 4/2
        Assert.True(ctx.Context.Variables.ContainsKey("btnConfidence"));
    }

    [Fact]
    public async Task Find_NoMatch_Fails_WritesNothing()
    {
        var action = new BotAction { Config = { [TemplateMatchCore.TemplatePathKey] = "btn.png" } };
        var (ctx, _) = WithDevice(action);

        var result = await Find(null).ExecuteAsync(ctx, default);

        Assert.False(result.Success);
        Assert.Empty(ctx.Context.Variables);
    }

    [Fact]
    public async Task Find_NoDevice_Fails()
    {
        var exec = new ActionExecutionContext(new BotAction { Config = { [TemplateMatchCore.TemplatePathKey] = "btn.png" } }, new BotExecutionContext(), _ => { });
        var result = await Find(new MatchResult(0, 0, 1, 1, 1)).ExecuteAsync(exec, default);
        Assert.False(result.Success);
        Assert.Contains("Android", result.ErrorMessage);
    }

    [Fact]
    public async Task Find_BlankTemplatePath_Fails()
    {
        var action = new BotAction();
        var (ctx, _) = WithDevice(action);
        var result = await Find(new MatchResult(0, 0, 1, 1, 1)).ExecuteAsync(ctx, default);
        Assert.False(result.Success);
        Assert.Contains("template", result.ErrorMessage);
    }

    [Fact]
    public void Find_Definition_Metadata()
    {
        var def = Find(null);
        Assert.Equal("android.findImage", def.TypeKey);
        Assert.Equal("Find Image (Android)", def.DisplayName);
        Assert.Equal("Android", def.Category);
        Assert.Equal(new[] { "onSuccess", "onFailure" }, def.OutputPorts.Select(p => p.Name));
        Assert.True(def.SupportsRetry);
        Assert.Contains(def.ConfigFields, f => f.Key == TemplateMatchCore.RegionWidthKey);
        Assert.DoesNotContain(def.ConfigFields, f => f.Key == ScreenActionBase.CaptureMethodKey);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~AndroidImageActionTests"`
Expected: FAIL to compile — `AndroidFindImageAction` does not exist.

- [ ] **Step 3: Create `AndroidFindImageAction`**

Create `AdbCore/Actions/BuiltIn/Android/AndroidFindImageAction.cs`:

```csharp
using AdbCore.Execution;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Finds a template image on the bound device's screen and writes its location (region edges,
/// center, a random in-region point, and the score) to run variables under a configurable prefix.
/// "Not found" is a failed result so the engine can retry (per RetryPolicy) and route onFailure.</summary>
public sealed class AndroidFindImageAction : AndroidImageActionBase
{
    private readonly ITemplateMatcher _matcher;
    private readonly IRandomSource _random;

    public AndroidFindImageAction(ITemplateMatcher matcher, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(matcher);
        ArgumentNullException.ThrowIfNull(random);
        _matcher = matcher;
        _random = random;
    }

    public override string TypeKey => "android.findImage";
    public override string DisplayName => "Find Image (Android)";
    public override string Description => "Finds a template image on the device screen and writes its location to variables.";

    protected override IEnumerable<ConfigField> ActionConfigFields =>
    [
        TemplateMatchCore.TemplatePathField(),
        TemplateMatchCore.ConfidenceField(),
        TemplateMatchCore.ResultVarField(),
    ];

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return Task.FromResult(RequiresDevice());
        }

        var templatePath = ConfigValues.GetString(context.Action.Config, TemplateMatchCore.TemplatePathKey);
        if (string.IsNullOrWhiteSpace(templatePath))
        {
            return Task.FromResult(ActionResult.Fail("Find Image (Android): a template image path is required."));
        }

        var confidence = ConfigValues.GetDouble(context.Action.Config, TemplateMatchCore.ConfidenceKey, TemplateMatchCore.DefaultConfidence);
        var prefix = ConfigValues.GetString(context.Action.Config, TemplateMatchCore.ResultVarKey, TemplateMatchCore.DefaultResultVar);
        if (string.IsNullOrWhiteSpace(prefix))
        {
            prefix = TemplateMatchCore.DefaultResultVar;
        }

        if (CaptureAndMatch(context, device, _matcher, templatePath, confidence) is not MatchResult m)
        {
            return Task.FromResult(ActionResult.Fail("Find Image (Android): no match at or above the configured confidence."));
        }

        TemplateMatchCore.WriteMatchVariables(context.Context.Variables, m, prefix, _random);
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~AndroidImageActionTests"`
Expected: PASS (6 Find tests).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Actions/BuiltIn/Android/AndroidFindImageAction.cs AdbCore.Tests/Actions/BuiltIn/Android/AndroidImageActionTests.cs
git commit -m "feat(android): android.findImage action"
```

---

## Task 5: `AndroidWaitForImageAction`

**Files:**
- Create: `AdbCore/Actions/BuiltIn/Android/AndroidWaitForImageAction.cs`
- Test: append to `AdbCore.Tests/Actions/BuiltIn/Android/AndroidImageActionTests.cs`

- [ ] **Step 1: Write the failing tests**

Append these methods inside the `AndroidImageActionTests` class (before its closing brace):

```csharp
    private static AndroidWaitForImageAction Wait(MatchResult? result, int rand = 0)
        => new(new FakeTemplateMatcher(result), new FixedRandomSource(rand));

    [Fact]
    public async Task Wait_ImagePresent_Succeeds_AndWritesVariables()
    {
        var action = new BotAction { Config =
        {
            [TemplateMatchCore.TemplatePathKey] = "btn.png",
            [AndroidWaitForImageAction.TimeoutMsKey] = 1000,
            [AndroidWaitForImageAction.PollIntervalMsKey] = 10,
        } };
        var (ctx, _) = WithDevice(action);

        var result = await Wait(new MatchResult(10, 20, 4, 6, 0.95)).ExecuteAsync(ctx, default);

        Assert.True(result.Success);
        Assert.Equal("onSuccess", result.OutputPort);
        Assert.Equal("10", ctx.Context.Variables["matchLeft"]);
    }

    [Fact]
    public async Task Wait_Timeout_Fails()
    {
        var action = new BotAction { Config =
        {
            [TemplateMatchCore.TemplatePathKey] = "btn.png",
            [AndroidWaitForImageAction.TimeoutMsKey] = 30,
            [AndroidWaitForImageAction.PollIntervalMsKey] = 10,
        } };
        var (ctx, _) = WithDevice(action);

        var result = await Wait(null).ExecuteAsync(ctx, default);

        Assert.False(result.Success);
        Assert.Contains("did not appear", result.ErrorMessage);
    }

    [Fact]
    public void Wait_Definition_Metadata()
    {
        var def = Wait(null);
        Assert.Equal("android.waitForImage", def.TypeKey);
        Assert.Equal("Wait for Image (Android)", def.DisplayName);
        Assert.Contains(def.ConfigFields, f => f.Key == AndroidWaitForImageAction.TimeoutMsKey);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~AndroidImageActionTests"`
Expected: FAIL to compile — `AndroidWaitForImageAction` does not exist.

- [ ] **Step 3: Create `AndroidWaitForImageAction`**

Create `AdbCore/Actions/BuiltIn/Android/AndroidWaitForImageAction.cs`:

```csharp
using System.Diagnostics;
using AdbCore.Execution;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Polls the device screen until the template appears or the timeout elapses. On success writes
/// the same match variables as Find Image (Android); on timeout returns a failed result.</summary>
public sealed class AndroidWaitForImageAction : AndroidImageActionBase
{
    public const string TimeoutMsKey = "timeoutMs";
    public const string PollIntervalMsKey = "pollIntervalMs";
    public const int DefaultTimeoutMs = 5000;
    public const int DefaultPollIntervalMs = 250;

    private readonly ITemplateMatcher _matcher;
    private readonly IRandomSource _random;

    public AndroidWaitForImageAction(ITemplateMatcher matcher, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(matcher);
        ArgumentNullException.ThrowIfNull(random);
        _matcher = matcher;
        _random = random;
    }

    public override string TypeKey => "android.waitForImage";
    public override string DisplayName => "Wait for Image (Android)";
    public override string Description => "Polls the device screen until the template appears or the timeout elapses.";

    protected override IEnumerable<ConfigField> ActionConfigFields =>
    [
        TemplateMatchCore.TemplatePathField(),
        TemplateMatchCore.ConfidenceField(),
        TemplateMatchCore.ResultVarField(),
        new ConfigField { Key = TimeoutMsKey, Label = "Timeout (ms)", Type = ConfigFieldType.Number, DefaultValue = DefaultTimeoutMs },
        new ConfigField { Key = PollIntervalMsKey, Label = "Poll Interval (ms)", Type = ConfigFieldType.Number, DefaultValue = DefaultPollIntervalMs },
    ];

    public override async Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return RequiresDevice();
        }

        var templatePath = ConfigValues.GetString(context.Action.Config, TemplateMatchCore.TemplatePathKey);
        if (string.IsNullOrWhiteSpace(templatePath))
        {
            return ActionResult.Fail("Wait for Image (Android): a template image path is required.");
        }

        var confidence = ConfigValues.GetDouble(context.Action.Config, TemplateMatchCore.ConfidenceKey, TemplateMatchCore.DefaultConfidence);
        var prefix = ConfigValues.GetString(context.Action.Config, TemplateMatchCore.ResultVarKey, TemplateMatchCore.DefaultResultVar);
        if (string.IsNullOrWhiteSpace(prefix))
        {
            prefix = TemplateMatchCore.DefaultResultVar;
        }

        var timeoutMs = Math.Max(0, ConfigValues.GetInt(context.Action.Config, TimeoutMsKey, DefaultTimeoutMs));
        var pollMs = Math.Max(1, ConfigValues.GetInt(context.Action.Config, PollIntervalMsKey, DefaultPollIntervalMs));

        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            if (CaptureAndMatch(context, device, _matcher, templatePath, confidence) is MatchResult m)
            {
                TemplateMatchCore.WriteMatchVariables(context.Context.Variables, m, prefix, _random);
                return ActionResult.Ok(SuccessPort);
            }

            if (elapsed.ElapsedMilliseconds >= timeoutMs)
            {
                return ActionResult.Fail($"Wait for Image (Android): template did not appear within {timeoutMs} ms.");
            }

            await Task.Delay(pollMs, ct);
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~AndroidImageActionTests"`
Expected: PASS (Find + Wait tests).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Actions/BuiltIn/Android/AndroidWaitForImageAction.cs AdbCore.Tests/Actions/BuiltIn/Android/AndroidImageActionTests.cs
git commit -m "feat(android): android.waitForImage action"
```

---

## Task 6: `AndroidAssertImageAbsentAction`

**Files:**
- Create: `AdbCore/Actions/BuiltIn/Android/AndroidAssertImageAbsentAction.cs`
- Test: append to `AdbCore.Tests/Actions/BuiltIn/Android/AndroidImageActionTests.cs`

- [ ] **Step 1: Write the failing tests**

Append inside the `AndroidImageActionTests` class:

```csharp
    private static AndroidAssertImageAbsentAction Absent(MatchResult? result)
        => new(new FakeTemplateMatcher(result));

    [Fact]
    public async Task Absent_TemplateMissing_Succeeds()
    {
        var action = new BotAction { Config = { [TemplateMatchCore.TemplatePathKey] = "btn.png" } };
        var (ctx, _) = WithDevice(action);

        var result = await Absent(null).ExecuteAsync(ctx, default);

        Assert.True(result.Success);
        Assert.Equal("onSuccess", result.OutputPort);
    }

    [Fact]
    public async Task Absent_TemplatePresent_Fails()
    {
        var action = new BotAction { Config = { [TemplateMatchCore.TemplatePathKey] = "btn.png" } };
        var (ctx, _) = WithDevice(action);

        var result = await Absent(new MatchResult(0, 0, 4, 4, 0.95)).ExecuteAsync(ctx, default);

        Assert.False(result.Success);
        Assert.Contains("present", result.ErrorMessage);
    }

    [Fact]
    public void Absent_Definition_Metadata()
    {
        var def = Absent(null);
        Assert.Equal("android.assertImageAbsent", def.TypeKey);
        Assert.Equal("Assert Image Absent (Android)", def.DisplayName);
        Assert.DoesNotContain(def.ConfigFields, f => f.Key == TemplateMatchCore.ResultVarKey);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~AndroidImageActionTests"`
Expected: FAIL to compile — `AndroidAssertImageAbsentAction` does not exist.

- [ ] **Step 3: Create `AndroidAssertImageAbsentAction`**

Create `AdbCore/Actions/BuiltIn/Android/AndroidAssertImageAbsentAction.cs`:

```csharp
using System.Globalization;
using AdbCore.Execution;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Succeeds only when the template is NOT found on the device screen. While the template is
/// present it returns a failed result, so with a RetryPolicy it becomes "wait until the image is gone",
/// and otherwise routes onFailure.</summary>
public sealed class AndroidAssertImageAbsentAction : AndroidImageActionBase
{
    private readonly ITemplateMatcher _matcher;

    public AndroidAssertImageAbsentAction(ITemplateMatcher matcher)
    {
        ArgumentNullException.ThrowIfNull(matcher);
        _matcher = matcher;
    }

    public override string TypeKey => "android.assertImageAbsent";
    public override string DisplayName => "Assert Image Absent (Android)";
    public override string Description => "Succeeds when the template is not present on the device screen.";

    protected override IEnumerable<ConfigField> ActionConfigFields =>
    [
        TemplateMatchCore.TemplatePathField(),
        TemplateMatchCore.ConfidenceField(),
    ];

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return Task.FromResult(RequiresDevice());
        }

        var templatePath = ConfigValues.GetString(context.Action.Config, TemplateMatchCore.TemplatePathKey);
        if (string.IsNullOrWhiteSpace(templatePath))
        {
            return Task.FromResult(ActionResult.Fail("Assert Image Absent (Android): a template image path is required."));
        }

        var confidence = ConfigValues.GetDouble(context.Action.Config, TemplateMatchCore.ConfidenceKey, TemplateMatchCore.DefaultConfidence);

        return CaptureAndMatch(context, device, _matcher, templatePath, confidence) is MatchResult m
            ? Task.FromResult(ActionResult.Fail($"Assert Image Absent (Android): template is present (score {m.Score.ToString(CultureInfo.InvariantCulture)})."))
            : Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~AndroidImageActionTests"`
Expected: PASS (Find + Wait + Absent tests).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Actions/BuiltIn/Android/AndroidAssertImageAbsentAction.cs AdbCore.Tests/Actions/BuiltIn/Android/AndroidImageActionTests.cs
git commit -m "feat(android): android.assertImageAbsent action"
```

---

## Task 7: Register the three actions + update counts

**Files:**
- Modify: `AdbCore/Actions/BuiltIn/BuiltInActions.cs`
- Create: `AdbCore.Tests/Actions/BuiltIn/Android/AndroidImageRegistrationTests.cs`
- Modify: `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs`
- Modify: `BotBuilder.Core.Tests/PaletteViewModelTests.cs`

- [ ] **Step 1: Write the failing registration test**

Create `AdbCore.Tests/Actions/BuiltIn/Android/AndroidImageRegistrationTests.cs`:

```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn.Android;

public class AndroidImageRegistrationTests
{
    [Theory]
    [InlineData("android.findImage")]
    [InlineData("android.waitForImage")]
    [InlineData("android.assertImageAbsent")]
    public void AndroidImageAction_IsRegistered_AsDefinitionAndExecutor(string typeKey)
    {
        var defs = new ActionRegistry();
        var execs = new ActionExecutorRegistry();
        BuiltInActions.Register(defs, execs);

        Assert.True(defs.TryGet(typeKey, out _));
        Assert.True(execs.TryGet(typeKey, out var exec) && exec is not null);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~AndroidImageRegistrationTests"`
Expected: FAIL — keys not registered (`TryGet` returns false).

- [ ] **Step 3: Register the three actions**

In `AdbCore/Actions/BuiltIn/BuiltInActions.cs`, immediately after the `AndroidScreenshotAction` registration (the line `Add(new AndroidScreenshotAction(), definitions, executors);`), add:

```csharp

        // Android image matching (handle-based device + injected matcher/RNG; mirrors Screen via TemplateMatchCore).
        Add(new AndroidFindImageAction(templateMatcher, randomSource), definitions, executors);
        Add(new AndroidWaitForImageAction(templateMatcher, randomSource), definitions, executors);
        Add(new AndroidAssertImageAbsentAction(templateMatcher), definitions, executors);
```

(`templateMatcher` and `randomSource` are already in scope from the Screen registration block above.)

- [ ] **Step 4: Update the count assertions**

In `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs`, change:

```csharp
        Assert.Equal(31, defs.Count);
        Assert.Equal(28, execs.Count);
```

to:

```csharp
        Assert.Equal(34, defs.Count);
        Assert.Equal(31, execs.Count);
```

In `BotBuilder.Core.Tests/PaletteViewModelTests.cs`, change the `ClearingSearch_RestoresAll` assertion:

```csharp
        Assert.Equal(31, palette.Categories.SelectMany(c => c.Items).Count()); // 7 Control Flow + 3 Data + 6 Input + 4 Screen + 6 Android + 5 Browser
```

to:

```csharp
        Assert.Equal(34, palette.Categories.SelectMany(c => c.Items).Count()); // 7 Control Flow + 3 Data + 6 Input + 4 Screen + 9 Android + 5 Browser
```

And in the `Categories_GroupBuiltInsByCategory` test, add an Android count assertion after the Input assertion (line 30):

```csharp
        var android = palette.Categories.Single(c => c.Name == "Android");
        Assert.Equal(9, android.Items.Count); // Tap, Swipe, Press Back, Launch App, Install APK, Screenshot, Find Image, Wait for Image, Assert Image Absent
```

- [ ] **Step 5: Run the registration + count tests to verify they pass**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~AndroidImageRegistrationTests|FullyQualifiedName~BuiltInActionsTests"`
Then: `dotnet test BotBuilder.Core.Tests --filter "FullyQualifiedName~PaletteViewModelTests"`
Expected: PASS for both.

- [ ] **Step 6: Commit**

```bash
git add AdbCore/Actions/BuiltIn/BuiltInActions.cs AdbCore.Tests/Actions/BuiltIn/Android/AndroidImageRegistrationTests.cs AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs BotBuilder.Core.Tests/PaletteViewModelTests.cs
git commit -m "feat(android): register android image actions + update palette/registry counts"
```

---

## Task 8: Full build + test sweep

**Files:** none (verification).

- [ ] **Step 1: Build the whole solution, treat warnings as failures**

Run: `dotnet build ADB.slnx`
Expected: Build succeeded, 0 warnings (the codebase has been kept warning-clean).

- [ ] **Step 2: Run the full test suite**

Run: `dotnet test ADB.slnx`
Expected: All tests pass (AdbCore.Tests +16: 4 TemplateMatchCore + 3 base + 12 action [6 Find, 3 Wait, 3 Absent] + 3 registration theory cases; minus none — existing Screen tests unchanged). Confirm 0 failures across all test projects.

- [ ] **Step 3: Final no-op commit check**

Run: `git status`
Expected: clean working tree (everything already committed in Tasks 1–7). If any stray formatting changed, commit it:

```bash
git commit -am "chore(android): formatting cleanup"
```

---

## Self-Review Notes (addressed)

- **Spec coverage (§3):** three actions (Tasks 4–6), framebuffer capture (Task 3), shared match core / identical output contract (Tasks 1–3), no `captureMethod` (Tasks 3, 4–6 metadata tests), confidence default 0.8 / ROI / `${var}` (inherited via `TemplateMatchCore` + `ConfigValues`, asserted in Task 4), miss semantics (Find Fail / Wait timeout / Absent present-Fail — Tasks 4–6), registration (Task 7), unit-tested with fakes + live-verify note (test tasks). ✓
- **`${var}` interpolation:** handled engine-side by `ConfigInterpolator` before leaf dispatch (unchanged); no per-action work needed — same as Screen. ✓
- **Type consistency:** `TemplateMatchCore` keys/fields/method names are referenced identically across Tasks 1–7; `AndroidImageActionBase.CaptureAndMatch` signature matches all three action call sites; `IAndroidDevice.Screenshot()` returns `byte[]` (decoded via `MemoryStream`+`Bitmap`). ✓
- **No placeholders:** every code step shows complete code; every run step gives an exact command + expected outcome. ✓
