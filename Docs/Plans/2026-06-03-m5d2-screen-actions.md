# M5d2 — Wait for Image, Screenshot, Assert Image Absent Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the Screen action category (and milestone M5) with the three remaining actions — **Wait for Image** (poll until it appears), **Screenshot** (save the window/region as PNG), and **Assert Image Absent** (succeed only when the template is gone) — built on the M5d1 capture/match infra.

**Architecture:** Refactor `ScreenActionBase` so it requires only `IWindowCapture` (capture + ROI), exposes a capture-only `CaptureRegion` helper (for Screenshot), takes the matcher as a parameter to `CaptureAndMatch` (so capture-only actions need no matcher), and centralizes match-variable writing in `WriteMatchVariables` (reused by Find Image and Wait for Image). Then add the three leaf actions and register them.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), xUnit, OpenCvSharp + System.Drawing (already referenced). Per `Docs/Specs/2026-06-02-m5d-screen-actions-design.md`.

---

## File Structure

- `AdbCore/Actions/BuiltIn/ScreenActionBase.cs` — refactor: capture-only ctor; shared config-key consts + field factories; `CaptureRegion`; `CaptureAndMatch(matcher param)`; `WriteMatchVariables`.
- `AdbCore/Actions/BuiltIn/FindImageAction.cs` — re-fit to the refactored base (no behavior change).
- `AdbCore/Actions/BuiltIn/WaitForImageAction.cs` — **new**.
- `AdbCore/Actions/BuiltIn/AssertImageAbsentAction.cs` — **new**.
- `AdbCore/Actions/BuiltIn/ScreenshotAction.cs` — **new**.
- `AdbCore/Actions/BuiltIn/BuiltInActions.cs` — register the three.
- Tests: `ScreenActionBaseTests.cs` (update for new ctor/signature), `WaitForImageActionTests.cs`, `AssertImageAbsentActionTests.cs`, `ScreenshotActionTests.cs` (new), `ScreenRegistrationTests.cs` (extend), and count-assertion bumps in `BuiltInActionsTests.cs` / `PaletteViewModelTests.cs`.

---

## Task 1: Refactor `ScreenActionBase` (capture-only base) + re-fit Find Image

Behavior-preserving refactor. The matcher moves out of the base ctor and becomes a `CaptureAndMatch` parameter; shared match config + the 9-variable write logic move to the base for reuse.

**Files:**
- Modify: `AdbCore/Actions/BuiltIn/ScreenActionBase.cs`, `AdbCore/Actions/BuiltIn/FindImageAction.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/ScreenActionBaseTests.cs`

- [ ] **Step 1: Rewrite `ScreenActionBase.cs`** to this:

