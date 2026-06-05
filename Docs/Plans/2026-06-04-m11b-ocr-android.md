# M11b — Android OCR Actions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the four **Android** OCR actions — `android.readText`, `android.findText`, `android.waitForText`, `android.assertTextAbsent` — reading text from the device framebuffer, reusing M11a's OCR engine + `OcrCore`.

**Architecture:** Extract the shared OCR config keys/field-factories from `ScreenOcrActionBase` into `OcrCore` (so both action families share them, mirroring `TemplateMatchCore`). Add `AndroidOcrActionBase` (inject `IOcrEngine`; capture the bound `IAndroidDevice` framebuffer → `OcrCore`) paralleling `AndroidImageActionBase`. The four actions mirror the Screen OCR ones but are device-targeted; Find/Wait Text reuse `TemplateMatchCore.WriteMatchVariables` so they yield the identical match-variable contract.

**Tech Stack:** C# / .NET 10, AdbCore, the (merged) `Tesseract` engine + `OcrCore`, System.Drawing, xUnit.

**Reference spec:** `Docs/Specs/2026-06-04-m11-ocr-design.md` (§6 "M11b"). Branches off `main` (M11a merged).

**Merge handling:** needs a live "real Tesseract reads real text on a device" check → **not** self-merged; opened as a PR, user live-verifies (real ADB device/emulator) + merges.

**`<WORKTREE>` = the actual worktree path the controller provides (e.g. `C:\git\ADB\.claude\worktrees\m11b-android-ocr`).**

---

## File Structure

- Modify `AdbCore/Ocr/OcrCore.cs` — add the shared OCR keys + field factories.
- Modify `AdbCore/Actions/BuiltIn/ScreenOcrActionBase.cs` — alias/forward to `OcrCore` (Screen OCR tests stay green).
- Create `AdbCore/Actions/BuiltIn/Android/AndroidOcrActionBase.cs`.
- Create `AdbCore/Actions/BuiltIn/Android/AndroidReadTextAction.cs`, `AndroidFindTextAction.cs`, `AndroidWaitForTextAction.cs`, `AndroidAssertTextAbsentAction.cs`.
- Modify `AdbCore/Actions/BuiltIn/BuiltInActions.cs` — register the 4 + counts.
- Tests: `AdbCore.Tests/Actions/BuiltIn/Android/AndroidOcrActionBaseTests.cs`, `AndroidOcrActionTests.cs`, `AndroidOcrRegistrationTests.cs`; bump `BuiltInActionsTests.cs` + `PaletteViewModelTests.cs`.

---

## Task 1: Extract shared OCR keys + field factories to `OcrCore`

**Files:** Modify `AdbCore/Ocr/OcrCore.cs`, `AdbCore/Actions/BuiltIn/ScreenOcrActionBase.cs`. Existing Screen OCR tests are the safety net (no new test).

- [ ] **Step 1: Add the keys + factories to `OcrCore`**

In `AdbCore/Ocr/OcrCore.cs`, add (the file already has `using` for ConfigField via `AdbCore.Actions`? It's in namespace `AdbCore.Ocr` — add `using AdbCore.Actions;` if `ConfigField`/`ConfigFieldType` aren't resolvable). Add these members to the `OcrCore` static class:
```csharp
    public const string TextKey = "text";
    public const string ResultVarKey = "resultVar";
    public const string MinConfidenceKey = "minConfidence";

    public static AdbCore.Actions.ConfigField TextField() => new() { Key = TextKey, Label = "Text", Type = AdbCore.Actions.ConfigFieldType.String };
    public static AdbCore.Actions.ConfigField ResultVarField(string def) => new() { Key = ResultVarKey, Label = "Result Variable", Type = AdbCore.Actions.ConfigFieldType.String, DefaultValue = def };
    public static AdbCore.Actions.ConfigField MinConfidenceField() => new() { Key = MinConfidenceKey, Label = "Min Confidence", Type = AdbCore.Actions.ConfigFieldType.Number, DefaultValue = 0 };
```
(If `using AdbCore.Actions;` is added at the top, drop the `AdbCore.Actions.` qualifiers for readability — either is fine as long as it compiles.)

