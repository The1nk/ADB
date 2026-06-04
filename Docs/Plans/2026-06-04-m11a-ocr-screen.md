# M11a — OCR Engine + Screen Actions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Tesseract-backed OCR plus the four **Screen** OCR actions — `screen.readText`, `screen.findText`, `screen.waitForText`, `screen.assertTextAbsent` — reading text from Win32 window captures.

**Architecture:** An `IOcrEngine` abstraction with a live `TesseractOcrEngine`; a thin `OcrCore` that reuses the existing `TemplateMatchCore` for ROI cropping + match-variable writing (so Find Text yields the identical `matchRandX/Y` contract as Find Image); and a `ScreenOcrActionBase` (inject `IWindowCapture` + `IOcrEngine`) paralleling `ScreenActionBase`. Actions are unit-tested against a fake engine; the concrete Tesseract engine + bundled `eng.traineddata` are live-verified.

**Tech Stack:** C# / .NET 10, AdbCore, `Tesseract` NuGet (charlesw), System.Drawing, OpenCvSharp's existing `TemplateMatchCore`/`MatchResult`, xUnit.

**Reference spec:** `Docs/Specs/2026-06-04-m11-ocr-design.md`. **M11b (Android OCR actions) is a separate later plan.**

**Merge handling:** needs a live "real Tesseract reads real text" check → **not** self-merged; opened as a PR and user-verified.

---

## File Structure

**Create:**
- `assets/tessdata/eng.traineddata` — bundled trained data (standard ~23 MB).
- `AdbCore/Ocr/IOcrEngine.cs` — `IOcrEngine`, `OcrWord`, `OcrResult`.
- `AdbCore/Ocr/OcrCore.cs` — `RecognizeRegion`, `FindWord`.
- `AdbCore/Ocr/TesseractOcrEngine.cs` — live engine (build-only + user-verified).
- `AdbCore/Actions/BuiltIn/ScreenOcrActionBase.cs`
- `AdbCore/Actions/BuiltIn/ReadTextAction.cs`, `FindTextAction.cs`, `WaitForTextAction.cs`, `AssertTextAbsentAction.cs`
- Tests: `AdbCore.Tests/Ocr/FakeOcrEngine.cs`, `OcrCoreTests.cs`, `AdbCore.Tests/Actions/BuiltIn/ScreenOcrActionTests.cs`, `ScreenOcrRegistrationTests.cs`

**Modify:**
- `AdbCore/AdbCore.csproj` — `Tesseract` PackageReference + copy `assets/tessdata/**` to output `tessdata/`.
- `AdbCore/Actions/BuiltIn/BuiltInActions.cs` — register the 4 Screen OCR actions.
- `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs` — counts.
- `BotBuilder.Core.Tests/PaletteViewModelTests.cs` — Screen category count + total.

---

## Task 1: Tesseract package + bundled `eng.traineddata`

**Files:** `AdbCore/AdbCore.csproj`, `assets/tessdata/eng.traineddata`. Build-only.

- [ ] **Step 1: Add the Tesseract NuGet to `AdbCore/AdbCore.csproj`**

In an `<ItemGroup>` with the other PackageReferences, add (use the current stable charlesw version; `5.2.0` is known-good):
```xml
    <PackageReference Include="Tesseract" Version="5.2.0" />
```

- [ ] **Step 2: Fetch the trained data into the repo**

Download the standard English data to `assets/tessdata/eng.traineddata` (the worktree root is the repo root). Run from the worktree directory:
```powershell
New-Item -ItemType Directory -Force "<WORKTREE>\assets\tessdata" | Out-Null
Invoke-WebRequest -Uri "https://github.com/tesseract-ocr/tessdata/raw/main/eng.traineddata" -OutFile "<WORKTREE>\assets\tessdata\eng.traineddata"
```
Verify the file is ~20–25 MB: `(Get-Item "<WORKTREE>\assets\tessdata\eng.traineddata").Length`. If the download fails (no network), STOP and report — this file is required.

- [ ] **Step 3: Copy tessdata to output**

In `AdbCore/AdbCore.csproj`, add an `<ItemGroup>` so the data ships next to any consuming exe:
```xml
  <ItemGroup>
    <None Include="..\assets\tessdata\eng.traineddata" Link="tessdata\eng.traineddata">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
```

- [ ] **Step 4: Build to confirm restore + copy**

Run: `dotnet build <WORKTREE>\ADB.slnx`
Expected: success, 0 warnings. Confirm `BotRunner\bin\Debug\net10.0-windows\tessdata\eng.traineddata` exists after build.

- [ ] **Step 5: Commit**
```bash
git -C <WORKTREE> add AdbCore/AdbCore.csproj assets/tessdata/eng.traineddata
git -C <WORKTREE> commit -m "build(ocr): add Tesseract NuGet + bundle eng.traineddata to output tessdata/"
```

---

## Task 2: OCR engine contracts (`IOcrEngine`, `OcrResult`, `OcrWord`)

**Files:** Create `AdbCore/Ocr/IOcrEngine.cs`; create `AdbCore.Tests/Ocr/FakeOcrEngine.cs`.

- [ ] **Step 1: Write the failing test**