```csharp
using System.Drawing;
using System.Globalization;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Shared base for Screen actions: resolves the target window HWND, exposes the Capture Method
/// + region-of-interest config fields, and provides capture/match helpers that return matches in
/// full-window client coordinates. Requires only <see cref="IWindowCapture"/>; matching actions pass
/// their own <see cref="ITemplateMatcher"/> to <see cref="CaptureAndMatch"/>.</summary>
public abstract class ScreenActionBase : IActionDefinition, IActionExecutor
{
    public const string CaptureMethodKey = "captureMethod";
    public const string RegionXKey = "regionX";
    public const string RegionYKey = "regionY";
    public const string RegionWidthKey = "regionWidth";
    public const string RegionHeightKey = "regionHeight";
    public const string SuccessPort = "onSuccess";
    public const string FailurePort = "onFailure";

    // Shared match config (used by Find Image, Wait for Image, Assert Image Absent).
    public const string TemplatePathKey = "templatePath";
    public const string ConfidenceKey = "confidence";
    public const string ResultVarKey = "resultVar";
    public const double DefaultConfidence = 0.8;
    public const string DefaultResultVar = "match";

    private readonly IWindowCapture _capture;
    private List<ConfigField>? _configFields;

    protected ScreenActionBase(IWindowCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        _capture = capture;
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

    // Shared config-field factories for the match actions.
    protected static ConfigField TemplatePathField() => new() { Key = TemplatePathKey, Label = "Template Image", Type = ConfigFieldType.ImagePath };
    protected static ConfigField ConfidenceField() => new() { Key = ConfidenceKey, Label = "Confidence", Type = ConfigFieldType.Number, DefaultValue = DefaultConfidence };
    protected static ConfigField ResultVarField() => new() { Key = ResultVarKey, Label = "Result Variable", Type = ConfigFieldType.String, DefaultValue = DefaultResultVar };

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

    /// <summary>Captures the window's client area via the chosen method, cropping to the ROI when set.
    /// Returns the (possibly cropped) bitmap and the ROI offset (0,0 when no ROI). Caller disposes.</summary>
    protected Bitmap CaptureRegion(ActionExecutionContext context, IntPtr hwnd, out int offsetX, out int offsetY)
    {
        var shot = _capture.Capture(hwnd, CaptureMethodOf(context));
        var region = ResolveRegion(context, shot.Width, shot.Height);
        if (region is not Rectangle roi)
        {
            offsetX = 0;
            offsetY = 0;
            return shot;
        }

        using (shot)
        {
            offsetX = roi.X;
            offsetY = roi.Y;
            return shot.Clone(roi, shot.PixelFormat);
        }
    }

    /// <summary>Captures (with any ROI crop), matches the template, and returns the match in full-window
    /// client coordinates (null if none ≥ confidence). Disposes the capture.</summary>
    protected MatchResult? CaptureAndMatch(ActionExecutionContext context, IntPtr hwnd, ITemplateMatcher matcher, string templatePath, double confidence)
    {
        using var region = CaptureRegion(context, hwnd, out var offsetX, out var offsetY);
        var hit = matcher.Match(region, templatePath, confidence);
        return hit is MatchResult m ? m with { X = m.X + offsetX, Y = m.Y + offsetY } : null;
    }

    /// <summary>Writes a match's region edges, center, a random in-region point, and the score to run
    /// variables under <paramref name="prefix"/> (all client-relative integers, as InvariantCulture strings).</summary>
    protected static void WriteMatchVariables(ActionExecutionContext context, MatchResult m, string prefix, IRandomSource random)
    {
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
        vars[$"{prefix}RandX"] = Str(random.Next(left, right));
        vars[$"{prefix}RandY"] = Str(random.Next(top, bottom));
        vars[$"{prefix}Confidence"] = m.Score.ToString(CultureInfo.InvariantCulture);
    }

    private static string Str(int v) => v.ToString(CultureInfo.InvariantCulture);
}
```

- [ ] **Step 2: Re-fit `FindImageAction.cs`** to the refactored base (drop the moved consts/logic; keep its public ctor `(capture, matcher, random)`):

```csharp
using AdbCore.Execution;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Finds a template image within the target window and writes its location (region edges,
/// center, a random in-region point, and the score) to run variables under a configurable prefix.
/// "Not found" is a failed result so the engine can retry (per RetryPolicy) and route onFailure.</summary>
public sealed class FindImageAction : ScreenActionBase
{
    private readonly ITemplateMatcher _matcher;
    private readonly IRandomSource _random;

    public FindImageAction(IWindowCapture capture, ITemplateMatcher matcher, IRandomSource random)
        : base(capture)
    {
        ArgumentNullException.ThrowIfNull(matcher);
        ArgumentNullException.ThrowIfNull(random);
        _matcher = matcher;
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
        TemplatePathField(),
        ConfidenceField(),
        ResultVarField(),
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

        if (CaptureAndMatch(context, hwnd, _matcher, templatePath, confidence) is not MatchResult m)
        {
            return Task.FromResult(ActionResult.Fail("Find Image: no match at or above the configured confidence."));
        }

        WriteMatchVariables(context, m, prefix, _random);
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
```

- [ ] **Step 3: Update `ScreenActionBaseTests.cs`** for the new ctor/signature. The `TestScreenAction` now holds a matcher and passes it to `CaptureAndMatch`:

```csharp
    private sealed class TestScreenAction(IWindowCapture capture, ITemplateMatcher matcher) : ScreenActionBase(capture)
    {
        private readonly ITemplateMatcher _matcher = matcher;

        public override string TypeKey => "screen.test";
        public override string DisplayName => "Test Screen";
        public override string Description => "";
        public override List<PortDefinition> OutputPorts { get; } = new() { new PortDefinition { Name = SuccessPort, Label = "On Success" } };
        protected override IEnumerable<ConfigField> ActionConfigFields => [];
        public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct) => Task.FromResult(ActionResult.Ok(SuccessPort));

        public MatchResult? CallCaptureAndMatch(ActionExecutionContext ctx, IntPtr hwnd, string template, double confidence)
            => CaptureAndMatch(ctx, hwnd, _matcher, template, confidence);
    }
```

(The four existing test bodies — full-haystack passthrough, ROI crop+offset, clamp, capture-method default/override — are unchanged; they still construct `new TestScreenAction(capture, matcher)` and call `CallCaptureAndMatch`.)

- [ ] **Step 4: Build + run the screen/find tests — expect green (no behavior change)**

Run: `dotnet test AdbCore.Tests/AdbCore.Tests.csproj --filter "FullyQualifiedName~ScreenActionBaseTests|FullyQualifiedName~FindImageActionTests"`
Expected: PASS. (`FindImageActionTests` references `FindImageAction.TemplatePathKey`/`ResultVarKey`; these still compile because inherited public consts are accessible via the derived type name.)

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Actions/BuiltIn/ScreenActionBase.cs AdbCore/Actions/BuiltIn/FindImageAction.cs AdbCore.Tests/Actions/BuiltIn/ScreenActionBaseTests.cs
git commit -m "refactor(actions): capture-only ScreenActionBase; share CaptureRegion + match-var writing"
```

---

## Task 2: Wait for Image (`screen.waitForImage`)

Polls the capture→match pipeline until the template appears or a timeout elapses; on success writes the same match variables as Find Image.

**Files:**
- Create: `AdbCore/Actions/BuiltIn/WaitForImageAction.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/WaitForImageActionTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Screen;
using AdbCore.Tests.Screen;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class WaitForImageActionTests
{
    private sealed class FlakyMatcher(int missesBeforeHit, MatchResult hit) : ITemplateMatcher
    {
        public int Calls { get; private set; }
        public MatchResult? Match(System.Drawing.Bitmap haystack, string templatePath, double minConfidence)
            => ++Calls > missesBeforeHit ? hit : null;
    }

    private static BotExecutionContext WindowContext(Guid id, IntPtr handle)
    {
        var ctx = new BotExecutionContext();
        ctx.Targets[id] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "hwnd:1", Handle = handle };
        return ctx;
    }

    private static ActionExecutionContext Exec(BotAction a, BotExecutionContext c) => new(a, c, _ => { });

    private static BotAction WaitAction(Guid id, int timeoutMs, int pollMs)
    {
        var a = new BotAction { TargetId = id, Config =
        {
            [ScreenActionBase.TemplatePathKey] = "t.png",
            [WaitForImageAction.TimeoutMsKey] = timeoutMs,
            [WaitForImageAction.PollIntervalMsKey] = pollMs,
        } };
        return a;
    }

    [Fact]
    public async Task AppearsAfterPolls_WritesVariables_AndSucceeds()
    {
        var id = Guid.NewGuid();
        var ctx = WindowContext(id, (IntPtr)5);
        var matcher = new FlakyMatcher(2, new MatchResult(100, 40, 30, 20, 0.95));
        var action = new WaitForImageAction(new FakeWindowCapture(800, 600), matcher, new FixedRandomSource(7));

        var result = await action.ExecuteAsync(Exec(WaitAction(id, timeoutMs: 5000, pollMs: 1), ctx), default);

        Assert.True(result.Success);
        Assert.Equal("onSuccess", result.OutputPort);
        Assert.True(matcher.Calls >= 3);                 // polled past the 2 misses
        Assert.Equal("115", ctx.Variables["matchCenterX"]); // 100 + 30/2
        Assert.Equal("7", ctx.Variables["matchRandX"]);
    }

    [Fact]
    public async Task NeverAppears_TimesOut_Fails_AndWritesNothing()
    {
        var id = Guid.NewGuid();
        var ctx = WindowContext(id, (IntPtr)5);
        var action = new WaitForImageAction(new FakeWindowCapture(800, 600), new FakeTemplateMatcher(null), new FixedRandomSource(0));

        var result = await action.ExecuteAsync(Exec(WaitAction(id, timeoutMs: 0, pollMs: 1), ctx), default);

        Assert.False(result.Success);   // timeout → Fail (engine retries / routes onFailure)
        Assert.Empty(ctx.Variables);
    }

    [Fact]
    public async Task NoTarget_Fails()
    {
        var action = new WaitForImageAction(new FakeWindowCapture(10, 10), new FakeTemplateMatcher(null), new FixedRandomSource(0));
        var a = new BotAction { Config = { [ScreenActionBase.TemplatePathKey] = "t.png" } };
        var result = await action.ExecuteAsync(Exec(a, new BotExecutionContext()), default);
        Assert.False(result.Success);
        Assert.Contains("Window", result.ErrorMessage);
    }

    [Fact]
    public void Definition_Metadata()
    {
        var def = new WaitForImageAction(new FakeWindowCapture(1, 1), new FakeTemplateMatcher(null), new FixedRandomSource(0));
        Assert.Equal("screen.waitForImage", def.TypeKey);
        Assert.Equal("Wait for Image", def.DisplayName);
        Assert.Equal("Screen", def.Category);
        Assert.Equal(new[] { "onSuccess", "onFailure" }, def.OutputPorts.Select(p => p.Name));
        Assert.Contains(def.ConfigFields, f => f.Key == WaitForImageAction.TimeoutMsKey);
        Assert.Contains(def.ConfigFields, f => f.Key == ScreenActionBase.RegionWidthKey);
    }
}
```

- [ ] **Step 2: Run, verify failure** (`WaitForImageAction` missing):
Run: `dotnet test AdbCore.Tests/AdbCore.Tests.csproj --filter "FullyQualifiedName~WaitForImageActionTests"` → FAIL to compile.

- [ ] **Step 3: Create `WaitForImageAction.cs`**

```csharp
using System.Diagnostics;
using AdbCore.Execution;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Polls the target window until the template appears or the timeout elapses. On success writes
/// the same match variables as Find Image; on timeout returns a failed result (engine retry / onFailure).</summary>
public sealed class WaitForImageAction : ScreenActionBase
{
    public const string TimeoutMsKey = "timeoutMs";
    public const string PollIntervalMsKey = "pollIntervalMs";
    public const int DefaultTimeoutMs = 5000;
    public const int DefaultPollIntervalMs = 250;