- [ ] **Step 2: Refactor `ScreenOcrActionBase` to alias/forward**

In `AdbCore/Actions/BuiltIn/ScreenOcrActionBase.cs`, change the three OCR key consts to alias `OcrCore` (keeps `TextKey` etc. resolvable for the inheriting Screen OCR actions), and forward the three factories. Replace:
```csharp
    public const string TextKey = "text";
    public const string ResultVarKey = "resultVar";
    public const string MinConfidenceKey = "minConfidence";
```
with:
```csharp
    public const string TextKey = OcrCore.TextKey;
    public const string ResultVarKey = OcrCore.ResultVarKey;
    public const string MinConfidenceKey = OcrCore.MinConfidenceKey;
```
and replace the three factory methods:
```csharp
    protected static ConfigField TextField() => new() { Key = TextKey, Label = "Text", Type = ConfigFieldType.String };
    protected static ConfigField ResultVarField(string def) => new() { Key = ResultVarKey, Label = "Result Variable", Type = ConfigFieldType.String, DefaultValue = def };
    protected static ConfigField MinConfidenceField() => new() { Key = MinConfidenceKey, Label = "Min Confidence", Type = ConfigFieldType.Number, DefaultValue = 0 };
```
with:
```csharp
    protected static ConfigField TextField() => OcrCore.TextField();
    protected static ConfigField ResultVarField(string def) => OcrCore.ResultVarField(def);
    protected static ConfigField MinConfidenceField() => OcrCore.MinConfidenceField();
```
(`SuccessPort`/`FailurePort` stay. The file already has `using AdbCore.Ocr;`.)

- [ ] **Step 3: Build + run the Screen OCR tests (safety net)**

Run: `dotnet build <WORKTREE>\ADB.slnx` → success, 0 warnings.
Run: `dotnet test <WORKTREE>\AdbCore.Tests --filter "FullyQualifiedName~ScreenOcrActionTests|FullyQualifiedName~ScreenOcrRegistrationTests|FullyQualifiedName~Ocr."`
Expected: PASS (the Screen OCR behaviour is unchanged — same keys, same fields).

- [ ] **Step 4: Commit**
```bash
git -C <WORKTREE> add AdbCore/Ocr/OcrCore.cs AdbCore/Actions/BuiltIn/ScreenOcrActionBase.cs
git -C <WORKTREE> commit -m "refactor(ocr): move shared OCR keys + field factories to OcrCore"
```

---

## Task 2: `AndroidOcrActionBase` + base test

**Files:** Create `AdbCore/Actions/BuiltIn/Android/AndroidOcrActionBase.cs`; `AdbCore.Tests/Actions/BuiltIn/Android/AndroidOcrActionBaseTests.cs`.

Context: `AndroidActionBase` provides `ResolveDevice(context) → IAndroidDevice?`, `RequiresDevice() → ActionResult`, `SuccessPort`/`FailurePort` consts, default `OutputPorts` (onSuccess/onFailure), `Category "Android"`, `SupportsRetry => false` (virtual). `IAndroidDevice.Screenshot() → byte[]` (PNG). `OcrCore.RecognizeRegion(Bitmap, config, IOcrEngine)`. Test fakes: `FakeAndroidDevice` (settable `ScreenshotBytes`), `FakeOcrEngine(OcrResult)` (namespace `AdbCore.Tests.Ocr`), and `AndroidImageActionBaseTests.PngBytes(int w, int h)` (internal static — reuse for a decodable framebuffer).

- [ ] **Step 1: Write the failing test**

