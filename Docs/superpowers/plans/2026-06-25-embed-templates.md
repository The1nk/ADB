# Embed Image Templates in `.bot` — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Store each image-action template's bytes inside the `.bot` (base64), so a saved bot is self-contained; existing path-based bots are upgraded in memory on load and embedded on save (path stripped to basename).

**Architecture:** A companion config key `templateImage` (base64) sits beside `templatePath`. The matcher gains a bytes-based `Match` overload; `TemplateMatchCore` centrally resolves embedded-bytes-else-path so path-based bots (and their tests) flow through the existing path method unchanged. The editor embeds at load (in memory) and strips the path to basename at save, and previews from embedded bytes so an embedded bot renders even when the source PNG is gone.

**Tech Stack:** .NET 10, OpenCvSharp4 (`Cv2.ImDecode`), System.Text.Json, WPF/CommunityToolkit.Mvvm, xUnit.

**Spec:** `docs/superpowers/specs/2026-06-25-embed-templates-design.md`

**Conventions for every task:** Work in the `C:/git/ADB-embed-templates` worktree. Build `dotnet build ADB.slnx`; test `dotnet test ADB.slnx`; narrow with `dotnet test ADB.slnx --filter "FullyQualifiedName~<Class>"`. NO `/dev/null` redirection (Windows). C#: file-scoped namespaces, nullable, `_camelCase` fields, XML summaries on public types.

---

## File Structure

**Modify (AdbCore):**
- `Screen/ITemplateMatcher.cs` — add a `byte[]` overload.
- `Screen/OpenCvSharpTemplateMatcher.cs` — implement bytes via `Cv2.ImDecode`; share post-decode matching.
- `Actions/BuiltIn/TemplateMatchCore.cs` — `TemplateImageKey`, `HasTemplate`, resolve embedded-vs-path inside `MatchInRegion`.
- `Actions/BuiltIn/ScreenActionBase.cs` + `Android/AndroidImageActionBase.cs` — `CaptureAndMatch` drops the `templatePath` param.
- The six image actions — validate via `HasTemplate`, drop the `templatePath` local.

**Create (BotBuilder.Core):** `TemplateEmbedder.cs`.
**Modify (BotBuilder.Core):** `BotEditorViewModel.cs` (Open/Save/ExportTo wiring), `Properties/ConfigFieldViewModel.cs` (embedded-bytes accessor + clear-on-repath).
**Create (BotBuilder):** `TemplateImageConverter.cs`.
**Modify (BotBuilder):** `MainWindow.xaml` (ImagePath preview → MultiBinding).
**Tests:** AdbCore.Tests (matcher + TemplateMatchCore + base-test signature updates + embedded-branch action test), BotBuilder.Core.Tests (TemplateEmbedder + editor round-trip), BotCapture.Core.Tests (fake gains the overload).

---

## Task 1: `ITemplateMatcher` gains a bytes overload

**Files:**
- Modify: `AdbCore/Screen/ITemplateMatcher.cs`, `AdbCore/Screen/OpenCvSharpTemplateMatcher.cs`
- Modify (compile fix): `AdbCore.Tests/Screen/FakeScreenDependencies.cs`, `BotCapture.Core.Tests/Fakes.cs`
- Test: `AdbCore.Tests/Screen/OpenCvSharpTemplateMatcherBytesTests.cs`

- [ ] **Step 1: Write the failing test**

Create `AdbCore.Tests/Screen/OpenCvSharpTemplateMatcherBytesTests.cs`:

```csharp
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using AdbCore.Screen;
using Xunit;

namespace AdbCore.Tests.Screen;

public class OpenCvSharpTemplateMatcherBytesTests
{
    private static byte[] PngOf(Color fill, int w, int h)
    {
        using var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp)) { g.Clear(fill); }
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    [Fact]
    public void Match_FromBytes_FindsTemplateInHaystack()
    {
        using var haystack = new Bitmap(40, 30, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(haystack))
        {
            g.Clear(Color.White);
            g.FillRectangle(Brushes.Black, 8, 6, 10, 10);
        }
        var templatePng = PngOf(Color.Black, 10, 10);

        var hit = new OpenCvSharpTemplateMatcher().Match(haystack, templatePng, 0.5);

        Assert.NotNull(hit);
        Assert.Equal(10, hit!.Width);
        Assert.Equal(10, hit.Height);
    }

    [Fact]
    public void Match_FromEmptyBytes_Throws()
    {
        using var haystack = new Bitmap(10, 10);
        Assert.ThrowsAny<Exception>(() => new OpenCvSharpTemplateMatcher().Match(haystack, Array.Empty<byte>(), 0.5));
    }
}
```

- [ ] **Step 2: Run, verify it fails to compile**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~OpenCvSharpTemplateMatcherBytesTests"`
Expected: FAIL — no `Match(Bitmap, byte[], double)` overload.

- [ ] **Step 3: Add the interface overload**

Replace `AdbCore/Screen/ITemplateMatcher.cs`:

```csharp
using System.Drawing;

namespace AdbCore.Screen;

/// <summary>Finds a template image within a haystack bitmap. Returns the single best match when its score
/// meets <paramref name="minConfidence"/> (0–1), else null. The template is supplied either by file path or
/// by in-memory image bytes (for templates embedded in a .bot). Throws if the template can't be read/decoded.</summary>
public interface ITemplateMatcher
{
    MatchResult? Match(Bitmap haystack, string templatePath, double minConfidence);

    MatchResult? Match(Bitmap haystack, byte[] templatePng, double minConfidence);
}
```

- [ ] **Step 4: Implement the overload + share matching**

Replace `AdbCore/Screen/OpenCvSharpTemplateMatcher.cs`:

```csharp
using System.Drawing;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace AdbCore.Screen;

/// <summary>Template matching via OpenCvSharp (<c>TM_CCOEFF_NORMED</c>, single best match). Accepts the
/// template by file path or by in-memory image bytes (templates embedded in a .bot).</summary>
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

        return MatchMat(haystack, template, minConfidence);
    }

    public MatchResult? Match(Bitmap haystack, byte[] templatePng, double minConfidence)
    {
        if (templatePng is null || templatePng.Length == 0)
        {
            throw new InvalidOperationException("Embedded template image is empty.");
        }

        using var template = Cv2.ImDecode(templatePng, ImreadModes.Color);
        if (template.Empty())
        {
            throw new InvalidOperationException("Embedded template image could not be decoded.");
        }

        return MatchMat(haystack, template, minConfidence);
    }

    private static MatchResult? MatchMat(Bitmap haystack, Mat template, double minConfidence)
    {
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

- [ ] **Step 5: Make the two test fakes implement the new method (so the solution compiles)**

In `AdbCore.Tests/Screen/FakeScreenDependencies.cs`, add to `FakeTemplateMatcher` (after the existing `Match`):

```csharp
    public byte[]? LastTemplateBytes { get; private set; }

    public MatchResult? Match(Bitmap haystack, byte[] templatePng, double minConfidence)
    {
        LastHaystackWidth = haystack.Width;
        LastHaystackHeight = haystack.Height;
        LastTemplateBytes = templatePng;
        LastConfidence = minConfidence;
        return result;
    }
```

In `BotCapture.Core.Tests/Fakes.cs`, add to `FakeTemplateMatcher` (after the existing `Match`):

```csharp
    public byte[]? LastTemplateBytes;

    public AdbCore.Screen.MatchResult? Match(System.Drawing.Bitmap haystack, byte[] templatePng, double minConfidence)
    {
        LastTemplateBytes = templatePng;
        LastMinConfidence = minConfidence;
        if (Throw is not null) throw Throw;
        return Next;
    }
```

- [ ] **Step 6: Run the matcher test + full build**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~OpenCvSharpTemplateMatcherBytesTests"`
Expected: PASS (2). Then `dotnet build ADB.slnx` → Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add AdbCore/Screen/ITemplateMatcher.cs AdbCore/Screen/OpenCvSharpTemplateMatcher.cs AdbCore.Tests/Screen/FakeScreenDependencies.cs BotCapture.Core.Tests/Fakes.cs AdbCore.Tests/Screen/OpenCvSharpTemplateMatcherBytesTests.cs
git commit -m "Add bytes-based ITemplateMatcher.Match overload (Cv2.ImDecode)"
```

---

## Task 2: `TemplateMatchCore` resolves embedded-vs-path

**Files:**
- Modify: `AdbCore/Actions/BuiltIn/TemplateMatchCore.cs`
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
    public void HasTemplate_TrueForEmbedded_TrueForPath_FalseForNeither()
    {
        Assert.True(TemplateMatchCore.HasTemplate(new Dictionary<string, object> { [TemplateMatchCore.TemplateImageKey] = "abc" }));
        Assert.True(TemplateMatchCore.HasTemplate(new Dictionary<string, object> { [TemplateMatchCore.TemplatePathKey] = "btn.png" }));
        Assert.False(TemplateMatchCore.HasTemplate(new Dictionary<string, object>()));
    }

    [Fact]
    public void MatchInRegion_EmbeddedImage_MatchesViaBytes()
    {
        using var haystack = new Bitmap(40, 30);
        var matcher = new FakeTemplateMatcher(new MatchResult(1, 2, 3, 4, 0.9));
        var config = new Dictionary<string, object>
        {
            [TemplateMatchCore.TemplateImageKey] = System.Convert.ToBase64String(new byte[] { 1, 2, 3 }),
        };

        var hit = TemplateMatchCore.MatchInRegion(haystack, config, matcher, 0.8);

        Assert.NotNull(hit);
        Assert.NotNull(matcher.LastTemplateBytes);          // bytes path used
        Assert.Equal(new byte[] { 1, 2, 3 }, matcher.LastTemplateBytes);
        Assert.Null(matcher.LastTemplatePath);
    }

    [Fact]
    public void MatchInRegion_NoEmbedded_MatchesViaPath()
    {
        using var haystack = new Bitmap(40, 30);
        var matcher = new FakeTemplateMatcher(new MatchResult(1, 2, 3, 4, 0.9));
        var config = new Dictionary<string, object> { [TemplateMatchCore.TemplatePathKey] = "btn.png" };

        TemplateMatchCore.MatchInRegion(haystack, config, matcher, 0.8);

        Assert.Equal("btn.png", matcher.LastTemplatePath);  // path branch used
        Assert.Null(matcher.LastTemplateBytes);
    }
}
```