    private readonly ITemplateMatcher _matcher;
    private readonly IRandomSource _random;

    public WaitForImageAction(IWindowCapture capture, ITemplateMatcher matcher, IRandomSource random)
        : base(capture)
    {
        ArgumentNullException.ThrowIfNull(matcher);
        ArgumentNullException.ThrowIfNull(random);
        _matcher = matcher;
        _random = random;
    }

    public override string TypeKey => "screen.waitForImage";
    public override string DisplayName => "Wait for Image";
    public override string Description => "Polls the target window until the template appears or the timeout elapses.";

    public override List<PortDefinition> OutputPorts { get; } = new()
    {
        new PortDefinition { Name = SuccessPort, Label = "On Success" },
        new PortDefinition { Name = FailurePort, Label = "On Failure" },
    };

    protected override IEnumerable<ConfigField> ActionConfigFields =>
    [
        TemplatePathField(),
        ConfidenceField(),
        ResultVarField(),
        new ConfigField { Key = TimeoutMsKey, Label = "Timeout (ms)", Type = ConfigFieldType.Number, DefaultValue = DefaultTimeoutMs },
        new ConfigField { Key = PollIntervalMsKey, Label = "Poll Interval (ms)", Type = ConfigFieldType.Number, DefaultValue = DefaultPollIntervalMs },
    ];

    public override async Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (ResolveWindow(context) is not IntPtr hwnd || hwnd == IntPtr.Zero)
        {
            return ActionResult.Fail($"{DisplayName} requires a resolved Window target (HWND).");
        }