Create `AdbCore.Tests/Actions/BuiltIn/Android/AndroidOcrActionBaseTests.cs`:
```csharp
using System.Collections.Generic;
using System.Drawing;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Actions.BuiltIn.Android;
using AdbCore.Android;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Ocr;
using AdbCore.Tests.Ocr;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn.Android;

public class AndroidOcrActionBaseTests
{
    private sealed class TestAndroidOcrAction(IOcrEngine ocr) : AndroidOcrActionBase(ocr)
    {
        public override string TypeKey => "android.testOcr";
        public override string DisplayName => "Test Android OCR";
        public override string Description => "";
        protected override IEnumerable<ConfigField> ActionConfigFields => [];
        public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct) => Task.FromResult(ActionResult.Ok(SuccessPort));

        public OcrResult CallRecognizeDevice(ActionExecutionContext ctx, IAndroidDevice device) => RecognizeDevice(ctx, device);
    }

    private static OcrResult Result(params OcrWord[] w) => new(string.Join(" ", System.Array.ConvertAll(w, x => x.Text)), w);
    private static ActionExecutionContext Exec(BotAction a) => new(a, new BotExecutionContext(), _ => { });

    [Fact]
    public void RecognizeDevice_DecodesFramebuffer_AndRunsOcr()
    {
        var device = new FakeAndroidDevice { ScreenshotBytes = AndroidImageActionBaseTests.PngBytes(1080, 1920) };
        var engine = new FakeOcrEngine(Result(new OcrWord("hi", new Rectangle(1, 2, 3, 4), 0.9)));
        var action = new TestAndroidOcrAction(engine);

        var res = action.CallRecognizeDevice(Exec(new BotAction()), device);

        Assert.Equal(1080, engine.LastWidth);
        Assert.Equal(1920, engine.LastHeight);
        Assert.Equal("hi", res.Words[0].Text);
        Assert.Contains("screenshot", device.Calls);
    }

    [Fact]
    public void Definition_HasRegionFields_SupportsRetry_CategoryAndroid()
    {
        var action = new TestAndroidOcrAction(new FakeOcrEngine(Result()));
        Assert.Equal("Android", action.Category);
        Assert.True(action.SupportsRetry);
        Assert.Contains(action.ConfigFields, f => f.Key == TemplateMatchCore.RegionWidthKey);
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test <WORKTREE>\AdbCore.Tests --filter "FullyQualifiedName~AndroidOcrActionBaseTests"` → compile FAIL.

- [ ] **Step 3: Create `AndroidOcrActionBase.cs`**
```csharp
using System.Drawing;
using System.IO;
using AdbCore.Android;
using AdbCore.Execution;
using AdbCore.Ocr;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Shared base for Android OCR actions: captures the bound device's framebuffer, decodes it, and
/// runs OCR via <see cref="OcrCore"/>. Exposes the shared ROI fields; no Capture Method field.</summary>
public abstract class AndroidOcrActionBase : AndroidActionBase
{
    private List<ConfigField>? _configFields;

    protected AndroidOcrActionBase(IOcrEngine ocr)
    {
        ArgumentNullException.ThrowIfNull(ocr);
        Ocr = ocr;
    }

    protected IOcrEngine Ocr { get; }

    public override bool SupportsRetry => true;

    /// <summary>The action's own config fields, shown before the shared ROI fields.</summary>
    protected abstract IEnumerable<ConfigField> ActionConfigFields { get; }

    public override List<ConfigField> ConfigFields => _configFields ??=
    [
        .. ActionConfigFields,
        .. TemplateMatchCore.RegionFields(),
    ];

    /// <summary>Captures the device framebuffer and OCRs the configured region (full-frame word coords).</summary>
    protected OcrResult RecognizeDevice(ActionExecutionContext context, IAndroidDevice device)
    {
        using var ms = new MemoryStream(device.Screenshot());
        using var frame = new Bitmap(ms);
        return OcrCore.RecognizeRegion(frame, context.Action.Config, Ocr);
    }
}
```

- [ ] **Step 4: Run to verify it passes** — same filter → PASS (2 tests).

- [ ] **Step 5: Commit**
```bash
git -C <WORKTREE> add AdbCore/Actions/BuiltIn/Android/AndroidOcrActionBase.cs AdbCore.Tests/Actions/BuiltIn/Android/AndroidOcrActionBaseTests.cs
git -C <WORKTREE> commit -m "feat(ocr): AndroidOcrActionBase (framebuffer capture + shared OcrCore)"
```

---

## Task 3: Android Read Text + Find Text

**Files:** Create `AndroidReadTextAction.cs`, `AndroidFindTextAction.cs`; create `AdbCore.Tests/Actions/BuiltIn/Android/AndroidOcrActionTests.cs`.