(Add `public string? LastTemplatePath` is already present on the fake; `LastTemplateBytes` was added in Task 1.)

- [ ] **Step 2: Run, verify failure**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~TemplateMatchCoreTests"`
Expected: FAIL — `TemplateImageKey`/`HasTemplate` absent; `MatchInRegion` still takes a `templatePath` arg.

- [ ] **Step 3: Edit `TemplateMatchCore.cs`**

Add the key constant after `TemplatePathKey` (line 14):

```csharp
    public const string TemplateImageKey = "templateImage";
```

Add `HasTemplate` (place after the `RegionFields()` method):

```csharp
    /// <summary>True when the config carries a template — embedded base64 bytes or a (non-empty) source path.</summary>
    public static bool HasTemplate(IReadOnlyDictionary<string, object> config)
        => !string.IsNullOrWhiteSpace(ConfigValues.GetString(config, TemplateImageKey))
           || !string.IsNullOrWhiteSpace(ConfigValues.GetString(config, TemplatePathKey));
```

Replace the `MatchInRegion` method (lines 56-69) with a version that drops the `templatePath` param and resolves the source internally:

```csharp
    /// <summary>Crops the haystack to the configured ROI (if any), matches the configured template (embedded
    /// bytes when present, else the source path), and returns the match in full-haystack coordinates (null
    /// when none ≥ confidence). Does not dispose the haystack.</summary>
    public static MatchResult? MatchInRegion(Bitmap haystack, IReadOnlyDictionary<string, object> config, ITemplateMatcher matcher, double confidence)
    {
        var region = ResolveRegion(config, haystack.Width, haystack.Height);
        if (region is not Rectangle roi)
        {
            return MatchConfigured(haystack, config, matcher, confidence);
        }

        using var crop = haystack.Clone(roi, haystack.PixelFormat);
        var hit = MatchConfigured(crop, config, matcher, confidence);
        return hit is MatchResult m ? m with { X = m.X + roi.X, Y = m.Y + roi.Y } : null;
    }

    // Prefers the embedded base64 image; falls back to the source path (which the matcher reads from disk).
    private static MatchResult? MatchConfigured(Bitmap haystack, IReadOnlyDictionary<string, object> config, ITemplateMatcher matcher, double confidence)
    {
        var embedded = ConfigValues.GetString(config, TemplateImageKey);
        if (!string.IsNullOrWhiteSpace(embedded))
        {
            return matcher.Match(haystack, Convert.FromBase64String(embedded), confidence);
        }

        return matcher.Match(haystack, ConfigValues.GetString(config, TemplatePathKey), confidence);
    }
```

- [ ] **Step 4: Run the tests (expect the action bases to break — fixed in Task 3)**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~TemplateMatchCoreTests"`
Expected: the TemplateMatchCore tests build only once Task 3 fixes the `CaptureAndMatch` callers. Instead verify this task by building the AdbCore project's compile of TemplateMatchCore in isolation is blocked by callers; proceed to Task 3 and run these tests at Task 3 Step 6. (Do NOT commit a broken build — commit Task 2 together with Task 3.)

> Note: `MatchInRegion`'s signature change breaks `ScreenActionBase.CaptureAndMatch` and `AndroidImageActionBase.CaptureAndMatch`, which still pass `templatePath`. Task 3 updates them. Keep Task 2's edits staged and commit after Task 3 builds green.

---

## Task 3: Action bases + the six actions use `HasTemplate` and the new `CaptureAndMatch`

**Files:**
- Modify: `AdbCore/Actions/BuiltIn/ScreenActionBase.cs`, `AdbCore/Actions/BuiltIn/Android/AndroidImageActionBase.cs`
- Modify: `FindImageAction.cs`, `WaitForImageAction.cs`, `AssertImageAbsentAction.cs`, `Android/AndroidFindImageAction.cs`, `Android/AndroidWaitForImageAction.cs`, `Android/AndroidAssertImageAbsentAction.cs`
- Modify (tests): `AdbCore.Tests/Actions/BuiltIn/ScreenActionBaseTests.cs`, `AdbCore.Tests/Actions/BuiltIn/Android/AndroidImageActionBaseTests.cs`
- Test (new): add an embedded-branch case to `AdbCore.Tests/Actions/BuiltIn/FindImageActionTests.cs`

- [ ] **Step 1: Update `ScreenActionBase.CaptureAndMatch` (drop `templatePath`)**

In `AdbCore/Actions/BuiltIn/ScreenActionBase.cs`, replace the `CaptureAndMatch` method (lines 108-114):

```csharp
    /// <summary>Captures the window's client area via the chosen method, then crops to any ROI, matches the
    /// configured template, and returns the match in full-window client coordinates (null if none ≥ confidence).</summary>
    protected MatchResult? CaptureAndMatch(ActionExecutionContext context, IntPtr hwnd, ITemplateMatcher matcher, double confidence)
    {
        using var shot = _capture.Capture(hwnd, CaptureMethodOf(context));
        return TemplateMatchCore.MatchInRegion(shot, context.Action.Config, matcher, confidence);
    }
```