        var templatePath = ConfigValues.GetString(context.Action.Config, TemplatePathKey);
        if (string.IsNullOrWhiteSpace(templatePath))
        {
            return ActionResult.Fail("Wait for Image: a template image path is required.");
        }

        var confidence = ConfigValues.GetDouble(context.Action.Config, ConfidenceKey, DefaultConfidence);
        var prefix = ConfigValues.GetString(context.Action.Config, ResultVarKey, DefaultResultVar);
        if (string.IsNullOrWhiteSpace(prefix))
        {
            prefix = DefaultResultVar;
        }

        var timeoutMs = Math.Max(0, ConfigValues.GetInt(context.Action.Config, TimeoutMsKey, DefaultTimeoutMs));
        var pollMs = Math.Max(1, ConfigValues.GetInt(context.Action.Config, PollIntervalMsKey, DefaultPollIntervalMs));

        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            if (CaptureAndMatch(context, hwnd, _matcher, templatePath, confidence) is MatchResult m)
            {
                WriteMatchVariables(context, m, prefix, _random);
                return ActionResult.Ok(SuccessPort);
            }

            if (elapsed.ElapsedMilliseconds >= timeoutMs)
            {
                return ActionResult.Fail($"Wait for Image: template did not appear within {timeoutMs} ms.");
            }

            await Task.Delay(pollMs, ct);
        }
    }
}
```

- [ ] **Step 4: Run tests — expect green**
Run: `dotnet test AdbCore.Tests/AdbCore.Tests.csproj --filter "FullyQualifiedName~WaitForImageActionTests"` → PASS.

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Actions/BuiltIn/WaitForImageAction.cs AdbCore.Tests/Actions/BuiltIn/WaitForImageActionTests.cs
git commit -m "feat(actions): add Wait for Image (screen.waitForImage) polling action"
```

---

## Task 3: Assert Image Absent (`screen.assertImageAbsent`)

Succeeds when the template is NOT present; fails (retryable → wait-until-gone) while it is present.

**Files:**
- Create: `AdbCore/Actions/BuiltIn/AssertImageAbsentAction.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/AssertImageAbsentActionTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Screen;
using AdbCore.Tests.Screen;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class AssertImageAbsentActionTests
{
    private static BotExecutionContext WindowContext(Guid id, IntPtr handle)
    {
        var ctx = new BotExecutionContext();
        ctx.Targets[id] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "hwnd:1", Handle = handle };
        return ctx;
    }

    private static ActionExecutionContext Exec(BotAction a, BotExecutionContext c) => new(a, c, _ => { });

    private static AssertImageAbsentAction Action(MatchResult? result)
        => new(new FakeWindowCapture(800, 600), new FakeTemplateMatcher(result));

    private static BotAction Cfg(Guid id) => new() { TargetId = id, Config = { [ScreenActionBase.TemplatePathKey] = "t.png" } };

    [Fact]
    public async Task ImageAbsent_Succeeds()
    {
        var id = Guid.NewGuid();
        var result = await Action(null).ExecuteAsync(Exec(Cfg(id), WindowContext(id, (IntPtr)5)), default);
        Assert.True(result.Success);
        Assert.Equal("onSuccess", result.OutputPort);
    }

    [Fact]
    public async Task ImagePresent_Fails()
    {
        var id = Guid.NewGuid();
        var result = await Action(new MatchResult(1, 2, 3, 4, 0.99)).ExecuteAsync(Exec(Cfg(id), WindowContext(id, (IntPtr)5)), default);
        Assert.False(result.Success);   // present → Fail (retry/onFailure)
    }

    [Fact]
    public async Task NoTarget_Fails()
    {
        var result = await Action(null).ExecuteAsync(Exec(new BotAction { Config = { [ScreenActionBase.TemplatePathKey] = "t.png" } }, new BotExecutionContext()), default);
        Assert.False(result.Success);
        Assert.Contains("Window", result.ErrorMessage);
    }

    [Fact]
    public async Task BlankTemplate_Fails()
    {
        var id = Guid.NewGuid();
        var result = await Action(null).ExecuteAsync(Exec(new BotAction { TargetId = id }, WindowContext(id, (IntPtr)5)), default);
        Assert.False(result.Success);
        Assert.Contains("template", result.ErrorMessage);
    }

    [Fact]
    public void Definition_Metadata()
    {
        var def = Action(null);
        Assert.Equal("screen.assertImageAbsent", def.TypeKey);
        Assert.Equal("Assert Image Absent", def.DisplayName);
        Assert.Equal("Screen", def.Category);
        Assert.Equal(new[] { "onSuccess", "onFailure" }, def.OutputPorts.Select(p => p.Name));
        Assert.True(def.SupportsRetry);
        Assert.DoesNotContain(def.ConfigFields, f => f.Key == ScreenActionBase.ResultVarKey); // no result var
    }
}
```