Context: `AndroidActionBase.ResolveDevice`/`RequiresDevice`/`SuccessPort`/`FailurePort`. `OcrCore.TextKey/ResultVarKey/MinConfidenceKey` + `TextField()/ResultVarField(def)/MinConfidenceField()` + `FindWord`. `TemplateMatchCore.DefaultResultVar` ("match") + `WriteMatchVariables`. `ConfigValues.GetString/GetDouble/GetInt`. `IRandomSource`/`MatchResult` (AdbCore.Screen).

- [ ] **Step 1: Write the failing test**

Create `AdbCore.Tests/Actions/BuiltIn/Android/AndroidOcrActionTests.cs`:
```csharp
using System.Drawing;
using AdbCore.Actions.BuiltIn;
using AdbCore.Actions.BuiltIn.Android;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Ocr;
using AdbCore.Screen;
using AdbCore.Tests.Ocr;
using AdbCore.Tests.Screen;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn.Android;

public class AndroidOcrActionTests
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

    private static OcrResult Result(params OcrWord[] w) => new(string.Join(" ", System.Array.ConvertAll(w, x => x.Text)), w);

    [Fact]
    public async Task ReadText_WritesRecognizedTextToVar()
    {
        var action = new BotAction();
        var (ctx, _) = WithDevice(action);
        var read = new AndroidReadTextAction(new FakeOcrEngine(Result(new OcrWord("Score", new Rectangle(0, 0, 9, 9), 0.9))));

        var r = await read.ExecuteAsync(ctx, default);

        Assert.True(r.Success);
        Assert.Equal("Score", ctx.Context.Variables["text"]);
    }

    [Fact]
    public async Task FindText_Match_WritesMatchVariables_AndSuccess()
    {
        var action = new BotAction { Config = { ["text"] = "attack" } };
        var (ctx, _) = WithDevice(action);
        var find = new AndroidFindTextAction(new FakeOcrEngine(Result(new OcrWord("ATTACK", new Rectangle(100, 40, 30, 20), 0.9))), new FixedRandomSource(123));

        var r = await find.ExecuteAsync(ctx, default);

        Assert.True(r.Success);
        Assert.Equal("onSuccess", r.OutputPort);
        Assert.Equal("100", ctx.Context.Variables["matchLeft"]);
        Assert.Equal("130", ctx.Context.Variables["matchRight"]);
        Assert.Equal("123", ctx.Context.Variables["matchRandX"]);
    }

    [Fact]
    public async Task FindText_NoMatch_Fails()
    {
        var action = new BotAction { Config = { ["text"] = "attack" } };
        var (ctx, _) = WithDevice(action);
        var find = new AndroidFindTextAction(new FakeOcrEngine(Result(new OcrWord("settings", new Rectangle(0, 0, 9, 9), 0.9))), new FixedRandomSource(0));

        Assert.False((await find.ExecuteAsync(ctx, default)).Success);
    }

    [Fact]
    public async Task ReadText_NoDevice_Fails()
    {
        var exec = new ActionExecutionContext(new BotAction(), new BotExecutionContext(), _ => { });
        var r = await new AndroidReadTextAction(new FakeOcrEngine(Result())).ExecuteAsync(exec, default);
        Assert.False(r.Success);
        Assert.Contains("Android", r.ErrorMessage);
    }

    [Fact]
    public void Find_Definition_Metadata()
    {
        var def = new AndroidFindTextAction(new FakeOcrEngine(Result()), new FixedRandomSource(0));
        Assert.Equal("android.findText", def.TypeKey);
        Assert.Equal("Find Text (Android)", def.DisplayName);
        Assert.Equal("Android", def.Category);
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test <WORKTREE>\AdbCore.Tests --filter "FullyQualifiedName~AndroidOcrActionTests"` → compile FAIL.