Create `AdbCore.Tests/Ocr/FakeOcrEngine.cs` (a reusable test fake — its mere compilation against the contracts is the check this task needs):
```csharp
using System.Collections.Generic;
using System.Drawing;
using AdbCore.Ocr;

namespace AdbCore.Tests.Ocr;

/// <summary>A canned OCR engine for tests: returns a fixed OcrResult and records the haystack size.</summary>
internal sealed class FakeOcrEngine : IOcrEngine
{
    private readonly OcrResult _result;
    public int LastWidth { get; private set; }
    public int LastHeight { get; private set; }

    public FakeOcrEngine(OcrResult result) => _result = result;

    public OcrResult Recognize(Bitmap image)
    {
        LastWidth = image.Width;
        LastHeight = image.Height;
        return _result;
    }
}
```
Add a trivial assertion test in the same file's namespace, `AdbCore.Tests/Ocr/OcrContractTests.cs`:
```csharp
using System.Collections.Generic;
using System.Drawing;
using AdbCore.Ocr;
using Xunit;

namespace AdbCore.Tests.Ocr;

public class OcrContractTests
{
    [Fact]
    public void OcrResult_CarriesTextAndWords()
    {
        var word = new OcrWord("Attack", new Rectangle(10, 20, 50, 18), 0.91);
        var result = new OcrResult("Attack Now", new List<OcrWord> { word });

        Assert.Equal("Attack Now", result.Text);
        Assert.Equal("Attack", result.Words[0].Text);
        Assert.Equal(new Rectangle(10, 20, 50, 18), result.Words[0].Bounds);
        Assert.Equal(0.91, result.Words[0].Confidence);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test <WORKTREE>\AdbCore.Tests --filter "FullyQualifiedName~Ocr.OcrContractTests"`
Expected: compile FAIL (types missing).

- [ ] **Step 3: Create `AdbCore/Ocr/IOcrEngine.cs`**
```csharp
using System.Drawing;

namespace AdbCore.Ocr;

/// <summary>One recognized word: its text, its bounding box (in the recognized image's pixels), and a
/// 0–1 confidence.</summary>
public readonly record struct OcrWord(string Text, Rectangle Bounds, double Confidence);

/// <summary>The result of recognizing an image: the full text and the per-word boxes.</summary>
public sealed record OcrResult(string Text, IReadOnlyList<OcrWord> Words);

/// <summary>Recognizes text in a bitmap. Implementations are stateless w.r.t. the call.</summary>
public interface IOcrEngine
{
    OcrResult Recognize(Bitmap image);
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test <WORKTREE>\AdbCore.Tests --filter "FullyQualifiedName~Ocr.OcrContractTests"`
Expected: PASS.

- [ ] **Step 5: Commit**
```bash
git -C <WORKTREE> add AdbCore/Ocr/IOcrEngine.cs AdbCore.Tests/Ocr/FakeOcrEngine.cs AdbCore.Tests/Ocr/OcrContractTests.cs
git -C <WORKTREE> commit -m "feat(ocr): IOcrEngine + OcrResult/OcrWord contracts + test fake"
```

---

## Task 3: `OcrCore` (region recognize + find word)

**Files:** Create `AdbCore/Ocr/OcrCore.cs`; create `AdbCore.Tests/Ocr/OcrCoreTests.cs`.

- [ ] **Step 1: Write the failing test**

Create `AdbCore.Tests/Ocr/OcrCoreTests.cs`:
```csharp
using System.Collections.Generic;
using System.Drawing;
using AdbCore.Ocr;
using AdbCore.Screen;
using Xunit;

namespace AdbCore.Tests.Ocr;

public class OcrCoreTests
{
    private static OcrResult Result(params OcrWord[] words) => new(string.Join(" ", System.Array.ConvertAll(words, w => w.Text)), words);

    [Fact]
    public void RecognizeRegion_NoRegion_PassesFullImage()
    {
        using var img = new Bitmap(800, 600);
        var engine = new FakeOcrEngine(Result(new OcrWord("hi", new Rectangle(1, 2, 3, 4), 0.9)));

        var res = OcrCore.RecognizeRegion(img, new Dictionary<string, object>(), engine);

        Assert.Equal(800, engine.LastWidth);
        Assert.Equal(600, engine.LastHeight);
        Assert.Equal("hi", res.Words[0].Text);
    }

    [Fact]
    public void RecognizeRegion_WithRegion_CropsAndOffsetsWordBoxesBack()
    {
        using var img = new Bitmap(800, 600);
        var engine = new FakeOcrEngine(Result(new OcrWord("hi", new Rectangle(5, 7, 10, 8), 0.9))); // crop-local
        var config = new Dictionary<string, object>
        {
            [TemplateMatchCore.RegionXKey] = 100, [TemplateMatchCore.RegionYKey] = 40,
            [TemplateMatchCore.RegionWidthKey] = 300, [TemplateMatchCore.RegionHeightKey] = 200,
        };

        var res = OcrCore.RecognizeRegion(img, config, engine);

        Assert.Equal(300, engine.LastWidth);
        Assert.Equal(200, engine.LastHeight);
        Assert.Equal(new Rectangle(105, 47, 10, 8), res.Words[0].Bounds); // offset by ROI origin
    }

    [Fact]
    public void FindWord_CaseInsensitiveSubstring_ReturnsFirstMatchBox()
    {
        var res = Result(
            new OcrWord("Settings", new Rectangle(0, 0, 80, 18), 0.95),
            new OcrWord("ATTACK", new Rectangle(120, 40, 70, 20), 0.88));

        var m = OcrCore.FindWord(res, "attack", 0.0);

        Assert.NotNull(m);
        Assert.Equal(new MatchResult(120, 40, 70, 20, 0.88), m);
    }

    [Fact]
    public void FindWord_BelowMinConfidence_NotMatched()
    {
        var res = Result(new OcrWord("attack", new Rectangle(1, 2, 3, 4), 0.40));
        Assert.Null(OcrCore.FindWord(res, "attack", 0.80));
    }

    [Fact]
    public void FindWord_NoMatch_ReturnsNull()
    {
        var res = Result(new OcrWord("hello", new Rectangle(1, 2, 3, 4), 0.9));
        Assert.Null(OcrCore.FindWord(res, "attack", 0.0));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test <WORKTREE>\AdbCore.Tests --filter "FullyQualifiedName~Ocr.OcrCoreTests"`