- [ ] **Step 2: Run, verify failure** → FAIL to compile.

- [ ] **Step 3: Create `AssertImageAbsentAction.cs`**

```csharp
using AdbCore.Execution;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Succeeds only when the template is NOT found in the target window. While the template is
/// present it returns a failed result, so with a RetryPolicy it becomes "wait until the image is gone",
/// and otherwise routes onFailure.</summary>
public sealed class AssertImageAbsentAction : ScreenActionBase
{
    private readonly ITemplateMatcher _matcher;

    public AssertImageAbsentAction(IWindowCapture capture, ITemplateMatcher matcher)
        : base(capture)
    {
        ArgumentNullException.ThrowIfNull(matcher);
        _matcher = matcher;
    }

    public override string TypeKey => "screen.assertImageAbsent";
    public override string DisplayName => "Assert Image Absent";
    public override string Description => "Succeeds when the template is not present in the target window.";

    public override List<PortDefinition> OutputPorts { get; } = new()
    {
        new PortDefinition { Name = SuccessPort, Label = "On Success" },
        new PortDefinition { Name = FailurePort, Label = "On Failure" },
    };

    protected override IEnumerable<ConfigField> ActionConfigFields =>
    [
        TemplatePathField(),
        ConfidenceField(),
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
            return Task.FromResult(ActionResult.Fail("Assert Image Absent: a template image path is required."));
        }

        var confidence = ConfigValues.GetDouble(context.Action.Config, ConfidenceKey, DefaultConfidence);

        return CaptureAndMatch(context, hwnd, _matcher, templatePath, confidence) is MatchResult m
            ? Task.FromResult(ActionResult.Fail($"Assert Image Absent: template is present (score {m.Score.ToString(System.Globalization.CultureInfo.InvariantCulture)})."))
            : Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
```