- [ ] **Step 3: Create `AndroidReadTextAction.cs`**
```csharp
using AdbCore.Execution;
using AdbCore.Ocr;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>OCRs the device screen (or region) and writes the recognized text to a variable.</summary>
public sealed class AndroidReadTextAction : AndroidOcrActionBase
{
    public AndroidReadTextAction(IOcrEngine ocr) : base(ocr) { }

    public override string TypeKey => "android.readText";
    public override string DisplayName => "Read Text (Android)";
    public override string Description => "Reads text from the device screen (or region) into a variable.";
    public override bool SupportsRetry => false;
    public override List<PortDefinition> OutputPorts { get; } = new() { new PortDefinition { Name = "out", Label = "Out" } };

    protected override IEnumerable<ConfigField> ActionConfigFields => [OcrCore.ResultVarField("text")];

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return Task.FromResult(RequiresDevice());
        }

        var resultVar = ConfigValues.GetString(context.Action.Config, OcrCore.ResultVarKey, "text");
        if (string.IsNullOrWhiteSpace(resultVar)) { resultVar = "text"; }

        var result = RecognizeDevice(context, device);
        context.Context.Variables[resultVar] = result.Text.Trim();
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
```
NOTE: `ReadText` uses the `SuccessPort` const for `Ok` but its only port is "out". Mirror the Screen `ReadTextAction`, which returns `ActionResult.Ok("out")` — use `"out"` here (its OutputPorts is the single "out" port). Change `Ok(SuccessPort)` to `Ok("out")`.

- [ ] **Step 4: Create `AndroidFindTextAction.cs`**
```csharp
using AdbCore.Execution;
using AdbCore.Ocr;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Finds a target string on the device screen via OCR and writes its location (the same match
/// variables as Find Image) under a prefix. Not found is a failed result (engine retry / onFailure).</summary>
public sealed class AndroidFindTextAction : AndroidOcrActionBase
{
    private readonly IRandomSource _random;

    public AndroidFindTextAction(IOcrEngine ocr, IRandomSource random) : base(ocr)
    {
        ArgumentNullException.ThrowIfNull(random);
        _random = random;
    }

    public override string TypeKey => "android.findText";
    public override string DisplayName => "Find Text (Android)";
    public override string Description => "Finds a text string on the device screen and writes its location to variables.";

    protected override IEnumerable<ConfigField> ActionConfigFields =>
    [
        OcrCore.TextField(),
        OcrCore.ResultVarField(TemplateMatchCore.DefaultResultVar),
        OcrCore.MinConfidenceField(),
    ];

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return Task.FromResult(RequiresDevice());
        }

        var target = ConfigValues.GetString(context.Action.Config, OcrCore.TextKey);
        if (string.IsNullOrWhiteSpace(target))
        {
            return Task.FromResult(ActionResult.Fail("Find Text (Android): a target text string is required."));
        }

        var prefix = ConfigValues.GetString(context.Action.Config, OcrCore.ResultVarKey, TemplateMatchCore.DefaultResultVar);
        if (string.IsNullOrWhiteSpace(prefix)) { prefix = TemplateMatchCore.DefaultResultVar; }
        var minConfidence = ConfigValues.GetDouble(context.Action.Config, OcrCore.MinConfidenceKey, 0);

        var result = RecognizeDevice(context, device);
        if (OcrCore.FindWord(result, target, minConfidence) is not MatchResult m)
        {
            return Task.FromResult(ActionResult.Fail($"Find Text (Android): '{target}' not found."));
        }

        TemplateMatchCore.WriteMatchVariables(context.Context.Variables, m, prefix, _random);
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
```

- [ ] **Step 5: Run to verify it passes** — `dotnet test <WORKTREE>\AdbCore.Tests --filter "FullyQualifiedName~AndroidOcrActionTests"` → PASS (5 tests).

- [ ] **Step 6: Commit**
```bash
git -C <WORKTREE> add AdbCore/Actions/BuiltIn/Android/AndroidReadTextAction.cs AdbCore/Actions/BuiltIn/Android/AndroidFindTextAction.cs AdbCore.Tests/Actions/BuiltIn/Android/AndroidOcrActionTests.cs
git -C <WORKTREE> commit -m "feat(ocr): Android Read Text + Find Text"
```

---

## Task 4: Android Wait for Text + Assert Text Absent

**Files:** Create `AndroidWaitForTextAction.cs`, `AndroidAssertTextAbsentAction.cs`; append tests to `AndroidOcrActionTests.cs`.