Expected: compile FAIL.

- [ ] **Step 3: Create `AdbCore/Ocr/OcrCore.cs`**
```csharp
using System.Drawing;
using AdbCore.Actions.BuiltIn;
using AdbCore.Screen;

namespace AdbCore.Ocr;

/// <summary>Capture-source-independent OCR helpers, shared by the Screen and Android OCR actions. Reuses
/// <see cref="TemplateMatchCore"/> for ROI resolution (and, in the actions, match-variable writing).</summary>
public static class OcrCore
{
    /// <summary>Recognizes the configured ROI of <paramref name="image"/> (or the whole image when no ROI),
    /// returning word boxes in full-image coordinates (offset back by the ROI origin).</summary>
    public static OcrResult RecognizeRegion(Bitmap image, IReadOnlyDictionary<string, object> config, IOcrEngine engine)
    {
        var region = TemplateMatchCore.ResolveRegion(config, image.Width, image.Height);
        if (region is not Rectangle roi)
        {
            return engine.Recognize(image);
        }

        using var crop = image.Clone(roi, image.PixelFormat);
        var result = engine.Recognize(crop);
        var offset = new List<OcrWord>(result.Words.Count);
        foreach (var w in result.Words)
        {
            offset.Add(w with { Bounds = w.Bounds with { X = w.Bounds.X + roi.X, Y = w.Bounds.Y + roi.Y } });
        }
        return result with { Words = offset };
    }

    /// <summary>Finds the first word whose text contains <paramref name="target"/> (case-insensitive, trimmed)
    /// with confidence ≥ <paramref name="minConfidence"/>; null when none. Returns the word box as a
    /// <see cref="MatchResult"/> (score = the word's confidence).</summary>
    public static MatchResult? FindWord(OcrResult result, string target, double minConfidence)
    {
        var needle = target.Trim().ToLowerInvariant();
        if (needle.Length == 0)
        {
            return null;
        }

        foreach (var w in result.Words)
        {
            if (w.Confidence >= minConfidence && w.Text.ToLowerInvariant().Contains(needle))
            {
                return new MatchResult(w.Bounds.X, w.Bounds.Y, w.Bounds.Width, w.Bounds.Height, w.Confidence);
            }
        }

        return null;
    }
}
```
Note: `Rectangle` is a struct with `with`-able members? `System.Drawing.Rectangle` is a mutable struct but does NOT support `with` (it's not a record). Replace the `w.Bounds with { ... }` usage with an explicit `new Rectangle(...)`:
```csharp
            var b = w.Bounds;
            offset.Add(w with { Bounds = new Rectangle(b.X + roi.X, b.Y + roi.Y, b.Width, b.Height) });
```
(`OcrWord` IS a record struct, so `w with { Bounds = ... }` is valid.)

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test <WORKTREE>\AdbCore.Tests --filter "FullyQualifiedName~Ocr.OcrCoreTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**
```bash
git -C <WORKTREE> add AdbCore/Ocr/OcrCore.cs AdbCore.Tests/Ocr/OcrCoreTests.cs
git -C <WORKTREE> commit -m "feat(ocr): OcrCore region-recognize (ROI offset) + case-insensitive find word"
```

---

## Task 4: `TesseractOcrEngine` (live; build-only, ADAPTIVE)

**Files:** Create `AdbCore/Ocr/TesseractOcrEngine.cs`. No unit test (live-verified by the user). Build-only.

This wraps the charlesw `Tesseract` API. Exact type/method names vary slightly by version — **adapt to the installed 5.x API**; the shape below is the intent. Verify against the package after restore (the public types are `TesseractEngine`, `Pix`, `Page`, `PageIteratorLevel`, `EngineMode`, and `PixConverter`).

- [ ] **Step 1: Create `AdbCore/Ocr/TesseractOcrEngine.cs`**
```csharp
using System.Drawing;
using Tesseract;

namespace AdbCore.Ocr;

/// <summary>Tesseract-backed OCR. Loads `eng` trained data from a tessdata directory (default: a
/// `tessdata` folder next to the running executable). Live adapter — verified by hand, not unit-tested.</summary>
public sealed class TesseractOcrEngine : IOcrEngine, IDisposable
{
    private readonly TesseractEngine _engine;

    public TesseractOcrEngine(string? tessdataPath = null, string language = "eng")
    {
        var path = tessdataPath ?? System.IO.Path.Combine(System.AppContext.BaseDirectory, "tessdata");
        _engine = new TesseractEngine(path, language, EngineMode.Default);
    }

    public OcrResult Recognize(Bitmap image)
    {
        using var pix = PixConverter.ToPix(image);
        using var page = _engine.Process(pix);

        var text = page.GetText() ?? string.Empty;
        var words = new List<OcrWord>();
        using (var iter = page.GetIterator())
        {
            iter.Begin();
            do
            {
                if (iter.TryGetBoundingBox(PageIteratorLevel.Word, out var rect))
                {
                    var wordText = iter.GetText(PageIteratorLevel.Word) ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(wordText))
                    {
                        var confidence = iter.GetConfidence(PageIteratorLevel.Word) / 100.0; // 0–100 -> 0–1
                        words.Add(new OcrWord(wordText, new Rectangle(rect.X1, rect.Y1, rect.Width, rect.Height), confidence));
                    }
                }
            }
            while (iter.Next(PageIteratorLevel.Word));
        }

        return new OcrResult(text, words);
    }

    public void Dispose() => _engine.Dispose();
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build <WORKTREE>\AdbCore`
Expected: success. **If a member differs** (e.g. `PixConverter` lives in a `Tesseract.Drawing`/`Tesseract.ImageSharp` companion, or `Rect` uses `.X1/.Y1/.Width/.Height` differently), adjust to the real 5.x API — the goal is: convert `Bitmap`→`Pix`, process, read full text via `page.GetText()`, and iterate words via `page.GetIterator()` reading text, confidence (0–100), and bounding box per `PageIteratorLevel.Word`. Report any adjustment. If `PixConverter` is not available for `System.Drawing.Bitmap` in 5.x, save the bitmap to an in-memory PNG and use `Pix.LoadFromMemory(byte[])` instead.

- [ ] **Step 3: Commit**
```bash
git -C <WORKTREE> add AdbCore/Ocr/TesseractOcrEngine.cs
git -C <WORKTREE> commit -m "feat(ocr): TesseractOcrEngine (live charlesw wrapper; word boxes + confidence)"
```

---

## Task 5: `ScreenOcrActionBase` + Read Text + Find Text

**Files:** Create `AdbCore/Actions/BuiltIn/ScreenOcrActionBase.cs`, `ReadTextAction.cs`, `FindTextAction.cs`; create `AdbCore.Tests/Actions/BuiltIn/ScreenOcrActionTests.cs`.

Context: `ScreenActionBase` already has `ResolveWindow(context) → IntPtr?`, consts `SuccessPort`/`FailurePort`, and reuses `TemplateMatchCore`. `IWindowCapture.Capture(IntPtr, ScreenCaptureMethod) → Bitmap`. `IRandomSource`. `ConfigValues.GetString/GetDouble/GetInt`. `ActionResult.Ok(port)`/`Fail(msg)`. Test fakes `FakeWindowCapture(w,h)` (namespace `AdbCore.Tests.Screen`) + `FixedRandomSource(int)` exist.

- [ ] **Step 1: Write the failing test**

Create `AdbCore.Tests/Actions/BuiltIn/ScreenOcrActionTests.cs`:
```csharp
using System.Collections.Generic;
using System.Drawing;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Ocr;
using AdbCore.Screen;
using AdbCore.Tests.Ocr;
using AdbCore.Tests.Screen;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class ScreenOcrActionTests
{
    private static BotExecutionContext WindowCtx(Guid id, IntPtr handle)
    {
        var ctx = new BotExecutionContext();
        ctx.Targets[id] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "hwnd:1", Handle = handle };
        return ctx;
    }
    private static ActionExecutionContext Exec(BotAction a, BotExecutionContext c) => new(a, c, _ => { });
    private static OcrResult Result(params OcrWord[] w) => new(string.Join(" ", System.Array.ConvertAll(w, x => x.Text)), w);

    [Fact]
    public async Task ReadText_WritesRecognizedTextToVar()
    {
        var id = Guid.NewGuid();
        var action = new BotAction { TargetId = id };
        var read = new ReadTextAction(new FakeWindowCapture(400, 300), new FakeOcrEngine(Result(new OcrWord("Score 42", new Rectangle(0, 0, 9, 9), 0.9))));
        // ReadText writes the full recognized text; FakeOcrEngine's OcrResult.Text = the joined words.

        var r = await read.ExecuteAsync(Exec(action, WindowCtx(id, (IntPtr)5)), default);

        Assert.True(r.Success);
        Assert.Equal("Score 42", ctxText(action));
        string ctxText(BotAction _) => (string)((BotExecutionContext)null! != null ? null! : null!)!; // replaced below
    }
}
```
NOTE: the skeleton above is illustrative only — write the ReadText assertion by reading the variable off the same `BotExecutionContext` you pass in (capture it in a local), e.g.:
```csharp
    [Fact]
    public async Task ReadText_WritesRecognizedTextToVar()
    {
        var id = Guid.NewGuid();
        var ctx = WindowCtx(id, (IntPtr)5);
        var action = new BotAction { TargetId = id };
        var read = new ReadTextAction(new FakeWindowCapture(400, 300), new FakeOcrEngine(Result(new OcrWord("Score", new Rectangle(0, 0, 9, 9), 0.9))));

        var r = await read.ExecuteAsync(Exec(action, ctx), default);

        Assert.True(r.Success);
        Assert.Equal("Score", ctx.Variables["text"]);
    }

    [Fact]
    public async Task FindText_Match_WritesMatchVariables_AndSuccess()
    {
        var id = Guid.NewGuid();
        var ctx = WindowCtx(id, (IntPtr)5);
        var action = new BotAction { TargetId = id, Config = { ["text"] = "attack" } };
        var find = new FindTextAction(new FakeWindowCapture(400, 300), new FakeOcrEngine(Result(new OcrWord("ATTACK", new Rectangle(100, 40, 30, 20), 0.9))), new FixedRandomSource(123));

        var r = await find.ExecuteAsync(Exec(action, ctx), default);

        Assert.True(r.Success);
        Assert.Equal("onSuccess", r.OutputPort);
        Assert.Equal("100", ctx.Variables["matchLeft"]);
        Assert.Equal("130", ctx.Variables["matchRight"]);
        Assert.Equal("123", ctx.Variables["matchRandX"]);
    }

    [Fact]
    public async Task FindText_NoMatch_Fails()
    {
        var id = Guid.NewGuid();
        var ctx = WindowCtx(id, (IntPtr)5);
        var action = new BotAction { TargetId = id, Config = { ["text"] = "attack" } };
        var find = new FindTextAction(new FakeWindowCapture(400, 300), new FakeOcrEngine(Result(new OcrWord("settings", new Rectangle(0, 0, 9, 9), 0.9))), new FixedRandomSource(0));

        var r = await find.ExecuteAsync(Exec(action, ctx), default);

        Assert.False(r.Success);
    }

    [Fact]
    public async Task ReadText_NoWindow_Fails()
    {
        var read = new ReadTextAction(new FakeWindowCapture(10, 10), new FakeOcrEngine(Result()));
        var r = await read.ExecuteAsync(Exec(new BotAction(), new BotExecutionContext()), default);
        Assert.False(r.Success);
        Assert.Contains("Window", r.ErrorMessage);
    }
```
(Delete the first illustrative `ReadText_WritesRecognizedTextToVar` skeleton; keep the four real tests.)

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test <WORKTREE>\AdbCore.Tests --filter "FullyQualifiedName~ScreenOcrActionTests"`
Expected: compile FAIL.

- [ ] **Step 3: Create `AdbCore/Actions/BuiltIn/ScreenOcrActionBase.cs`**
```csharp
using System.Drawing;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Ocr;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Shared base for Screen OCR actions: resolves the target HWND, captures the client area, runs
/// OCR via <see cref="OcrCore"/>. Exposes the shared ROI fields (no Capture Method field — Auto only).</summary>
public abstract class ScreenOcrActionBase : IActionDefinition, IActionExecutor
{
    public const string SuccessPort = "onSuccess";
    public const string FailurePort = "onFailure";
    public const string TextKey = "text";
    public const string ResultVarKey = "resultVar";
    public const string MinConfidenceKey = "minConfidence";

    private readonly IWindowCapture _capture;
    private List<ConfigField>? _configFields;

    protected ScreenOcrActionBase(IWindowCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        _capture = capture;
    }

    protected readonly IOcrEngine Ocr;

    public abstract string TypeKey { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }
    public string Category => "Screen";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public abstract List<PortDefinition> OutputPorts { get; }
    public virtual bool SupportsRetry => true;

    protected abstract IEnumerable<ConfigField> ActionConfigFields { get; }

    public List<ConfigField> ConfigFields => _configFields ??= [.. ActionConfigFields, .. TemplateMatchCore.RegionFields()];

    public abstract Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct);

    protected ScreenOcrActionBase(IWindowCapture capture, IOcrEngine ocr) : this(capture)
    {
        ArgumentNullException.ThrowIfNull(ocr);
        Ocr = ocr;
    }

    /// <summary>Resolves the action's target HWND (explicit TargetId or the sole target).</summary>
    protected static IntPtr? ResolveWindow(ActionExecutionContext context)
    {
        var targets = context.Context.Targets;
        ResolvedTarget? target = context.Action.TargetId is Guid id
            ? targets.TryGetValue(id, out var t) ? t : null
            : targets.Count == 1 ? targets.Values.First() : null;
        return target?.Handle as IntPtr?;
    }

    /// <summary>Captures the client area and OCRs the configured region (full-frame word coords).</summary>
    protected OcrResult RecognizeWindow(ActionExecutionContext context, IntPtr hwnd)
    {
        using var shot = _capture.Capture(hwnd, ScreenCaptureMethod.Auto);
        return OcrCore.RecognizeRegion(shot, context.Action.Config, Ocr);
    }

    protected static ConfigField TextField() => new() { Key = TextKey, Label = "Text", Type = ConfigFieldType.String };
    protected static ConfigField ResultVarField(string def) => new() { Key = ResultVarKey, Label = "Result Variable", Type = ConfigFieldType.String, DefaultValue = def };
    protected static ConfigField MinConfidenceField() => new() { Key = MinConfidenceKey, Label = "Min Confidence", Type = ConfigFieldType.Number, DefaultValue = 0 };
}
```
NOTE: the two-constructor layout above is awkward (a non-nullable readonly `Ocr` field can't be left unset by the first ctor). Simplify to a SINGLE constructor `protected ScreenOcrActionBase(IWindowCapture capture, IOcrEngine ocr)` that sets both fields, and make `Ocr` a `protected IOcrEngine Ocr { get; }`. Remove the parameterless-capture ctor. (The implementer should produce the clean single-ctor version; the field list and methods above are otherwise correct.)

- [ ] **Step 4: Create `ReadTextAction.cs`**
```csharp
using AdbCore.Execution;
using AdbCore.Ocr;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn;