- [ ] **Step 2: Update `AndroidImageActionBase.CaptureAndMatch` (drop `templatePath`)**

In `AdbCore/Actions/BuiltIn/Android/AndroidImageActionBase.cs`, replace the `CaptureAndMatch` method (lines 30-35):

```csharp
    /// <summary>Captures the device framebuffer, crops to any ROI, matches the configured template, and
    /// returns the match in full-frame device-pixel coordinates (null if none ≥ confidence).</summary>
    protected static MatchResult? CaptureAndMatch(ActionExecutionContext context, IAndroidDevice device, ITemplateMatcher matcher, double confidence)
    {
        using var ms = new MemoryStream(device.Screenshot());
        using var frame = new Bitmap(ms);
        return TemplateMatchCore.MatchInRegion(frame, context.Action.Config, matcher, confidence);
    }
```

- [ ] **Step 3: Update the six actions**

For each action: replace its `templatePath` resolve+blank-check block with a `HasTemplate` check, and drop `templatePath` from the `CaptureAndMatch` call. Exact edits:

**`FindImageAction.cs`** — replace lines 49-53 with:
```csharp
        if (!TemplateMatchCore.HasTemplate(context.Action.Config))
        {
            return Task.FromResult(ActionResult.Fail("Find Image: a template image is required."));
        }
```
and line 62 `CaptureAndMatch(context, hwnd, _matcher, templatePath, confidence)` → `CaptureAndMatch(context, hwnd, _matcher, confidence)`.

**`WaitForImageAction.cs`** — replace lines 56-60 with:
```csharp
        if (!TemplateMatchCore.HasTemplate(context.Action.Config))
        {
            return ActionResult.Fail("Wait for Image: a template image is required.");
        }
```
and line 75 `CaptureAndMatch(context, hwnd, _matcher, templatePath, confidence)` → `CaptureAndMatch(context, hwnd, _matcher, confidence)`.

**`AssertImageAbsentAction.cs`** — replace lines 45-49 with:
```csharp
        if (!TemplateMatchCore.HasTemplate(context.Action.Config))
        {
            return Task.FromResult(ActionResult.Fail("Assert Image Absent: a template image is required."));
        }
```
and line 53 `CaptureAndMatch(context, hwnd, _matcher, templatePath, confidence)` → `CaptureAndMatch(context, hwnd, _matcher, confidence)`.

**`Android/AndroidFindImageAction.cs`** — replace lines 41-45 with:
```csharp
        if (!TemplateMatchCore.HasTemplate(context.Action.Config))
        {
            return Task.FromResult(ActionResult.Fail("Find Image (Android): a template image is required."));
        }
```
and line 54 `CaptureAndMatch(context, device, _matcher, templatePath, confidence)` → `CaptureAndMatch(context, device, _matcher, confidence)`.

**`Android/AndroidWaitForImageAction.cs`** — replace lines 48-52 with:
```csharp
        if (!TemplateMatchCore.HasTemplate(context.Action.Config))
        {
            return ActionResult.Fail("Wait for Image (Android): a template image is required.");
        }
```
and line 67 `CaptureAndMatch(context, device, _matcher, templatePath, confidence)` → `CaptureAndMatch(context, device, _matcher, confidence)`.

**`Android/AndroidAssertImageAbsentAction.cs`** — replace lines 38-42 with:
```csharp
        if (!TemplateMatchCore.HasTemplate(context.Action.Config))
        {
            return Task.FromResult(ActionResult.Fail("Assert Image Absent (Android): a template image is required."));
        }
```
and line 46 `CaptureAndMatch(context, device, _matcher, templatePath, confidence)` → `CaptureAndMatch(context, device, _matcher, confidence)`.

- [ ] **Step 4: Update the two base tests to the new `CaptureAndMatch` signature**

In `AdbCore.Tests/Actions/BuiltIn/ScreenActionBaseTests.cs`, change the helper (lines 24-25) to drop the `template` param:
```csharp
        public MatchResult? CallCaptureAndMatch(ActionExecutionContext ctx, IntPtr hwnd, double confidence)
            => CaptureAndMatch(ctx, hwnd, _matcher, confidence);
```
and at every call site remove the `"t.png"` argument: `action.CallCaptureAndMatch(Exec(...), (IntPtr)1, 0.8)` (lines 37, 56, 75, 87, 91).

In `AdbCore.Tests/Actions/BuiltIn/Android/AndroidImageActionBaseTests.cs`, change the helper (lines 28-29):
```csharp
        public MatchResult? CallCaptureAndMatch(ActionExecutionContext ctx, IAndroidDevice device, double confidence)
            => CaptureAndMatch(ctx, device, _matcher, confidence);
```
and drop the `"t.png"` argument at both call sites (lines 49, 69): `action.CallCaptureAndMatch(Exec(...), device, 0.8)`.

(The faked matcher returns its canned result regardless of the empty path/bytes, so the ROI/offset assertions are unchanged.)

- [ ] **Step 5: Add an embedded-branch action test**

In `AdbCore.Tests/Actions/BuiltIn/FindImageActionTests.cs`, add:

```csharp
    [Fact]
    public async Task EmbeddedTemplate_MatchesViaBytes_RoutesSuccess()
    {
        var id = Guid.NewGuid();
        var ctx = WindowContext(id, (IntPtr)5);
        var matcher = new FakeTemplateMatcher(new MatchResult(1, 2, 3, 4, 0.95));
        var action = new BotAction { TargetId = id, Config =
        {
            [FindImageAction.TemplatePathKey] = "btn.png",
            [AdbCore.Actions.BuiltIn.TemplateMatchCore.TemplateImageKey] = Convert.ToBase64String(new byte[] { 9, 8, 7 }),
        } };

        var find = new FindImageAction(new FakeWindowCapture(800, 600), matcher, new FixedRandomSource(0));
        var result = await find.ExecuteAsync(Exec(action, ctx), default);

        Assert.True(result.Success);
        Assert.Equal(new byte[] { 9, 8, 7 }, matcher.LastTemplateBytes); // embedded bytes used, not the path
    }
```

Also update the existing `BlankTemplatePath_Fails` assertion message check is still valid (`Assert.Contains("template", result.ErrorMessage)` — the new message "a template image is required" still contains "template").

- [ ] **Step 6: Run the full AdbCore suite (incl. Task 2's tests) + build**

Run: `dotnet build ADB.slnx` → Build succeeded. Then `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests"`
Expected: all pass, including `TemplateMatchCoreTests`, `OpenCvSharpTemplateMatcherBytesTests`, the updated base tests, and the new embedded-branch test.

- [ ] **Step 7: Commit (Task 2 + Task 3 together)**

```bash
git add AdbCore/Actions/BuiltIn/TemplateMatchCore.cs AdbCore/Actions/BuiltIn/ScreenActionBase.cs AdbCore/Actions/BuiltIn/Android/AndroidImageActionBase.cs AdbCore/Actions/BuiltIn/FindImageAction.cs AdbCore/Actions/BuiltIn/WaitForImageAction.cs AdbCore/Actions/BuiltIn/AssertImageAbsentAction.cs AdbCore/Actions/BuiltIn/Android/AndroidFindImageAction.cs AdbCore/Actions/BuiltIn/Android/AndroidWaitForImageAction.cs AdbCore/Actions/BuiltIn/Android/AndroidAssertImageAbsentAction.cs AdbCore.Tests/Actions/BuiltIn/TemplateMatchCoreTests.cs AdbCore.Tests/Actions/BuiltIn/ScreenActionBaseTests.cs AdbCore.Tests/Actions/BuiltIn/Android/AndroidImageActionBaseTests.cs AdbCore.Tests/Actions/BuiltIn/FindImageActionTests.cs
git commit -m "Resolve embedded-or-path template in TemplateMatchCore; actions validate via HasTemplate"
```

---

## Task 4: `TemplateEmbedder` (embed + strip)

**Files:**
- Create: `BotBuilder.Core/TemplateEmbedder.cs`
- Test: `BotBuilder.Core.Tests/TemplateEmbedderTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `BotBuilder.Core.Tests/TemplateEmbedderTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using AdbCore.Actions.BuiltIn;
using AdbCore.Models;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class TemplateEmbedderTests
{
    private static Bot BotWith(Dictionary<string, object> config)
        => new() { Actions = { new BotAction { TypeKey = "screen.findImage", Config = config } } };

    [Fact]
    public void Embed_FillsTemplateImageFromReadableFile_LeavesPath()
    {
        var bot = BotWith(new() { [TemplateMatchCore.TemplatePathKey] = @"C:\caps\btn.png" });
        var read = (string? p) => p == @"C:\caps\btn.png" ? new byte[] { 1, 2, 3 } : null;

        TemplateEmbedder.Embed(bot, read);

        Assert.Equal(Convert.ToBase64String(new byte[] { 1, 2, 3 }),
            bot.Actions[0].Config[TemplateMatchCore.TemplateImageKey]);
        Assert.Equal(@"C:\caps\btn.png", bot.Actions[0].Config[TemplateMatchCore.TemplatePathKey]);
    }

    [Fact]
    public void Embed_Idempotent_DoesNotOverwriteExistingImage()
    {
        var bot = BotWith(new()
        {
            [TemplateMatchCore.TemplatePathKey] = @"C:\caps\btn.png",
            [TemplateMatchCore.TemplateImageKey] = "ALREADY",
        });

        TemplateEmbedder.Embed(bot, _ => new byte[] { 9 });

        Assert.Equal("ALREADY", bot.Actions[0].Config[TemplateMatchCore.TemplateImageKey]);
    }

    [Fact]
    public void Embed_MissingFile_LeavesNoImage()
    {
        var bot = BotWith(new() { [TemplateMatchCore.TemplatePathKey] = @"C:\gone.png" });

        TemplateEmbedder.Embed(bot, _ => null);

        Assert.False(bot.Actions[0].Config.ContainsKey(TemplateMatchCore.TemplateImageKey));
    }

    [Fact]
    public void PrepareForSave_EmbedsThenStripsPathToBasename()
    {
        var bot = BotWith(new() { [TemplateMatchCore.TemplatePathKey] = @"C:\caps\sub\btn.png" });

        TemplateEmbedder.PrepareForSave(bot, _ => new byte[] { 1 });

        Assert.Equal("btn.png", bot.Actions[0].Config[TemplateMatchCore.TemplatePathKey]);
        Assert.True(bot.Actions[0].Config.ContainsKey(TemplateMatchCore.TemplateImageKey));
    }

    [Fact]
    public void PrepareForSave_NoEmbeddableImage_LeavesPathUnchanged()
    {
        var bot = BotWith(new() { [TemplateMatchCore.TemplatePathKey] = @"C:\gone.png" });

        TemplateEmbedder.PrepareForSave(bot, _ => null);

        Assert.Equal(@"C:\gone.png", bot.Actions[0].Config[TemplateMatchCore.TemplatePathKey]);
    }
}
```

- [ ] **Step 2: Run, verify failure**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~TemplateEmbedderTests"`
Expected: FAIL — `TemplateEmbedder` does not exist.

- [ ] **Step 3: Create `BotBuilder.Core/TemplateEmbedder.cs`**

```csharp
using System.IO;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Models;

namespace BotBuilder.Core;

/// <summary>Embeds image-action templates into the bot model as base64 (so a saved .bot is self-contained)
/// and, on save, strips the source path to its basename. Pure; the file reader is injected for testing.</summary>
public static class TemplateEmbedder
{
    /// <summary>Reads a file's bytes, or null when the path is empty or the file does not exist.</summary>
    public static byte[]? ReadFileIfExists(string? path)
        => !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? File.ReadAllBytes(path) : null;

    /// <summary>For each action with a source path but no embedded image yet, reads the file and stores its
    /// base64 under the templateImage key. Idempotent; leaves the path untouched. Mutates and returns the bot.</summary>
    public static Bot Embed(Bot bot, Func<string?, byte[]?> read)
    {
        foreach (var action in bot.Actions)
        {
            if (!string.IsNullOrWhiteSpace(Get(action, TemplateMatchCore.TemplateImageKey)))
            {
                continue;
            }

            if (read(Get(action, TemplateMatchCore.TemplatePathKey)) is byte[] bytes)
            {
                action.Config[TemplateMatchCore.TemplateImageKey] = Convert.ToBase64String(bytes);
            }
        }

        return bot;
    }

    /// <summary>Embeds any not-yet-embedded templates, then rewrites the source path to its basename for every
    /// action that now carries embedded bytes. Mutates and returns the bot.</summary>
    public static Bot PrepareForSave(Bot bot, Func<string?, byte[]?> read)
    {
        Embed(bot, read);

        foreach (var action in bot.Actions)
        {
            if (string.IsNullOrWhiteSpace(Get(action, TemplateMatchCore.TemplateImageKey)))
            {
                continue;
            }

            var path = Get(action, TemplateMatchCore.TemplatePathKey);
            if (!string.IsNullOrWhiteSpace(path))
            {
                action.Config[TemplateMatchCore.TemplatePathKey] = Path.GetFileName(path);
            }
        }

        return bot;
    }

    private static string Get(BotAction action, string key)
        => action.Config.TryGetValue(key, out var v) ? ConfigValues.AsString(v) : string.Empty;
}
```

- [ ] **Step 4: Run, verify pass**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~TemplateEmbedderTests"`
Expected: PASS (5).

- [ ] **Step 5: Commit**

```bash
git add BotBuilder.Core/TemplateEmbedder.cs BotBuilder.Core.Tests/TemplateEmbedderTests.cs
git commit -m "Add TemplateEmbedder: embed templates as base64, strip path to basename on save"
```

---

## Task 5: Wire embedding into load/save + expose embedded bytes to the field VM

**Files:**
- Modify: `BotBuilder.Core/BotEditorViewModel.cs` (Open, Save, ExportTo)
- Modify: `BotBuilder.Core/Properties/ConfigFieldViewModel.cs`
- Test: `BotBuilder.Core.Tests/TemplateEmbedRoundTripTests.cs`

- [ ] **Step 1: Write the failing round-trip test**

Create `BotBuilder.Core.Tests/TemplateEmbedRoundTripTests.cs`. It builds a `.bot` on disk that references a real temp PNG by path, opens it through a `BotEditorViewModel`, saves to a new path, and asserts the saved JSON embedded the image and stripped the path to a basename. Construct the `BotEditorViewModel` and `ActionRegistry` the same way the existing editor tests in `BotBuilder.Core.Tests` do (reuse their helper/registry setup):

```csharp
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using AdbCore.Actions.BuiltIn;
using AdbCore.Models;
using AdbCore.Serialization;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class TemplateEmbedRoundTripTests
{
    private static string WriteTempPng()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tmpl_{Guid.NewGuid():N}.png");
        using var bmp = new Bitmap(4, 4);
        bmp.Save(path, ImageFormat.Png);
        return path;
    }

    [Fact]
    public void OpenPathBasedBot_ThenSave_EmbedsImageAndStripsPathToBasename()
    {
        var png = WriteTempPng();
        var srcBotPath = Path.Combine(Path.GetTempPath(), $"src_{Guid.NewGuid():N}.bot");
        var outBotPath = Path.Combine(Path.GetTempPath(), $"out_{Guid.NewGuid():N}.bot");
        try
        {
            var bot = new Bot { Name = "T" };
            bot.Actions.Add(new BotAction
            {
                Id = Guid.NewGuid(),
                TypeKey = "screen.findImage",
                Config = { [TemplateMatchCore.TemplatePathKey] = png },
            });
            new BotSerializer().Save(bot, srcBotPath);

            // Editor construction mirrors the existing editor tests (see BotEditorViewModelSaveTests.cs).
            var defs = new AdbCore.Actions.ActionRegistry();
            AdbCore.Actions.BuiltIn.BuiltInActions.Register(defs, new AdbCore.Execution.ActionExecutorRegistry());
            var editor = new BotEditorViewModel(defs);
            editor.Open(srcBotPath);
            editor.Save(outBotPath);

            var reloaded = new BotSerializer().Load(outBotPath);
            var cfg = reloaded.Actions[0].Config;
            Assert.True(cfg.ContainsKey(TemplateMatchCore.TemplateImageKey));
            Assert.Equal(Path.GetFileName(png),
                AdbCore.Actions.ConfigValues.AsString(cfg[TemplateMatchCore.TemplatePathKey]));
        }
        finally
        {
            foreach (var p in new[] { png, srcBotPath, outBotPath }) { try { File.Delete(p); } catch { } }
        }
    }
}
```

If no shared `TestEditor` helper exists, add a tiny one in the test project that constructs `new BotEditorViewModel(BuiltInActions.BuildRegistry(...))` mirroring the existing editor-test setup (copy the construction from the nearest existing `BotEditorViewModel` test). Do NOT invent a new registry path — reuse what those tests already call.

- [ ] **Step 2: Run, verify failure**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~TemplateEmbedRoundTripTests"`
Expected: FAIL — save does not embed yet (no `templateImage` in the reloaded config).

- [ ] **Step 3: Wire `BotEditorViewModel`**

In `BotBuilder.Core/BotEditorViewModel.cs`:

`Open` (currently lines 428-436) — embed before populate:
```csharp
    public void Open(string path)
    {
        var bot = _serializer.Load(path);
        TemplateEmbedder.Embed(bot, TemplateEmbedder.ReadFileIfExists);
        DocumentMapper.Populate(this, bot, _registry);
        _undo.Clear();
        FilePath = path;
        IsDirty = false;
        RaiseUndoState();
    }
```

Add a private helper near `ExportTo`:
```csharp
    // The save-bound bot copy with templates embedded + source paths stripped to basenames.
    private Bot ToBotForSave()
        => TemplateEmbedder.PrepareForSave(DocumentMapper.ToBot(this), TemplateEmbedder.ReadFileIfExists);
```

`Save(path)` (line 442) — change `_serializer.Save(DocumentMapper.ToBot(this), path);` to `_serializer.Save(ToBotForSave(), path);`.

`ExportTo(path)` (line 463) — change `=> _serializer.Save(DocumentMapper.ToBot(this), path);` to `=> _serializer.Save(ToBotForSave(), path);`.

Add `using AdbCore.Models;` if not already present (it is used by `New`/`DocumentMapper`; confirm the file compiles).

- [ ] **Step 4: Expose embedded bytes + clear-on-repath in `ConfigFieldViewModel`**

In `BotBuilder.Core/Properties/ConfigFieldViewModel.cs`, add `using AdbCore.Actions.BuiltIn;` at the top. Replace the `Value` setter and add the accessor:

```csharp
    public object? Value
    {
        get => Normalize(_node.Config.TryGetValue(Field.Key, out var v) ? v : Field.DefaultValue);
        set
        {
            _node.Config[Field.Key] = Coerce(value);
            if (Type == ConfigFieldType.ImagePath)
            {
                // A newly chosen source path supersedes any embedded image; it re-embeds on the next save.
                _node.Config.Remove(TemplateMatchCore.TemplateImageKey);
                OnPropertyChanged(nameof(EmbeddedImageBase64));
            }
            OnPropertyChanged();
            _onChanged();
        }
    }

    /// <summary>For an ImagePath field, the companion embedded base64 image (templateImage), or null —
    /// lets the preview render an embedded template even when the original source file is gone.</summary>
    public string? EmbeddedImageBase64
        => _node.Config.TryGetValue(TemplateMatchCore.TemplateImageKey, out var v)
           && AdbCore.Actions.ConfigValues.AsString(v) is { Length: > 0 } s ? s : null;
```

- [ ] **Step 5: Run the round-trip + full BotBuilder.Core suite**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~BotBuilder.Core.Tests"`
Expected: PASS, including `TemplateEmbedRoundTripTests` and `TemplateEmbedderTests`.

- [ ] **Step 6: Commit**

```bash
git add BotBuilder.Core/BotEditorViewModel.cs BotBuilder.Core/Properties/ConfigFieldViewModel.cs BotBuilder.Core.Tests/TemplateEmbedRoundTripTests.cs
git commit -m "Embed templates on load (in-memory) and save; expose embedded bytes to the field VM"
```

---

## Task 6: Preview from embedded bytes (WPF)

**Files:**
- Create: `BotBuilder/TemplateImageConverter.cs`
- Modify: `BotBuilder/MainWindow.xaml` (resource + ImagePath preview)

No unit test (WPF converter + XAML); verified by build + the user's manual check.

- [ ] **Step 1: Create `BotBuilder/TemplateImageConverter.cs`**

```csharp
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace BotBuilder;

/// <summary>Produces the template preview image, preferring embedded base64 bytes (values[0]) over the source
/// file path (values[1]); returns null when neither yields a decodable image. Cached on load so files aren't
/// locked.</summary>
public sealed class TemplateImageConverter : IMultiValueConverter
{
    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var base64 = values is { Length: > 0 } ? values[0] as string : null;
        if (!string.IsNullOrWhiteSpace(base64))
        {
            try { return Decode(System.Convert.FromBase64String(base64)); }
            catch { /* fall through to the path */ }
        }

        var path = values is { Length: > 1 } ? values[1] as string : null;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try { return Decode(File.ReadAllBytes(path)); }
            catch { return null; }
        }

        return null;
    }

    private static BitmapImage Decode(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

- [ ] **Step 2: Register the converter + switch the preview to a MultiBinding**

In `BotBuilder/MainWindow.xaml`, in `<Window.Resources>` (near line 12), add:
```xml
        <local:TemplateImageConverter x:Key="TemplateImage" />
```

Replace the `FieldImagePath` preview `<Image .../>` (lines 91-92) with:
```xml
                    <Image MaxHeight="120" MaxWidth="220" Stretch="Uniform" Margin="2">
                        <Image.Source>
                            <MultiBinding Converter="{StaticResource TemplateImage}">
                                <Binding Path="EmbeddedImageBase64" />
                                <Binding Path="Value" />
                            </MultiBinding>
                        </Image.Source>
                    </Image>
```

(The `PathToImageConverter`/`PathToImage` resource is now unused by this template; leave the class file in place — removing it is out of scope.)

- [ ] **Step 3: Build the solution**

Run: `dotnet build ADB.slnx`
Expected: Build succeeded, 0 errors, 0 warnings.

- [ ] **Step 4: Commit**

```bash
git add BotBuilder/TemplateImageConverter.cs BotBuilder/MainWindow.xaml
git commit -m "Preview image templates from embedded bytes (fallback to source path)"
```

---

## Task 7: Full verification + PR

**Files:** none (verification only)

- [ ] **Step 1: Clean build + full test run**

Run: `dotnet build ADB.slnx` then `dotnet test ADB.slnx`
Expected: Build succeeded; all tests pass. Capture counts for the PR body.

- [ ] **Step 2: Grep for leftover path-only assumptions**

Run: `git grep -n "templatePath, confidence" -- AdbCore`
Expected: no matches (all `CaptureAndMatch`/`MatchInRegion` calls dropped the path argument).

- [ ] **Step 3: Manual-verification checklist for the PR body (the user runs it)**

- In BotBuilder, add a Find Image action, **Capture** a template, **Save** the `.bot`. Reopen — preview shows.
- Open the saved `.bot` in a text editor — confirm a `templateImage` base64 string is present and `templatePath` is just the filename.
- **Move or delete** the original PNG, reopen the `.bot` — the preview still renders, and a **Test Run** still matches (resolves from embedded bytes).
- Open an **older** path-based `.bot` (PNG present), Save — confirm it is now embedded.
- Re-Capture over an existing template — confirm the preview updates to the new image (stale embed cleared) and persists on save.

- [ ] **Step 4: Push + open the PR (parked for user verify + merge)**

```bash
git push -u origin feat/embed-templates
gh pr create --base main --title "Embed image templates inside the .bot file" --body "<summary + manual checklist + test counts>"
```

---

## Self-Review Notes

- **Spec coverage:** data model (`TemplateImageKey`) → Task 2; matcher bytes → Task 1; resolver embedded-vs-path → Task 2; actions → Task 3; BotCapture fake compile-fix → Task 1; `TemplateEmbedder` (embed/strip) → Task 4; load/save/export wiring + field-VM bytes + clear-on-repath → Task 5; preview-from-bytes → Task 6; back-compat (upgrade-on-load, embed-on-save, basename) → Tasks 4+5; tests → Tasks 1,2,3,4,5.
- **Type consistency:** `TemplateMatchCore.TemplateImageKey` / `HasTemplate` / `MatchInRegion(haystack, config, matcher, confidence)`; `ITemplateMatcher.Match(Bitmap, byte[], double)`; `CaptureAndMatch(context, hwnd|device, matcher, confidence)`; `TemplateEmbedder.Embed/PrepareForSave/ReadFileIfExists(Func<string?,byte[]?>)`; `ConfigFieldViewModel.EmbeddedImageBase64` — consistent across tasks.
- **No placeholders:** the Task 5 round-trip test pins the exact editor construction (`new ActionRegistry()` → `BuiltInActions.Register(defs, new ActionExecutorRegistry())` → `new BotEditorViewModel(defs)`), matching `BotEditorViewModelSaveTests.cs`.