- [ ] **Step 1: Append failing tests** (inside `AndroidOcrActionTests`):
```csharp
    [Fact]
    public async Task WaitForText_Present_Succeeds()
    {
        var action = new BotAction { Config = { ["text"] = "ready", [AndroidWaitForTextAction.TimeoutMsKey] = 1000, [AndroidWaitForTextAction.PollIntervalMsKey] = 10 } };
        var (ctx, _) = WithDevice(action);
        var wait = new AndroidWaitForTextAction(new FakeOcrEngine(Result(new OcrWord("READY", new Rectangle(1, 2, 3, 4), 0.9))), new FixedRandomSource(0));

        Assert.True((await wait.ExecuteAsync(ctx, default)).Success);
    }

    [Fact]
    public async Task WaitForText_Timeout_Fails()
    {
        var action = new BotAction { Config = { ["text"] = "ready", [AndroidWaitForTextAction.TimeoutMsKey] = 30, [AndroidWaitForTextAction.PollIntervalMsKey] = 10 } };
        var (ctx, _) = WithDevice(action);
        var wait = new AndroidWaitForTextAction(new FakeOcrEngine(Result(new OcrWord("loading", new Rectangle(1, 2, 3, 4), 0.9))), new FixedRandomSource(0));

        var r = await wait.ExecuteAsync(ctx, default);
        Assert.False(r.Success);
        Assert.Contains("did not appear", r.ErrorMessage);
    }

    [Fact]
    public async Task AssertTextAbsent_Absent_Ok_Present_Fail()
    {
        var a1 = new BotAction { Config = { ["text"] = "gameover" } };
        var (c1, _) = WithDevice(a1);
        Assert.True((await new AndroidAssertTextAbsentAction(new FakeOcrEngine(Result(new OcrWord("menu", new Rectangle(1, 2, 3, 4), 0.9)))).ExecuteAsync(c1, default)).Success);

        var a2 = new BotAction { Config = { ["text"] = "gameover" } };
        var (c2, _) = WithDevice(a2);
        Assert.False((await new AndroidAssertTextAbsentAction(new FakeOcrEngine(Result(new OcrWord("gameover", new Rectangle(1, 2, 3, 4), 0.9)))).ExecuteAsync(c2, default)).Success);
    }
```

- [ ] **Step 2: Run to verify it fails** — same filter → compile FAIL.

- [ ] **Step 3: Create `AndroidWaitForTextAction.cs`**
```csharp
using System.Diagnostics;
using AdbCore.Execution;
using AdbCore.Ocr;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Polls the device screen until the text appears or the timeout elapses.</summary>
public sealed class AndroidWaitForTextAction : AndroidOcrActionBase
{
    public const string TimeoutMsKey = "timeoutMs";
    public const string PollIntervalMsKey = "pollIntervalMs";
    public const int DefaultTimeoutMs = 5000;
    public const int DefaultPollIntervalMs = 250;

    private readonly IRandomSource _random;

    public AndroidWaitForTextAction(IOcrEngine ocr, IRandomSource random) : base(ocr)
    {
        ArgumentNullException.ThrowIfNull(random);
        _random = random;
    }

    public override string TypeKey => "android.waitForText";
    public override string DisplayName => "Wait for Text (Android)";
    public override string Description => "Polls the device screen until the text appears or the timeout elapses.";

    protected override IEnumerable<ConfigField> ActionConfigFields =>
    [
        OcrCore.TextField(),
        OcrCore.ResultVarField(TemplateMatchCore.DefaultResultVar),
        OcrCore.MinConfidenceField(),
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

        var target = ConfigValues.GetString(context.Action.Config, OcrCore.TextKey);
        if (string.IsNullOrWhiteSpace(target))
        {
            return ActionResult.Fail("Wait for Text (Android): a target text string is required.");
        }

        var prefix = ConfigValues.GetString(context.Action.Config, OcrCore.ResultVarKey, TemplateMatchCore.DefaultResultVar);
        if (string.IsNullOrWhiteSpace(prefix)) { prefix = TemplateMatchCore.DefaultResultVar; }
        var minConfidence = ConfigValues.GetDouble(context.Action.Config, OcrCore.MinConfidenceKey, 0);
        var timeoutMs = Math.Max(0, ConfigValues.GetInt(context.Action.Config, TimeoutMsKey, DefaultTimeoutMs));
        var pollMs = Math.Max(1, ConfigValues.GetInt(context.Action.Config, PollIntervalMsKey, DefaultPollIntervalMs));

        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            var result = RecognizeDevice(context, device);
            if (OcrCore.FindWord(result, target, minConfidence) is MatchResult m)
            {
                TemplateMatchCore.WriteMatchVariables(context.Context.Variables, m, prefix, _random);
                return ActionResult.Ok(SuccessPort);
            }
            if (elapsed.ElapsedMilliseconds >= timeoutMs)
            {
                return ActionResult.Fail($"Wait for Text (Android): '{target}' did not appear within {timeoutMs} ms.");
            }
            await Task.Delay(pollMs, ct);
        }
    }
}
```