/// <summary>OCRs the target window (or region) and writes the recognized text to a variable.</summary>
public sealed class ReadTextAction : ScreenOcrActionBase
{
    public ReadTextAction(IWindowCapture capture, IOcrEngine ocr) : base(capture, ocr) { }

    public override string TypeKey => "screen.readText";
    public override string DisplayName => "Read Text";
    public override string Description => "Reads text from the target window (or region) into a variable.";
    public override bool SupportsRetry => false;
    public override List<PortDefinition> OutputPorts { get; } = new() { new PortDefinition { Name = "out", Label = "Out" } };

    protected override IEnumerable<ConfigField> ActionConfigFields => [ResultVarField("text")];

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveWindow(context) is not IntPtr hwnd || hwnd == IntPtr.Zero)
        {
            return Task.FromResult(ActionResult.Fail($"{DisplayName} requires a resolved Window target (HWND)."));
        }

        var resultVar = ConfigValues.GetString(context.Action.Config, ResultVarKey, "text");
        if (string.IsNullOrWhiteSpace(resultVar)) { resultVar = "text"; }

        var result = RecognizeWindow(context, hwnd);
        context.Context.Variables[resultVar] = result.Text.Trim();
        return Task.FromResult(ActionResult.Ok("out"));
    }
}
```

- [ ] **Step 5: Create `FindTextAction.cs`**
```csharp
using AdbCore.Execution;
using AdbCore.Ocr;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Finds a target string in the target window via OCR and writes its location (the same match
/// variables as Find Image) under a prefix. Not found is a failed result (engine retry / onFailure).</summary>
public sealed class FindTextAction : ScreenOcrActionBase
{
    private readonly IRandomSource _random;

    public FindTextAction(IWindowCapture capture, IOcrEngine ocr, IRandomSource random) : base(capture, ocr)
    {
        ArgumentNullException.ThrowIfNull(random);
        _random = random;
    }

    public override string TypeKey => "screen.findText";
    public override string DisplayName => "Find Text";
    public override string Description => "Finds a text string in the target window and writes its location to variables.";
    public override List<PortDefinition> OutputPorts { get; } = new()
    {
        new PortDefinition { Name = SuccessPort, Label = "On Success" },
        new PortDefinition { Name = FailurePort, Label = "On Failure" },
    };

    protected override IEnumerable<ConfigField> ActionConfigFields =>
    [
        TextField(),
        ResultVarField(TemplateMatchCore.DefaultResultVar),
        MinConfidenceField(),
    ];

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveWindow(context) is not IntPtr hwnd || hwnd == IntPtr.Zero)
        {
            return Task.FromResult(ActionResult.Fail($"{DisplayName} requires a resolved Window target (HWND)."));
        }

        var target = ConfigValues.GetString(context.Action.Config, TextKey);
        if (string.IsNullOrWhiteSpace(target))
        {
            return Task.FromResult(ActionResult.Fail("Find Text: a target text string is required."));
        }

        var prefix = ConfigValues.GetString(context.Action.Config, ResultVarKey, TemplateMatchCore.DefaultResultVar);
        if (string.IsNullOrWhiteSpace(prefix)) { prefix = TemplateMatchCore.DefaultResultVar; }
        var minConfidence = ConfigValues.GetDouble(context.Action.Config, MinConfidenceKey, 0);

        var result = RecognizeWindow(context, hwnd);
        if (OcrCore.FindWord(result, target, minConfidence) is not MatchResult m)
        {
            return Task.FromResult(ActionResult.Fail($"Find Text: '{target}' not found."));
        }

        TemplateMatchCore.WriteMatchVariables(context.Context.Variables, m, prefix, _random);
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
```

- [ ] **Step 6: Run to verify it passes**

Run: `dotnet test <WORKTREE>\AdbCore.Tests --filter "FullyQualifiedName~ScreenOcrActionTests"`
Expected: PASS (4 tests).

- [ ] **Step 7: Commit**
```bash
git -C <WORKTREE> add AdbCore/Actions/BuiltIn/ScreenOcrActionBase.cs AdbCore/Actions/BuiltIn/ReadTextAction.cs AdbCore/Actions/BuiltIn/FindTextAction.cs AdbCore.Tests/Actions/BuiltIn/ScreenOcrActionTests.cs
git -C <WORKTREE> commit -m "feat(ocr): ScreenOcrActionBase + Read Text + Find Text"
```

---

## Task 6: Wait for Text + Assert Text Absent (Screen)

**Files:** Create `AdbCore/Actions/BuiltIn/WaitForTextAction.cs`, `AssertTextAbsentAction.cs`; append tests to `ScreenOcrActionTests.cs`.

- [ ] **Step 1: Append failing tests** to `ScreenOcrActionTests.cs`:
```csharp
    [Fact]
    public async Task WaitForText_Present_Succeeds()
    {
        var id = Guid.NewGuid();
        var ctx = WindowCtx(id, (IntPtr)5);
        var action = new BotAction { TargetId = id, Config = { ["text"] = "ready", [WaitForTextAction.TimeoutMsKey] = 1000, [WaitForTextAction.PollIntervalMsKey] = 10 } };
        var wait = new WaitForTextAction(new FakeWindowCapture(400, 300), new FakeOcrEngine(Result(new OcrWord("READY", new Rectangle(1, 2, 3, 4), 0.9))), new FixedRandomSource(0));

        var r = await wait.ExecuteAsync(Exec(action, ctx), default);
        Assert.True(r.Success);
    }

    [Fact]
    public async Task WaitForText_Timeout_Fails()
    {
        var id = Guid.NewGuid();
        var ctx = WindowCtx(id, (IntPtr)5);
        var action = new BotAction { TargetId = id, Config = { ["text"] = "ready", [WaitForTextAction.TimeoutMsKey] = 30, [WaitForTextAction.PollIntervalMsKey] = 10 } };
        var wait = new WaitForTextAction(new FakeWindowCapture(400, 300), new FakeOcrEngine(Result(new OcrWord("loading", new Rectangle(1, 2, 3, 4), 0.9))), new FixedRandomSource(0));

        var r = await wait.ExecuteAsync(Exec(action, ctx), default);
        Assert.False(r.Success);
        Assert.Contains("did not appear", r.ErrorMessage);
    }

    [Fact]
    public async Task AssertTextAbsent_Absent_Succeeds_Present_Fails()
    {
        var id = Guid.NewGuid();
        var ctx = WindowCtx(id, (IntPtr)5);
        var absent = new AssertTextAbsentAction(new FakeWindowCapture(400, 300), new FakeOcrEngine(Result(new OcrWord("menu", new Rectangle(1, 2, 3, 4), 0.9))));
        var okAction = new BotAction { TargetId = id, Config = { ["text"] = "gameover" } };
        Assert.True((await absent.ExecuteAsync(Exec(okAction, ctx), default)).Success);

        var present = new AssertTextAbsentAction(new FakeWindowCapture(400, 300), new FakeOcrEngine(Result(new OcrWord("gameover", new Rectangle(1, 2, 3, 4), 0.9))));
        var failAction = new BotAction { TargetId = id, Config = { ["text"] = "gameover" } };
        Assert.False((await present.ExecuteAsync(Exec(failAction, ctx), default)).Success);
    }
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test <WORKTREE>\AdbCore.Tests --filter "FullyQualifiedName~ScreenOcrActionTests"` → compile FAIL.

- [ ] **Step 3: Create `WaitForTextAction.cs`**
```csharp
using System.Diagnostics;
using AdbCore.Execution;
using AdbCore.Ocr;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Polls the target window until the text appears or the timeout elapses.</summary>
public sealed class WaitForTextAction : ScreenOcrActionBase
{
    public const string TimeoutMsKey = "timeoutMs";
    public const string PollIntervalMsKey = "pollIntervalMs";
    public const int DefaultTimeoutMs = 5000;
    public const int DefaultPollIntervalMs = 250;

    private readonly IRandomSource _random;

    public WaitForTextAction(IWindowCapture capture, IOcrEngine ocr, IRandomSource random) : base(capture, ocr)
    {
        ArgumentNullException.ThrowIfNull(random);
        _random = random;
    }

    public override string TypeKey => "screen.waitForText";
    public override string DisplayName => "Wait for Text";
    public override string Description => "Polls the target window until the text appears or the timeout elapses.";
    public override List<PortDefinition> OutputPorts { get; } = new()
    {
        new PortDefinition { Name = SuccessPort, Label = "On Success" },
        new PortDefinition { Name = FailurePort, Label = "On Failure" },
    };

    protected override IEnumerable<ConfigField> ActionConfigFields =>
    [
        TextField(),
        ResultVarField(TemplateMatchCore.DefaultResultVar),
        MinConfidenceField(),
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

        var target = ConfigValues.GetString(context.Action.Config, TextKey);
        if (string.IsNullOrWhiteSpace(target))
        {
            return ActionResult.Fail("Wait for Text: a target text string is required.");
        }

        var prefix = ConfigValues.GetString(context.Action.Config, ResultVarKey, TemplateMatchCore.DefaultResultVar);
        if (string.IsNullOrWhiteSpace(prefix)) { prefix = TemplateMatchCore.DefaultResultVar; }
        var minConfidence = ConfigValues.GetDouble(context.Action.Config, MinConfidenceKey, 0);
        var timeoutMs = Math.Max(0, ConfigValues.GetInt(context.Action.Config, TimeoutMsKey, DefaultTimeoutMs));
        var pollMs = Math.Max(1, ConfigValues.GetInt(context.Action.Config, PollIntervalMsKey, DefaultPollIntervalMs));

        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            var result = RecognizeWindow(context, hwnd);
            if (OcrCore.FindWord(result, target, minConfidence) is MatchResult m)
            {
                TemplateMatchCore.WriteMatchVariables(context.Context.Variables, m, prefix, _random);
                return ActionResult.Ok(SuccessPort);
            }
            if (elapsed.ElapsedMilliseconds >= timeoutMs)
            {
                return ActionResult.Fail($"Wait for Text: '{target}' did not appear within {timeoutMs} ms.");
            }
            await Task.Delay(pollMs, ct);
        }
    }
}
```

- [ ] **Step 4: Create `AssertTextAbsentAction.cs`**
```csharp
using AdbCore.Execution;
using AdbCore.Ocr;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Succeeds only when the target text is NOT present in the target window (present → Fail, so
/// with a RetryPolicy it becomes "wait until the text is gone").</summary>
public sealed class AssertTextAbsentAction : ScreenOcrActionBase
{
    public AssertTextAbsentAction(IWindowCapture capture, IOcrEngine ocr) : base(capture, ocr) { }

    public override string TypeKey => "screen.assertTextAbsent";
    public override string DisplayName => "Assert Text Absent";
    public override string Description => "Succeeds when the target text is not present in the target window.";
    public override List<PortDefinition> OutputPorts { get; } = new()
    {
        new PortDefinition { Name = SuccessPort, Label = "On Success" },
        new PortDefinition { Name = FailurePort, Label = "On Failure" },
    };

    protected override IEnumerable<ConfigField> ActionConfigFields => [TextField(), MinConfidenceField()];

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveWindow(context) is not IntPtr hwnd || hwnd == IntPtr.Zero)
        {
            return Task.FromResult(ActionResult.Fail($"{DisplayName} requires a resolved Window target (HWND)."));
        }

        var target = ConfigValues.GetString(context.Action.Config, TextKey);
        if (string.IsNullOrWhiteSpace(target))
        {
            return Task.FromResult(ActionResult.Fail("Assert Text Absent: a target text string is required."));
        }

        var minConfidence = ConfigValues.GetDouble(context.Action.Config, MinConfidenceKey, 0);
        var result = RecognizeWindow(context, hwnd);

        return OcrCore.FindWord(result, target, minConfidence) is MatchResult
            ? Task.FromResult(ActionResult.Fail($"Assert Text Absent: '{target}' is present."))
            : Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
```

- [ ] **Step 5: Run to verify it passes** — `dotnet test <WORKTREE>\AdbCore.Tests --filter "FullyQualifiedName~ScreenOcrActionTests"` → PASS (7 tests).

- [ ] **Step 6: Commit**
```bash
git -C <WORKTREE> add AdbCore/Actions/BuiltIn/WaitForTextAction.cs AdbCore/Actions/BuiltIn/AssertTextAbsentAction.cs AdbCore.Tests/Actions/BuiltIn/ScreenOcrActionTests.cs
git -C <WORKTREE> commit -m "feat(ocr): Wait for Text + Assert Text Absent (Screen)"
```

---

## Task 7: Register Screen OCR actions + update counts

**Files:** Modify `BuiltInActions.cs`, `BuiltInActionsTests.cs`, `PaletteViewModelTests.cs`; create `ScreenOcrRegistrationTests.cs`.

- [ ] **Step 1: Write the failing registration test**

Create `AdbCore.Tests/Actions/BuiltIn/ScreenOcrRegistrationTests.cs`:
```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class ScreenOcrRegistrationTests
{
    [Theory]
    [InlineData("screen.readText")]
    [InlineData("screen.findText")]
    [InlineData("screen.waitForText")]
    [InlineData("screen.assertTextAbsent")]
    public void ScreenOcrAction_IsRegistered(string typeKey)
    {
        var defs = new ActionRegistry();
        var execs = new ActionExecutorRegistry();
        BuiltInActions.Register(defs, execs);

        Assert.True(defs.TryGet(typeKey, out _));
        Assert.True(execs.TryGet(typeKey, out var exec) && exec is not null);
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test <WORKTREE>\AdbCore.Tests --filter "FullyQualifiedName~ScreenOcrRegistrationTests"` → FAIL.

- [ ] **Step 3: Register**

In `AdbCore/Actions/BuiltIn/BuiltInActions.cs`, after the existing Screen block (`Add(new ScreenshotAction(windowCapture), ...);`), add (a shared `TesseractOcrEngine` instance, reusing the existing `randomSource`):
```csharp

        // Screen OCR (Tesseract; reuses the window capture + RNG).
        var ocrEngine = new AdbCore.Ocr.TesseractOcrEngine();
        Add(new ReadTextAction(windowCapture, ocrEngine), definitions, executors);
        Add(new FindTextAction(windowCapture, ocrEngine, randomSource), definitions, executors);
        Add(new WaitForTextAction(windowCapture, ocrEngine, randomSource), definitions, executors);
        Add(new AssertTextAbsentAction(windowCapture, ocrEngine), definitions, executors);
```

- [ ] **Step 4: Update counts**

In `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs`, the current counts are `34`/`31` (post-M10a). Bump both by 4:
```csharp
        Assert.Equal(38, defs.Count);
        Assert.Equal(35, execs.Count);
```

In `BotBuilder.Core.Tests/PaletteViewModelTests.cs`:
- `Categories_GroupBuiltInsByCategory`: the Screen category was 4; add an assertion if one isn't present, or update it to 8. Add after the Android assertion:
```csharp
        var screen = palette.Categories.Single(c => c.Name == "Screen");
        Assert.Equal(8, screen.Items.Count); // Find/Wait/AssertAbsent Image + Screenshot + Read/Find/Wait/AssertAbsent Text
```
- `ClearingSearch_RestoresAll`: change `34` to `38` and update the comment to `... + 8 Screen + 9 Android + 5 Browser`.

(If the current numbers differ from 34/31 or the Screen total isn't 4, read the files and adjust by +4 Screen / +4 total. STOP and report if the base numbers are unexpected.)

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test <WORKTREE>\AdbCore.Tests --filter "FullyQualifiedName~ScreenOcrRegistrationTests|FullyQualifiedName~BuiltInActionsTests"`
Then: `dotnet test <WORKTREE>\BotBuilder.Core.Tests --filter "FullyQualifiedName~PaletteViewModelTests"`
Expected: PASS.

- [ ] **Step 6: Commit**
```bash
git -C <WORKTREE> add AdbCore/Actions/BuiltIn/BuiltInActions.cs AdbCore.Tests/Actions/BuiltIn/ScreenOcrRegistrationTests.cs AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs BotBuilder.Core.Tests/PaletteViewModelTests.cs
git -C <WORKTREE> commit -m "feat(ocr): register the 4 Screen OCR actions + update palette/registry counts"
```

---

## Task 8: Build + test sweep + PR (user-verified)

- [ ] **Step 1: Build, 0 warnings** — `dotnet build <WORKTREE>\ADB.slnx` → success, 0 warnings.
- [ ] **Step 2: Full suite** — `dotnet test <WORKTREE>\ADB.slnx` → all pass. New AdbCore.Tests: +1 OcrContract, +5 OcrCore, +7 ScreenOcrAction, +4 ScreenOcrRegistration theory cases. Report counts.
- [ ] **Step 3: Push + open PR (DO NOT MERGE)** — push `worktree-m11a-ocr-screen`; `gh pr create` with a summary + the live-verify ask. This PR is **parked for the user** to verify real OCR (point a Find Text at a window with known text, confirm it locates + the match vars drive a click; Read Text returns the text). Report the PR URL.

---

## Self-Review Notes (addressed)

- **Spec coverage:** engine always-on + tessdata bundle (Task 1); `IOcrEngine`/records (Task 2); `OcrCore` ROI+find reusing `TemplateMatchCore` (Task 3); `TesseractOcrEngine` live (Task 4); 4 Screen actions w/ correct semantics + match-variable reuse for Find/Wait (Tasks 5–6); registration + counts (Task 7); fake-engine unit tests + live-verify handoff (all). ✓
- **Match contract reuse:** Find/Wait Text call `TemplateMatchCore.WriteMatchVariables`, so the `matchRandX/Y` contract is identical to Find Image. ✓
- **Type consistency:** `OcrResult`/`OcrWord`/`IOcrEngine`, `OcrCore.RecognizeRegion`/`FindWord`, `ScreenOcrActionBase` (single `(IWindowCapture, IOcrEngine)` ctor + protected `Ocr`), action ctors, and `TextKey/ResultVarKey/MinConfidenceKey` are referenced consistently. `MatchResult` reused from `AdbCore.Screen`. ✓
- **Known adaptive points (flagged in-task):** the charlesw Tesseract 5.x API (Task 4) and the exact base-count numbers (Task 7) are verified against the real package/files by the implementer. The `ScreenOcrActionBase` two-ctor sketch is explicitly called out to be collapsed to one clean ctor.
- **No placeholders elsewhere:** every other step has complete code + exact commands.