- [ ] **Step 4: Run tests — expect green** → PASS.

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Actions/BuiltIn/AssertImageAbsentAction.cs AdbCore.Tests/Actions/BuiltIn/AssertImageAbsentActionTests.cs
git commit -m "feat(actions): add Assert Image Absent (screen.assertImageAbsent)"
```

---

## Task 4: Screenshot (`screen.screenshot`)

Captures the target window (or ROI) and saves a PNG. Capture-only — no matcher.

**Files:**
- Create: `AdbCore/Actions/BuiltIn/ScreenshotAction.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/ScreenshotActionTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Drawing;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Tests.Screen;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class ScreenshotActionTests
{
    private static BotExecutionContext WindowContext(Guid id, IntPtr handle)
    {
        var ctx = new BotExecutionContext();
        ctx.Targets[id] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "hwnd:1", Handle = handle };
        return ctx;
    }

    private static ActionExecutionContext Exec(BotAction a, BotExecutionContext c) => new(a, c, _ => { });

    [Fact]
    public async Task SavesPng_OfClientArea()
    {
        var id = Guid.NewGuid();
        var path = Path.Combine(Path.GetTempPath(), $"adb-shot-{Guid.NewGuid():N}.png");
        try
        {
            var action = new ScreenshotAction(new FakeWindowCapture(120, 90));
            var a = new BotAction { TargetId = id, Config = { [ScreenshotAction.OutputPathKey] = path } };

            var result = await action.ExecuteAsync(Exec(a, WindowContext(id, (IntPtr)5)), default);

            Assert.True(result.Success);
            Assert.Equal("out", result.OutputPort);
            Assert.True(File.Exists(path));
            using var saved = Image.FromFile(path);
            Assert.Equal(120, saved.Width);
            Assert.Equal(90, saved.Height);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task SavesPng_OfRegion_WhenRoiSet()
    {
        var id = Guid.NewGuid();
        var path = Path.Combine(Path.GetTempPath(), $"adb-shot-{Guid.NewGuid():N}.png");
        try
        {
            var action = new ScreenshotAction(new FakeWindowCapture(200, 150));
            var a = new BotAction { TargetId = id, Config =
            {
                [ScreenshotAction.OutputPathKey] = path,
                [ScreenActionBase.RegionXKey] = 10, [ScreenActionBase.RegionYKey] = 20,
                [ScreenActionBase.RegionWidthKey] = 50, [ScreenActionBase.RegionHeightKey] = 40,
            } };

            await action.ExecuteAsync(Exec(a, WindowContext(id, (IntPtr)5)), default);

            using var saved = Image.FromFile(path);
            Assert.Equal(50, saved.Width);
            Assert.Equal(40, saved.Height);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task NoTarget_Fails()
    {
        var action = new ScreenshotAction(new FakeWindowCapture(10, 10));
        var a = new BotAction { Config = { [ScreenshotAction.OutputPathKey] = "x.png" } };
        var result = await action.ExecuteAsync(Exec(a, new BotExecutionContext()), default);
        Assert.False(result.Success);
        Assert.Contains("Window", result.ErrorMessage);
    }

    [Fact]
    public async Task BlankPath_Fails()
    {
        var id = Guid.NewGuid();
        var action = new ScreenshotAction(new FakeWindowCapture(10, 10));
        var result = await action.ExecuteAsync(Exec(new BotAction { TargetId = id }, WindowContext(id, (IntPtr)5)), default);
        Assert.False(result.Success);
        Assert.Contains("output path", result.ErrorMessage);
    }

    [Fact]
    public void Definition_Metadata()
    {
        var def = new ScreenshotAction(new FakeWindowCapture(1, 1));
        Assert.Equal("screen.screenshot", def.TypeKey);
        Assert.Equal("Screenshot", def.DisplayName);
        Assert.Equal("Screen", def.Category);
        Assert.Equal(new[] { "out" }, def.OutputPorts.Select(p => p.Name));
        Assert.False(def.SupportsRetry);
        Assert.Contains(def.ConfigFields, f => f.Key == ScreenshotAction.OutputPathKey);
        Assert.Contains(def.ConfigFields, f => f.Key == ScreenActionBase.RegionWidthKey);
    }
}
```

- [ ] **Step 2: Run, verify failure** → FAIL to compile.

- [ ] **Step 3: Create `ScreenshotAction.cs`**

```csharp
using System.Drawing.Imaging;
using AdbCore.Execution;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Captures the target window's client area (or the configured region) and saves it as a PNG.</summary>
public sealed class ScreenshotAction : ScreenActionBase
{
    public const string OutputPathKey = "outputPath";

    public ScreenshotAction(IWindowCapture capture) : base(capture)
    {
    }

    public override string TypeKey => "screen.screenshot";
    public override string DisplayName => "Screenshot";
    public override string Description => "Captures the target window (optionally a region) and saves it as a PNG.";

    public override List<PortDefinition> OutputPorts { get; } = new() { new PortDefinition { Name = "out", Label = "Out" } };
    public override bool SupportsRetry => false;

    protected override IEnumerable<ConfigField> ActionConfigFields =>
    [
        new ConfigField { Key = OutputPathKey, Label = "Output Path", Type = ConfigFieldType.FilePath },
    ];

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (ResolveWindow(context) is not IntPtr hwnd || hwnd == IntPtr.Zero)
        {
            return Task.FromResult(ActionResult.Fail($"{DisplayName} requires a resolved Window target (HWND)."));
        }

        var outputPath = ConfigValues.GetString(context.Action.Config, OutputPathKey);
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return Task.FromResult(ActionResult.Fail("Screenshot: an output path is required."));
        }

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var bitmap = CaptureRegion(context, hwnd, out _, out _);
        bitmap.Save(outputPath, ImageFormat.Png);

        return Task.FromResult(ActionResult.Ok("out"));
    }
}
```

- [ ] **Step 4: Run tests — expect green** → PASS.

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Actions/BuiltIn/ScreenshotAction.cs AdbCore.Tests/Actions/BuiltIn/ScreenshotActionTests.cs
git commit -m "feat(actions): add Screenshot (screen.screenshot) PNG capture action"
```

---

## Task 5: Register the three actions + integration coverage

**Files:**
- Modify: `AdbCore/Actions/BuiltIn/BuiltInActions.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/ScreenRegistrationTests.cs` (extend), and update count assertions in `BuiltInActionsTests.cs` + `PaletteViewModelTests.cs`.

- [ ] **Step 1: Extend the registration test**

In `ScreenRegistrationTests.cs`, add a fact asserting all four screen actions are registered as definitions + executors:

```csharp
    [Theory]
    [InlineData("screen.findImage")]
    [InlineData("screen.waitForImage")]
    [InlineData("screen.assertImageAbsent")]
    [InlineData("screen.screenshot")]
    public void ScreenAction_IsRegistered_AsDefinitionAndExecutor(string typeKey)
    {
        var defs = new ActionRegistry();
        var execs = new ActionExecutorRegistry();
        BuiltInActions.Register(defs, execs);

        Assert.True(defs.TryGet(typeKey, out _));
        Assert.True(execs.TryGet(typeKey, out var exec) && exec is not null);
    }
```

- [ ] **Step 2: Run, verify the new cases FAIL** (only findImage registered):
Run: `dotnet test AdbCore.Tests/AdbCore.Tests.csproj --filter "FullyQualifiedName~ScreenRegistrationTests"` → 3 of 4 fail.

- [ ] **Step 3: Register the three** in `BuiltInActions.cs`. Replace the single Find Image registration line with:

```csharp
        // Screen actions share one capture + matcher + RNG (OpenCvSharp/Win32 adapters; foreground-bound).
        var windowCapture = new Win32WindowCapture();
        var templateMatcher = new OpenCvSharpTemplateMatcher();
        var randomSource = new SystemRandomSource();
        Add(new FindImageAction(windowCapture, templateMatcher, randomSource), definitions, executors);
        Add(new WaitForImageAction(windowCapture, templateMatcher, randomSource), definitions, executors);
        Add(new AssertImageAbsentAction(windowCapture, templateMatcher), definitions, executors);
        Add(new ScreenshotAction(windowCapture), definitions, executors);
```

- [ ] **Step 4: Fix stale count assertions.** Find the tests asserting registry/palette counts (search `AdbCore.Tests` and `BotBuilder.Core.Tests` for the prior totals — Find Image made them 17 defs / 14 execs and 17 palette items). Adding 3 actions makes them **20 defs / 17 execs** and **20 palette items**. Update those literals (and any "+ N Screen" comments) accordingly. Run the two affected test classes to confirm the new numbers.

- [ ] **Step 5: Run the full suite**
Run: `dotnet test ADB.slnx` → all green. Report the total.

- [ ] **Step 6: Commit**

```bash
git add AdbCore/Actions/BuiltIn/BuiltInActions.cs AdbCore.Tests/Actions/BuiltIn/ScreenRegistrationTests.cs AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs BotBuilder.Core.Tests/PaletteViewModelTests.cs
git commit -m "feat(actions): register Wait for Image, Assert Image Absent, Screenshot"
```

---

## Final verification

- [ ] `dotnet test ADB.slnx` — all green.
- [ ] `dotnet build BotBuilder/BotBuilder.csproj` — the new actions surface in the palette/properties with no shell change (FilePath/Number/ImagePath/String fields reuse existing templates).

### Manual Verification Checklist (user)
1. **Wait for Image:** target a window, point it at a template that appears a moment later (or have the Delay reveal it) → it polls and routes `onSuccess` once it shows; with a template that never appears it times out → `onFailure`. On success the `match*` variables are populated (chain into a Click as before).
2. **Assert Image Absent:** with the template visible → `onFailure` (or, with a RetryPolicy, it waits until you remove it then succeeds); with it absent → `onSuccess`.
3. **Screenshot:** writes a PNG of the window client area to the output path; with ROI fields set, the saved PNG is just that region (correct size).