- [ ] **Step 4: Create `AndroidAssertTextAbsentAction.cs`**
```csharp
using AdbCore.Execution;
using AdbCore.Ocr;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Succeeds only when the target text is NOT present on the device screen (present → Fail).</summary>
public sealed class AndroidAssertTextAbsentAction : AndroidOcrActionBase
{
    public AndroidAssertTextAbsentAction(IOcrEngine ocr) : base(ocr) { }

    public override string TypeKey => "android.assertTextAbsent";
    public override string DisplayName => "Assert Text Absent (Android)";
    public override string Description => "Succeeds when the target text is not present on the device screen.";

    protected override IEnumerable<ConfigField> ActionConfigFields => [OcrCore.TextField(), OcrCore.MinConfidenceField()];

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return Task.FromResult(RequiresDevice());
        }

        var target = ConfigValues.GetString(context.Action.Config, OcrCore.TextKey);
        if (string.IsNullOrWhiteSpace(target))
        {
            return Task.FromResult(ActionResult.Fail("Assert Text Absent (Android): a target text string is required."));
        }

        var minConfidence = ConfigValues.GetDouble(context.Action.Config, OcrCore.MinConfidenceKey, 0);
        var result = RecognizeDevice(context, device);

        return OcrCore.FindWord(result, target, minConfidence) is MatchResult
            ? Task.FromResult(ActionResult.Fail($"Assert Text Absent (Android): '{target}' is present."))
            : Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
```

- [ ] **Step 5: Run to verify it passes** — same filter → PASS (8 tests).

- [ ] **Step 6: Commit**
```bash
git -C <WORKTREE> add AdbCore/Actions/BuiltIn/Android/AndroidWaitForTextAction.cs AdbCore/Actions/BuiltIn/Android/AndroidAssertTextAbsentAction.cs AdbCore.Tests/Actions/BuiltIn/Android/AndroidOcrActionTests.cs
git -C <WORKTREE> commit -m "feat(ocr): Android Wait for Text + Assert Text Absent"
```

---

## Task 5: Register Android OCR actions + counts

**Files:** Modify `BuiltInActions.cs`; create `AndroidOcrRegistrationTests.cs`; modify `BuiltInActionsTests.cs`, `PaletteViewModelTests.cs`.

- [ ] **Step 1: Write the failing registration test**

Create `AdbCore.Tests/Actions/BuiltIn/Android/AndroidOcrRegistrationTests.cs`:
```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn.Android;

public class AndroidOcrRegistrationTests
{
    [Theory]
    [InlineData("android.readText")]
    [InlineData("android.findText")]
    [InlineData("android.waitForText")]
    [InlineData("android.assertTextAbsent")]
    public void AndroidOcrAction_IsRegistered(string typeKey)
    {
        var defs = new ActionRegistry();
        var execs = new ActionExecutorRegistry();
        BuiltInActions.Register(defs, execs);

        Assert.True(defs.TryGet(typeKey, out _));
        Assert.True(execs.TryGet(typeKey, out var exec) && exec is not null);
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test <WORKTREE>\AdbCore.Tests --filter "FullyQualifiedName~AndroidOcrRegistrationTests"` → FAIL.

- [ ] **Step 3: Register**

In `AdbCore/Actions/BuiltIn/BuiltInActions.cs`, after the Android image-action registrations (the block ending with `Add(new AndroidAssertImageAbsentAction(templateMatcher), ...)`), and noting `ocrEngine` is already declared in the Screen OCR block above and `randomSource` in the Screen block, add:
```csharp

        // Android OCR (Tesseract; reuses the shared OCR engine + RNG).
        Add(new AndroidReadTextAction(ocrEngine), definitions, executors);
        Add(new AndroidFindTextAction(ocrEngine, randomSource), definitions, executors);
        Add(new AndroidWaitForTextAction(ocrEngine, randomSource), definitions, executors);
        Add(new AndroidAssertTextAbsentAction(ocrEngine), definitions, executors);
```
(`Add(new AndroidReadTextAction(...))` needs `AndroidReadTextAction` etc. — the file already has `using AdbCore.Actions.BuiltIn.Android;`.) IMPORTANT: confirm `ocrEngine` is in scope at this point — it's declared in the Screen OCR block which runs BEFORE the Android block in `Register`. If the Android block is ABOVE the Screen OCR block, MOVE these 4 lines to after `var ocrEngine = ...` (or reference order accordingly). Read the method and place them where `ocrEngine` + `randomSource` are both in scope.

- [ ] **Step 4: Update counts**

Read current values first. In `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs`, the counts are `38`/`35` (post-M11a). Increase both by 4 → `42`/`39`. In `BotBuilder.Core.Tests/PaletteViewModelTests.cs`: the Android category assertion is `9` → `13`; the `ClearingSearch_RestoresAll` total `38` → `42` (update the inline comment's Android number). If the base numbers differ, +4 each and report.

- [ ] **Step 5: Run to verify it passes**
Run: `dotnet test <WORKTREE>\AdbCore.Tests --filter "FullyQualifiedName~AndroidOcrRegistrationTests|FullyQualifiedName~BuiltInActionsTests"`
Then: `dotnet test <WORKTREE>\BotBuilder.Core.Tests --filter "FullyQualifiedName~PaletteViewModelTests"` → PASS.

- [ ] **Step 6: Commit**
```bash
git -C <WORKTREE> add AdbCore/Actions/BuiltIn/BuiltInActions.cs AdbCore.Tests/Actions/BuiltIn/Android/AndroidOcrRegistrationTests.cs AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs BotBuilder.Core.Tests/PaletteViewModelTests.cs
git -C <WORKTREE> commit -m "feat(ocr): register the 4 Android OCR actions + update palette/registry counts"
```

---

## Task 6: Build + test sweep + PR (user-verified)

- [ ] **Step 1:** `dotnet build <WORKTREE>\ADB.slnx` → 0 warnings.
- [ ] **Step 2:** `dotnet test <WORKTREE>\ADB.slnx` → all pass (AdbCore.Tests +2 base, +8 action, +4 registration theory). Report counts.
- [ ] **Step 3:** Push `worktree-m11b-android-ocr`; `gh pr create` (base main) with a summary + the live-verify ask (needs a real ADB device/emulator: Find Text locates real on-screen text, Read Text returns it). Report PR URL. **Do NOT merge** — user live-verifies on a device + merges. **This completes M11.**

---

## Self-Review Notes (addressed)

- **Spec coverage (§6 M11b):** AndroidOcrActionBase framebuffer capture (Task 2); 4 device-targeted actions mirroring Screen semantics (Tasks 3-4); registration + counts (Task 5); fake-engine unit tests + live-verify handoff. Shared OCR factories extracted to OcrCore (Task 1) so Screen + Android stay consistent. ✓
- **Match contract:** Android Find/Wait Text reuse `TemplateMatchCore.WriteMatchVariables` → identical `matchRandX/Y` contract as Find Image/Find Text. ✓
- **Type consistency:** `OcrCore.TextKey/ResultVarKey/MinConfidenceKey` + factories, `AndroidOcrActionBase(IOcrEngine)` ctor + `RecognizeDevice`, action ctors `(IOcrEngine[, IRandomSource])` referenced consistently. ReadText returns `Ok("out")` (single out port), others `Ok(SuccessPort)`. ✓
- **Adaptive points flagged:** Task 5 `ocrEngine`/`randomSource` scope ordering in `Register`; base-count numbers verified against the files; Read Text `Ok("out")` correction noted in Task 3.
- **No regression:** Task 1 keeps Screen OCR keys/fields identical (aliases), Screen OCR tests are the safety net.
