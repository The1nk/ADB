# M6b — BotCapture Region Select + Preview/Confirm + Save Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend BotCapture so that after capturing a window (M6a), the user can drag-select a region, preview/confirm it with a live OpenCV Test Match and confidence slider, and save it as `<name>.png` + `<name>.png.meta.json`.

**Architecture:** Add testable Core services (`ConfidenceSidecar`, `CaptureSaver`) and view-models (`RegionSelectionViewModel`, `PreviewConfirmViewModel`) plus small hand-off additions to the existing `WindowPickerViewModel`. The WPF shell (`MainWindow`) orchestrates a linear picker→region→confirm flow by swapping `UserControl`s in a `ContentControl` (house style — matches BotBuilder's code-behind orchestration). Test Match re-captures the source window (`IWindowCapture`) and runs the existing `ITemplateMatcher` against a temp PNG of the crop, requesting the *best* match (minConfidence `-1.0`) so the score is shown even below threshold.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), WPF, CommunityToolkit.Mvvm 8.4.2, System.Drawing.Common, System.Text.Json, xUnit. Reuses AdbCore `IWindowCapture`/`ITemplateMatcher`/`MatchResult`/`ScreenCaptureMethod`. Per `Docs/Specs/2026-06-03-m6-botcapture-design.md` §5.

**Scope notes (deliberate decisions):**
- Test Match shows a **color + score + location indicator** ("🟢 Match — 0.93 @ (x,y)" / "🔴 No match — best 0.61, threshold 0.90"). The full green-box-overlay-on-screenshot from V1 §7.2 is deferred as polish (can land in M6c) — the indicator already serves threshold-tuning against real content.
- Navigation uses `MainWindow` code-behind (not a new shell view-model). M6c will build the standalone session shell on top.
- `CaptureSaver` tests use a real temp directory (no `IFileSystem` abstraction) — simpler and equally deterministic.

---

## File Structure

**BotCapture.Core (new):**
- `ConfidenceSidecar.cs` — read/write `<image>.png.meta.json` (`{ "confidence": <double> }`); read tolerant of missing/corrupt.
- `CaptureSaver.cs` — owns a save folder; `NextFileName()` auto-increment + `Save(Bitmap, fileName, confidence)` writes PNG + sidecar.
- `RegionSelectionViewModel.cs` — source bitmap + selection rectangle (image pixels) → `Crop()`; static `ClampSelection`.
- `TestMatchOutcome.cs` — result record for a Test Match run.
- `PreviewConfirmViewModel.cs` — crop, filename, confidence (clamped), `TestMatch()`, `Save()`.

**BotCapture.Core (modified):**
- `WindowPickerViewModel.cs` — add `HasCapture` (notified) + `TakeCapturedImage()` ownership hand-off.

**BotCapture (WPF, new):**
- `Views/RegionSelectView.xaml(.cs)` — drag-rect region selection over the captured image.
- `Views/PreviewConfirmView.xaml(.cs)` — 1×/2× preview, filename, confidence slider, Test Match indicator, Save/Retake.

**BotCapture (WPF, modified):**
- `Views/WindowPickerView.xaml(.cs)` — add a "Use This Capture →" button + `CaptureAccepted` event.
- `MainWindow.xaml(.cs)` — `ContentControl` navigation across the three steps.

**Tests (BotCapture.Core.Tests, new):** `ConfidenceSidecarTests.cs`, `CaptureSaverTests.cs`, `RegionSelectionViewModelTests.cs`, `PreviewConfirmViewModelTests.cs`, plus additions to `WindowPickerViewModelTests.cs`. New fakes: `FakeTemplateMatcher` (added to `Fakes.cs`).

---

## Task 1: ConfidenceSidecar

**Files:**
- Create: `BotCapture.Core/ConfidenceSidecar.cs`
- Test: `BotCapture.Core.Tests/ConfidenceSidecarTests.cs`

- [ ] **Step 1: Write the failing tests** — `BotCapture.Core.Tests/ConfidenceSidecarTests.cs`:

```csharp
using System.IO;
using BotCapture.Core;

namespace BotCapture.Core.Tests;

public class ConfidenceSidecarTests
{
    private static string TempImagePath() =>
        Path.Combine(Path.GetTempPath(), $"botcap_{Guid.NewGuid():N}.png");

    [Fact]
    public void Write_ThenRead_RoundTripsConfidence()
    {
        var image = TempImagePath();
        try
        {
            ConfidenceSidecar.Write(image, 0.83);
            Assert.Equal(0.83, ConfidenceSidecar.Read(image, fallback: 0.5), 3);
        }
        finally
        {
            File.Delete(image + ".meta.json");
        }
    }

    [Fact]
    public void Read_MissingSidecar_ReturnsFallback()
    {
        Assert.Equal(0.9, ConfidenceSidecar.Read(TempImagePath(), fallback: 0.9), 3);
    }

    [Fact]
    public void Read_CorruptSidecar_ReturnsFallback()
    {
        var image = TempImagePath();
        File.WriteAllText(image + ".meta.json", "{ not valid json");
        try
        {
            Assert.Equal(0.9, ConfidenceSidecar.Read(image, fallback: 0.9), 3);
        }
        finally
        {
            File.Delete(image + ".meta.json");
        }
    }

    [Fact]
    public void Write_ProducesCamelCaseConfidenceKey()
    {
        var image = TempImagePath();
        try
        {
            ConfidenceSidecar.Write(image, 0.7);
            var json = File.ReadAllText(image + ".meta.json");
            Assert.Contains("\"confidence\"", json);
        }
        finally
        {
            File.Delete(image + ".meta.json");
        }
    }
}
```

- [ ] **Step 2: Run to verify they fail** — `dotnet test BotCapture.Core.Tests/BotCapture.Core.Tests.csproj --filter "FullyQualifiedName~ConfidenceSidecarTests"` → FAIL (type missing).

- [ ] **Step 3: Implement** — `BotCapture.Core/ConfidenceSidecar.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BotCapture.Core;

/// <summary>Reads/writes the <c>&lt;image&gt;.png.meta.json</c> sidecar that stores a template's chosen
/// confidence threshold. Writes are best-effort; reads never throw — a missing or corrupt sidecar
/// yields the supplied fallback.</summary>
public static class ConfidenceSidecar
{
    private const string Suffix = ".meta.json";

    private sealed record Meta([property: JsonPropertyName("confidence")] double Confidence);

    /// <summary>The sidecar path for an image (e.g. <c>attack-btn.png</c> -> <c>attack-btn.png.meta.json</c>).</summary>
    public static string PathFor(string imagePath) => imagePath + Suffix;

    public static void Write(string imagePath, double confidence)
    {
        var json = JsonSerializer.Serialize(new Meta(confidence));
        File.WriteAllText(PathFor(imagePath), json);
    }

    /// <summary>Reads the sidecar confidence, or returns <paramref name="fallback"/> if the sidecar is
    /// absent or unreadable.</summary>
    public static double Read(string imagePath, double fallback)
    {
        try
        {
            var path = PathFor(imagePath);
            if (!File.Exists(path))
            {
                return fallback;
            }

            var meta = JsonSerializer.Deserialize<Meta>(File.ReadAllText(path));
            return meta?.Confidence ?? fallback;
        }
        catch
        {
            return fallback; // missing/corrupt/locked sidecar -> caller's default
        }
    }
}
```

- [ ] **Step 4: Run to verify they pass** — same filter → PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add BotCapture.Core/ConfidenceSidecar.cs BotCapture.Core.Tests/ConfidenceSidecarTests.cs
git commit -m "feat(capture): add ConfidenceSidecar (.meta.json read/write, tolerant read)"
```

---

## Task 2: CaptureSaver

**Files:**
- Create: `BotCapture.Core/CaptureSaver.cs`
- Test: `BotCapture.Core.Tests/CaptureSaverTests.cs`

- [ ] **Step 1: Write the failing tests** — `BotCapture.Core.Tests/CaptureSaverTests.cs`:

```csharp
using System.Drawing;
using System.IO;
using BotCapture.Core;

namespace BotCapture.Core.Tests;

public class CaptureSaverTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"botcap_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void NextFileName_EmptyFolder_IsCaptureOne()
    {
        var dir = NewTempDir();
        try
        {
            Assert.Equal("capture_001.png", new CaptureSaver(dir).NextFileName());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void NextFileName_SkipsExisting_PicksLowestFree()
    {
        var dir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "capture_001.png"), "x");
            File.WriteAllText(Path.Combine(dir, "capture_003.png"), "x");
            Assert.Equal("capture_002.png", new CaptureSaver(dir).NextFileName());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Save_WritesPngAndSidecar()
    {
        var dir = NewTempDir();
        try
        {
            var saver = new CaptureSaver(dir);
            using var crop = new Bitmap(10, 6);

            saver.Save(crop, "attack-btn.png", 0.88);

            var png = Path.Combine(dir, "attack-btn.png");
            Assert.True(File.Exists(png));
            Assert.True(File.Exists(png + ".meta.json"));
            Assert.Equal(0.88, ConfidenceSidecar.Read(png, fallback: 0.0), 3);
            using var reread = new Bitmap(png);
            Assert.Equal(10, reread.Width);
        }
        finally { Directory.Delete(dir, true); }
    }
}
```

- [ ] **Step 2: Run to verify they fail** — filter `~CaptureSaverTests` → FAIL.

- [ ] **Step 3: Implement** — `BotCapture.Core/CaptureSaver.cs`:

```csharp
using System.Drawing;
using System.Drawing.Imaging;

namespace BotCapture.Core;

/// <summary>Saves cropped templates into a folder: generates the next free <c>capture_NNN.png</c> name
/// and writes the PNG alongside its confidence sidecar.</summary>
public sealed class CaptureSaver
{
    private readonly string _folder;

    public CaptureSaver(string folder)
    {
        _folder = folder;
    }

    /// <summary>The lowest <c>capture_NNN.png</c> (N from 1) not already present in the folder.</summary>
    public string NextFileName()
    {
        for (var i = 1; ; i++)
        {
            var name = $"capture_{i:000}.png";
            if (!File.Exists(Path.Combine(_folder, name)))
            {
                return name;
            }
        }
    }

    /// <summary>Writes <paramref name="crop"/> as a PNG named <paramref name="fileName"/> in the folder,
    /// plus a confidence sidecar next to it.</summary>
    public void Save(Bitmap crop, string fileName, double confidence)
    {
        var path = Path.Combine(_folder, fileName);
        crop.Save(path, ImageFormat.Png);
        ConfidenceSidecar.Write(path, confidence);
    }
}
```

- [ ] **Step 4: Run to verify they pass** — filter `~CaptureSaverTests` → PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add BotCapture.Core/CaptureSaver.cs BotCapture.Core.Tests/CaptureSaverTests.cs
git commit -m "feat(capture): add CaptureSaver (capture_NNN auto-increment + PNG/sidecar write)"
```

---

## Task 3: RegionSelectionViewModel

**Files:**
- Create: `BotCapture.Core/RegionSelectionViewModel.cs`
- Test: `BotCapture.Core.Tests/RegionSelectionViewModelTests.cs`

- [ ] **Step 1: Write the failing tests** — `BotCapture.Core.Tests/RegionSelectionViewModelTests.cs`:

```csharp
using System.Drawing;
using BotCapture.Core;

namespace BotCapture.Core.Tests;

public class RegionSelectionViewModelTests
{
    [Theory]
    // in-bounds rect passes through
    [InlineData(10, 10, 20, 15, 10, 10, 20, 15)]
    // negative width/height (drag up-left) normalizes
    [InlineData(30, 25, -20, -15, 10, 10, 20, 15)]
    // overflow clamps to image bounds (image is 100x80)
    [InlineData(90, 70, 50, 50, 90, 70, 10, 10)]
    public void ClampSelection_NormalizesAndClamps(
        int x, int y, int w, int h, int ex, int ey, int ew, int eh)
    {
        var clamped = RegionSelectionViewModel.ClampSelection(new Rectangle(x, y, w, h), 100, 80);
        Assert.Equal(new Rectangle(ex, ey, ew, eh), clamped);
    }

    [Fact]
    public void ClampSelection_ZeroSize_BecomesOnePixel()
    {
        var clamped = RegionSelectionViewModel.ClampSelection(new Rectangle(5, 5, 0, 0), 100, 80);
        Assert.Equal(1, clamped.Width);
        Assert.Equal(1, clamped.Height);
    }

    [Fact]
    public void Crop_ReturnsBitmapOfClampedSelectionSize()
    {
        using var source = new Bitmap(100, 80);
        var vm = new RegionSelectionViewModel(source);
        vm.Selection = new Rectangle(10, 10, 20, 15);

        using var crop = vm.Crop();

        Assert.Equal(20, crop.Width);
        Assert.Equal(15, crop.Height);
    }
}
```

- [ ] **Step 2: Run to verify they fail** — filter `~RegionSelectionViewModelTests` → FAIL.

- [ ] **Step 3: Implement** — `BotCapture.Core/RegionSelectionViewModel.cs`:

```csharp
using System.Drawing;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotCapture.Core;

/// <summary>Holds the captured source image and the user's current selection rectangle (in source-image
/// pixels), and crops the selected region. The view maps drag coordinates into source pixels and assigns
/// <see cref="Selection"/>. Owns <see cref="Source"/>; <see cref="Crop"/> returns a new caller-owned bitmap.</summary>
public partial class RegionSelectionViewModel : ObservableObject, IDisposable
{
    public RegionSelectionViewModel(Bitmap source)
    {
        Source = source;
    }

    /// <summary>The full window capture being cropped from.</summary>
    public Bitmap Source { get; }

    /// <summary>Current selection in source-image pixels (may be un-normalized / out of bounds while
    /// dragging; <see cref="Crop"/> clamps it).</summary>
    [ObservableProperty] private Rectangle _selection;

    /// <summary>Normalizes a (possibly negative or overflowing) selection into an in-bounds pixel rect of
    /// at least 1×1.</summary>
    public static Rectangle ClampSelection(Rectangle sel, int width, int height)
    {
        var left = Math.Min(sel.Left, sel.Right);
        var top = Math.Min(sel.Top, sel.Bottom);
        var right = Math.Max(sel.Left, sel.Right);
        var bottom = Math.Max(sel.Top, sel.Bottom);

        left = Math.Clamp(left, 0, width - 1);
        top = Math.Clamp(top, 0, height - 1);
        right = Math.Clamp(right, 0, width);
        bottom = Math.Clamp(bottom, 0, height);

        var w = Math.Max(1, right - left);
        var h = Math.Max(1, bottom - top);
        return new Rectangle(left, top, w, h);
    }

    /// <summary>Crops the clamped <see cref="Selection"/> out of <see cref="Source"/> into a new bitmap.</summary>
    public Bitmap Crop()
    {
        var rect = ClampSelection(Selection, Source.Width, Source.Height);
        return Source.Clone(rect, Source.PixelFormat);
    }

    public void Dispose() => Source.Dispose();
}
```

- [ ] **Step 4: Run to verify they pass** — filter `~RegionSelectionViewModelTests` → PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add BotCapture.Core/RegionSelectionViewModel.cs BotCapture.Core.Tests/RegionSelectionViewModelTests.cs
git commit -m "feat(capture): add RegionSelectionViewModel (clamp/normalize selection + crop)"
```

---

## Task 4: TestMatchOutcome + PreviewConfirmViewModel

**Files:**
- Create: `BotCapture.Core/TestMatchOutcome.cs`, `BotCapture.Core/PreviewConfirmViewModel.cs`
- Modify: `BotCapture.Core.Tests/Fakes.cs` (add `FakeTemplateMatcher`)
- Test: `BotCapture.Core.Tests/PreviewConfirmViewModelTests.cs`

- [ ] **Step 1: Add `FakeTemplateMatcher` to `BotCapture.Core.Tests/Fakes.cs`** (append to the existing file, keeping `FakeWindowEnumerator`/`FakeWindowCapture`):

```csharp
internal sealed class FakeTemplateMatcher : AdbCore.Screen.ITemplateMatcher
{
    public AdbCore.Screen.MatchResult? Next;
    public Exception? Throw;
    public string? LastTemplatePath;
    public double LastMinConfidence;

    public AdbCore.Screen.MatchResult? Match(System.Drawing.Bitmap haystack, string templatePath, double minConfidence)
    {
        LastTemplatePath = templatePath;
        LastMinConfidence = minConfidence;
        if (Throw is not null) throw Throw;
        return Next;
    }
}
```

- [ ] **Step 2: Write the failing tests** — `BotCapture.Core.Tests/PreviewConfirmViewModelTests.cs`:

```csharp
using System.Drawing;
using System.IO;
using AdbCore.Screen;
using BotCapture.Core;

namespace BotCapture.Core.Tests;

public class PreviewConfirmViewModelTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"botcap_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static PreviewConfirmViewModel Make(
        string dir, FakeWindowCapture capture, FakeTemplateMatcher matcher, out Bitmap crop)
    {
        crop = new Bitmap(12, 8);
        return new PreviewConfirmViewModel(crop, (IntPtr)5, capture, matcher, new CaptureSaver(dir));
    }

    [Fact]
    public void FileName_SeededFromSaverNextName()
    {
        var dir = NewTempDir();
        try
        {
            var vm = Make(dir, new FakeWindowCapture(), new FakeTemplateMatcher(), out var crop);
            using (crop)
            {
                Assert.Equal("capture_001.png", vm.FileName);
            }
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData(1.5, 1.0)]
    [InlineData(-0.2, 0.0)]
    [InlineData(0.7, 0.7)]
    public void Confidence_ClampsToUnitRange(double set, double expected)
    {
        var dir = NewTempDir();
        try
        {
            var vm = Make(dir, new FakeWindowCapture(), new FakeTemplateMatcher(), out var crop);
            using (crop)
            {
                vm.Confidence = set;
                Assert.Equal(expected, vm.Confidence, 3);
            }
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void TestMatch_ScoreAtOrAboveConfidence_IsMatched()
    {
        var dir = NewTempDir();
        try
        {
            var matcher = new FakeTemplateMatcher { Next = new MatchResult(3, 4, 12, 8, 0.95) };
            var vm = Make(dir, new FakeWindowCapture(), matcher, out var crop);
            using (crop)
            {
                vm.Confidence = 0.90;

                vm.TestMatch();

                Assert.NotNull(vm.LastOutcome);
                Assert.True(vm.LastOutcome!.Matched);
                Assert.Equal(0.95, vm.LastOutcome.Score!.Value, 3);
                Assert.Equal(-1.0, matcher.LastMinConfidence, 3); // asks for the best match regardless of threshold
            }
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void TestMatch_BestScoreBelowConfidence_IsNotMatched_ButReportsScore()
    {
        var dir = NewTempDir();
        try
        {
            var matcher = new FakeTemplateMatcher { Next = new MatchResult(0, 0, 12, 8, 0.61) };
            var vm = Make(dir, new FakeWindowCapture(), matcher, out var crop);
            using (crop)
            {
                vm.Confidence = 0.90;

                vm.TestMatch();

                Assert.False(vm.LastOutcome!.Matched);
                Assert.Equal(0.61, vm.LastOutcome.Score!.Value, 3);
            }
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void TestMatch_MatcherThrows_SetsErrorOutcome_NoException()
    {
        var dir = NewTempDir();
        try
        {
            var matcher = new FakeTemplateMatcher { Throw = new InvalidOperationException("bad template") };
            var vm = Make(dir, new FakeWindowCapture(), matcher, out var crop);
            using (crop)
            {
                vm.TestMatch();

                Assert.NotNull(vm.LastOutcome);
                Assert.False(vm.LastOutcome!.Matched);
                Assert.False(string.IsNullOrEmpty(vm.LastOutcome.Error));
            }
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Save_WritesPngAndSidecarAtChosenNameAndConfidence()
    {
        var dir = NewTempDir();
        try
        {
            var vm = Make(dir, new FakeWindowCapture(), new FakeTemplateMatcher(), out var crop);
            using (crop)
            {
                vm.FileName = "btn.png";
                vm.Confidence = 0.77;

                vm.Save();

                var png = Path.Combine(dir, "btn.png");
                Assert.True(File.Exists(png));
                Assert.Equal(0.77, ConfidenceSidecar.Read(png, 0.0), 3);
            }
        }
        finally { Directory.Delete(dir, true); }
    }
}
```

- [ ] **Step 3: Run to verify they fail** — filter `~PreviewConfirmViewModelTests` → FAIL.

- [ ] **Step 4: Implement `TestMatchOutcome`** — `BotCapture.Core/TestMatchOutcome.cs`:

```csharp
using AdbCore.Screen;

namespace BotCapture.Core;

/// <summary>The result of a single Test Match run. <see cref="Matched"/> is whether the best score met the
/// chosen confidence; <see cref="Score"/>/<see cref="Location"/> describe the best match found (null when
/// the matcher couldn't run); <see cref="Error"/> is set instead when Test Match failed.</summary>
public sealed record TestMatchOutcome(bool Matched, double? Score, MatchResult? Location, string? Error);
```

- [ ] **Step 5: Implement `PreviewConfirmViewModel`** — `BotCapture.Core/PreviewConfirmViewModel.cs`:

```csharp
using System.Drawing;
using System.Drawing.Imaging;
using AdbCore.Screen;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotCapture.Core;

/// <summary>Drives the preview/confirm step: holds the cropped template, the chosen filename and
/// confidence, runs a live Test Match against a fresh capture of the source window, and saves the
/// template (PNG + confidence sidecar). Owns <see cref="Crop"/>.</summary>
public partial class PreviewConfirmViewModel : ObservableObject, IDisposable
{
    // Template-match score is CCOEFF_NORMED in [-1, 1]; asking for -1.0 always returns the best match,
    // so the score is shown even when it's below the user's threshold.
    private const double BestMatchFloor = -1.0;

    private readonly IntPtr _sourceHandle;
    private readonly IWindowCapture _capture;
    private readonly ITemplateMatcher _matcher;
    private readonly CaptureSaver _saver;

    public PreviewConfirmViewModel(
        Bitmap crop, IntPtr sourceHandle, IWindowCapture capture, ITemplateMatcher matcher, CaptureSaver saver)
    {
        Crop = crop;
        _sourceHandle = sourceHandle;
        _capture = capture;
        _matcher = matcher;
        _saver = saver;
        _fileName = saver.NextFileName();
    }

    /// <summary>The cropped template image to be saved.</summary>
    public Bitmap Crop { get; }

    [ObservableProperty] private TestMatchOutcome? _lastOutcome;

    private string _fileName;
    private double _confidence = 0.9;

    /// <summary>The chosen output filename (defaults to the saver's next free name).</summary>
    public string FileName
    {
        get => _fileName;
        set => SetProperty(ref _fileName, value);
    }

    /// <summary>Match threshold in [0, 1] (default 0.9). Out-of-range assignments clamp.</summary>
    public double Confidence
    {
        get => _confidence;
        set => SetProperty(ref _confidence, Math.Clamp(value, 0.0, 1.0));
    }

    /// <summary>Re-captures the source window and matches the crop against it, recording the best score and
    /// whether it met <see cref="Confidence"/> into <see cref="LastOutcome"/>. Never throws.</summary>
    public void TestMatch()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"botcap_test_{Guid.NewGuid():N}.png");
        try
        {
            Crop.Save(tempPath, ImageFormat.Png);
            using var fresh = _capture.Capture(_sourceHandle, ScreenCaptureMethod.Auto);
            var best = _matcher.Match(fresh, tempPath, BestMatchFloor);
            LastOutcome = best is MatchResult m
                ? new TestMatchOutcome(m.Score >= Confidence, m.Score, m, Error: null)
                : new TestMatchOutcome(Matched: false, Score: null, Location: null, Error: "No match could be computed.");
        }
        catch (Exception ex)
        {
            LastOutcome = new TestMatchOutcome(Matched: false, Score: null, Location: null, Error: ex.Message);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { /* temp cleanup is best-effort */ }
        }
    }

    /// <summary>Writes the template (PNG + confidence sidecar) under the chosen filename.</summary>
    public void Save() => _saver.Save(Crop, FileName, Confidence);

    public void Dispose() => Crop.Dispose();
}
```

- [ ] **Step 6: Run to verify they pass** — filter `~PreviewConfirmViewModelTests` → PASS (8 tests).

- [ ] **Step 7: Commit**

```bash
git add BotCapture.Core/TestMatchOutcome.cs BotCapture.Core/PreviewConfirmViewModel.cs BotCapture.Core.Tests/Fakes.cs BotCapture.Core.Tests/PreviewConfirmViewModelTests.cs
git commit -m "feat(capture): add PreviewConfirmViewModel + TestMatchOutcome (live test-match + save)"
```

---

## Task 5: WindowPickerViewModel capture hand-off

Adds the seam M6b needs to advance from the picker: a notified `HasCapture` flag (to enable a "Use This" button) and an ownership-transferring `TakeCapturedImage()`.

**Files:**
- Modify: `BotCapture.Core/WindowPickerViewModel.cs`
- Test: `BotCapture.Core.Tests/WindowPickerViewModelTests.cs` (add cases)

- [ ] **Step 1: Add the failing tests** to `WindowPickerViewModelTests.cs` (new methods in the existing class):

```csharp
    [Fact]
    public void HasCapture_FalseUntilCapture_TrueAfter()
    {
        var vm = Make(out _, out _);
        Assert.False(vm.HasCapture);

        vm.SelectedWindow = new WindowRow(new WindowInfo((IntPtr)1, "A", "a"), null);
        vm.CaptureSelected();

        Assert.True(vm.HasCapture);
    }

    [Fact]
    public void TakeCapturedImage_TransfersOwnership_ClearsWithoutDisposing()
    {
        var vm = Make(out _, out _);
        vm.SelectedWindow = new WindowRow(new WindowInfo((IntPtr)1, "A", "a"), null);
        vm.CaptureSelected();

        var taken = vm.TakeCapturedImage();

        Assert.NotNull(taken);
        Assert.Null(vm.CapturedImage);
        Assert.False(vm.HasCapture);
        Assert.Equal(8, taken!.Width); // FakeWindowCapture returns an 8x8 bitmap; still usable (not disposed)
        taken.Dispose();
    }

    [Fact]
    public void TakeCapturedImage_NoCapture_ReturnsNull()
    {
        var vm = Make(out _, out _);
        Assert.Null(vm.TakeCapturedImage());
    }
```

- [ ] **Step 2: Run to verify the new tests fail** — filter `~WindowPickerViewModelTests` → the 3 new fail (members missing), existing 6 still pass.

- [ ] **Step 3: Implement** — in `BotCapture.Core/WindowPickerViewModel.cs`, add a `HasCapture` property, notify it whenever `CapturedImage` changes (CommunityToolkit generates the `OnCapturedImageChanged` partial hook), and add `TakeCapturedImage()`.

Add this partial hook + property next to the existing `_capturedImage` field declaration:

```csharp
    /// <summary>Whether a capture is available to advance with.</summary>
    public bool HasCapture => CapturedImage is not null;

    partial void OnCapturedImageChanged(System.Drawing.Bitmap? value) => OnPropertyChanged(nameof(HasCapture));
```

Add this method (e.g. after `CaptureSelected`):

```csharp
    /// <summary>Hands the current capture to the next step, transferring ownership: returns the bitmap and
    /// clears the field WITHOUT disposing it (the caller now owns and disposes it).</summary>
    public System.Drawing.Bitmap? TakeCapturedImage()
    {
        var image = CapturedImage;
        CapturedImage = null; // relinquish without dispose; ownership moves to the caller
        return image;
    }
```

> Note: `Refresh()` already does `CapturedImage?.Dispose(); CapturedImage = null;`. After `TakeCapturedImage()`, `CapturedImage` is null, so a later `Refresh()` disposes nothing — no double-free. Leave `Refresh()` as-is.

- [ ] **Step 4: Run to verify all pass** — filter `~WindowPickerViewModelTests` → PASS (9 tests). Then build to confirm no MVVMTK warning: `dotnet build BotCapture.Core/BotCapture.Core.csproj -c Debug --nologo` → 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add BotCapture.Core/WindowPickerViewModel.cs BotCapture.Core.Tests/WindowPickerViewModelTests.cs
git commit -m "feat(capture): picker capture hand-off (HasCapture + TakeCapturedImage)"
```

---

## Task 6: RegionSelectView (WPF, visual)

Drag a selection rectangle over the captured image; map view coordinates to source pixels; confirm to crop. No unit tests — verified by running the app.

**Files:**
- Create: `BotCapture/Views/RegionSelectView.xaml`, `BotCapture/Views/RegionSelectView.xaml.cs`

- [ ] **Step 1: Create `RegionSelectView.xaml`**

```xml
<UserControl x:Class="BotCapture.Views.RegionSelectView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <DockPanel Margin="8">
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0,0,0,8">
            <TextBlock Text="Drag to select a region, then Confirm." VerticalAlignment="Center" />
            <Button Content="Confirm Region →" Click="OnConfirm" Width="140" Margin="12,0,0,0" />
            <Button Content="← Back" Click="OnBack" Width="80" Margin="8,0,0,0" />
        </StackPanel>
        <Border BorderBrush="LightGray" BorderThickness="1">
            <!-- Image sized to its pixels via a Grid; the overlay Canvas shares the same coordinate space. -->
            <Grid x:Name="ImageHost" HorizontalAlignment="Center" VerticalAlignment="Center">
                <Image x:Name="SourceImage" Stretch="Uniform"
                       MouseLeftButtonDown="OnMouseDown" MouseMove="OnMouseMove" MouseLeftButtonUp="OnMouseUp" />
                <Canvas x:Name="Overlay" Background="Transparent" IsHitTestVisible="False">
                    <Rectangle x:Name="SelectionRect" Stroke="Lime" StrokeThickness="2"
                               Fill="#330000FF" Visibility="Collapsed" />
                </Canvas>
            </Grid>
        </Border>
    </DockPanel>
</UserControl>
```

- [ ] **Step 2: Create `RegionSelectView.xaml.cs`**

The view maps mouse positions (relative to the displayed `SourceImage`) into source-pixel coordinates using the ratio of source pixels to displayed size, assigns `vm.Selection`, and draws the overlay rectangle in display coordinates.

```csharp
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BotCapture.Core;

namespace BotCapture.Views;

public partial class RegionSelectView : UserControl
{
    private System.Windows.Point _dragStart;
    private bool _dragging;

    public RegionSelectView()
    {
        InitializeComponent();
    }

    /// <summary>Raised with the cropped template when the user confirms a region.</summary>
    public event EventHandler<Bitmap>? RegionConfirmed;

    /// <summary>Raised when the user backs out of region selection.</summary>
    public event EventHandler? BackRequested;

    private RegionSelectionViewModel? Vm => DataContext as RegionSelectionViewModel;

    /// <summary>Call after setting DataContext to show the source image.</summary>
    public void Bind(RegionSelectionViewModel vm)
    {
        DataContext = vm;
        SourceImage.Source = BitmapInterop.ToImageSource(vm.Source);
        SelectionRect.Visibility = Visibility.Collapsed;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Vm is null) return;
        _dragging = true;
        _dragStart = e.GetPosition(SourceImage);
        SourceImage.CaptureMouse();
        UpdateSelection(_dragStart, _dragStart);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        UpdateSelection(_dragStart, e.GetPosition(SourceImage));
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        SourceImage.ReleaseMouseCapture();
        UpdateSelection(_dragStart, e.GetPosition(SourceImage));
    }

    private void UpdateSelection(System.Windows.Point a, System.Windows.Point b)
    {
        if (Vm is null) return;

        // Display rectangle (for the overlay), clamped to the image bounds.
        var dispLeft = Math.Max(0, Math.Min(a.X, b.X));
        var dispTop = Math.Max(0, Math.Min(a.Y, b.Y));
        var dispRight = Math.Min(SourceImage.ActualWidth, Math.Max(a.X, b.X));
        var dispBottom = Math.Min(SourceImage.ActualHeight, Math.Max(a.Y, b.Y));

        Canvas.SetLeft(SelectionRect, dispLeft);
        Canvas.SetTop(SelectionRect, dispTop);
        SelectionRect.Width = Math.Max(0, dispRight - dispLeft);
        SelectionRect.Height = Math.Max(0, dispBottom - dispTop);
        SelectionRect.Visibility = Visibility.Visible;

        // Map display coords -> source pixels by the displayed/actual ratio.
        var scaleX = Vm.Source.Width / Math.Max(1.0, SourceImage.ActualWidth);
        var scaleY = Vm.Source.Height / Math.Max(1.0, SourceImage.ActualHeight);
        Vm.Selection = new Rectangle(
            (int)Math.Round(dispLeft * scaleX),
            (int)Math.Round(dispTop * scaleY),
            (int)Math.Round((dispRight - dispLeft) * scaleX),
            (int)Math.Round((dispBottom - dispTop) * scaleY));
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        RegionConfirmed?.Invoke(this, Vm.Crop());
    }

    private void OnBack(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
}
```

- [ ] **Step 3: Build** — `dotnet build ADB.slnx -c Debug --nologo` → 0 warnings, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add BotCapture/Views/RegionSelectView.xaml BotCapture/Views/RegionSelectView.xaml.cs
git commit -m "feat(capture): RegionSelectView (drag-select region over captured image)"
```

---

## Task 7: PreviewConfirmView (WPF, visual)

1× and 2× preview, filename field, confidence slider, Test Match indicator, Save/Retake. No unit tests.

**Files:**
- Create: `BotCapture/Views/PreviewConfirmView.xaml`, `BotCapture/Views/PreviewConfirmView.xaml.cs`

- [ ] **Step 1: Create `PreviewConfirmView.xaml`**

```xml
<UserControl x:Class="BotCapture.Views.PreviewConfirmView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid Margin="12">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>

        <StackPanel Grid.Column="0" Margin="0,0,16,0">
            <TextBlock Text="Preview (1×)" FontWeight="SemiBold" />
            <Border BorderBrush="LightGray" BorderThickness="1" Margin="0,2,0,8">
                <Image x:Name="Preview1x" Stretch="None" />
            </Border>
            <TextBlock Text="Preview (2×)" FontWeight="SemiBold" />
            <Border BorderBrush="LightGray" BorderThickness="1" Margin="0,2,0,0">
                <Image x:Name="Preview2x" Stretch="Fill" />
            </Border>
        </StackPanel>

        <StackPanel Grid.Column="1">
            <TextBlock Text="Filename" FontWeight="SemiBold" />
            <TextBox Text="{Binding FileName, UpdateSourceTrigger=PropertyChanged}" Margin="0,2,0,10" />

            <TextBlock Text="Confidence" FontWeight="SemiBold" />
            <StackPanel Orientation="Horizontal" Margin="0,2,0,10">
                <Slider x:Name="ConfidenceSlider" Minimum="0" Maximum="1" Width="240"
                        Value="{Binding Confidence, Mode=TwoWay}" />
                <TextBlock Text="{Binding Confidence, StringFormat=F2}" Width="40" Margin="8,0,0,0" />
            </StackPanel>

            <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                <Button Content="Test Match" Click="OnTestMatch" Width="110" />
                <TextBlock x:Name="MatchStatus" VerticalAlignment="Center" Margin="12,0,0,0" />
            </StackPanel>

            <StackPanel Orientation="Horizontal" Margin="0,12,0,0">
                <Button Content="Save" Click="OnSave" Width="100" />
                <Button Content="Retake" Click="OnRetake" Width="100" Margin="8,0,0,0" />
            </StackPanel>
            <TextBlock x:Name="SaveStatus" Foreground="Green" Margin="0,8,0,0" />
        </StackPanel>
    </Grid>
</UserControl>
```

- [ ] **Step 2: Create `PreviewConfirmView.xaml.cs`**

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BotCapture.Core;

namespace BotCapture.Views;

public partial class PreviewConfirmView : UserControl
{
    public PreviewConfirmView()
    {
        InitializeComponent();
    }

    /// <summary>Raised after a successful Save, with the saved file name.</summary>
    public event EventHandler<string>? Saved;

    /// <summary>Raised when the user wants to re-select a region.</summary>
    public event EventHandler? RetakeRequested;

    private PreviewConfirmViewModel? Vm => DataContext as PreviewConfirmViewModel;

    /// <summary>Call after constructing the VM to bind it and show the crop previews.</summary>
    public void Bind(PreviewConfirmViewModel vm)
    {
        DataContext = vm;
        var image = BitmapInterop.ToImageSource(vm.Crop);
        Preview1x.Source = image;
        Preview1x.Width = vm.Crop.Width;
        Preview1x.Height = vm.Crop.Height;
        Preview2x.Source = image;
        Preview2x.Width = vm.Crop.Width * 2;
        Preview2x.Height = vm.Crop.Height * 2;
        MatchStatus.Text = string.Empty;
        SaveStatus.Text = string.Empty;
    }

    private void OnTestMatch(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        Vm.TestMatch();
        var o = Vm.LastOutcome;
        if (o is null) return;

        if (o.Error is not null)
        {
            MatchStatus.Foreground = Brushes.DarkRed;
            MatchStatus.Text = $"Test Match failed: {o.Error}";
        }
        else if (o.Matched && o.Location is { } loc)
        {
            MatchStatus.Foreground = Brushes.Green;
            MatchStatus.Text = $"✅ Match — {o.Score:F2} @ ({loc.X},{loc.Y})";
        }
        else
        {
            MatchStatus.Foreground = Brushes.DarkRed;
            MatchStatus.Text = $"🔴 No match — best {o.Score:F2}, threshold {Vm.Confidence:F2}";
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        Vm.Save();
        SaveStatus.Text = $"Saved {Vm.FileName}";
        Saved?.Invoke(this, Vm.FileName);
    }

    private void OnRetake(object sender, RoutedEventArgs e) => RetakeRequested?.Invoke(this, EventArgs.Empty);
}
```

- [ ] **Step 3: Build** — `dotnet build ADB.slnx -c Debug --nologo` → 0 warnings, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add BotCapture/Views/PreviewConfirmView.xaml BotCapture/Views/PreviewConfirmView.xaml.cs
git commit -m "feat(capture): PreviewConfirmView (1x/2x preview, slider, test-match, save)"
```

---

## Task 8: MainWindow navigation + picker "Use This" button (WPF, visual)

Wires the picker → region → confirm flow. No unit tests.

**Files:**
- Modify: `BotCapture/Views/WindowPickerView.xaml`, `BotCapture/Views/WindowPickerView.xaml.cs`, `BotCapture/MainWindow.xaml`, `BotCapture/MainWindow.xaml.cs`

- [ ] **Step 1: Add a "Use This Capture →" button to `WindowPickerView.xaml`** — insert it in the top `StackPanel`, after the "Capture Selected" button:

```xml
            <Button Content="Use This Capture →" Click="OnUseCapture" Width="150" Margin="8,0,0,0"
                    IsEnabled="{Binding HasCapture}" />
```

- [ ] **Step 2: Add the event + handler to `WindowPickerView.xaml.cs`** — add a public event and handler (keep the existing `OnRefresh`/`OnCapture`):

```csharp
    /// <summary>Raised when the user accepts the current capture to proceed to region selection.</summary>
    public event EventHandler? CaptureAccepted;

    private void OnUseCapture(object sender, RoutedEventArgs e)
    {
        if (Vm?.HasCapture == true)
        {
            CaptureAccepted?.Invoke(this, EventArgs.Empty);
        }
    }
```

- [ ] **Step 3: Rewrite `MainWindow.xaml`** to host a swappable content area plus a persistent picker:

```xml
<Window x:Class="BotCapture.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="BotCapture" Height="700" Width="1000">
    <Grid x:Name="Root" />
</Window>
```

- [ ] **Step 4: Rewrite `MainWindow.xaml.cs`** to orchestrate the flow. It owns the picker VM + the dependencies, swaps views into `Root`, and manages bitmap lifetimes across steps.

```csharp
using System.Windows;
using System.Windows.Controls;
using AdbCore.Screen;
using AdbCore.Targets;
using BotCapture.Core;
using BotCapture.Views;

namespace BotCapture;

public partial class MainWindow : Window
{
    private readonly IWindowCapture _capture = new Win32WindowCapture();
    private readonly ITemplateMatcher _matcher = new OpenCvSharpTemplateMatcher();

    private readonly WindowPickerViewModel _pickerVm;
    private readonly WindowPickerView _pickerView;

    private IntPtr _sourceHandle;
    private RegionSelectionViewModel? _regionVm;
    private PreviewConfirmViewModel? _confirmVm;

    public MainWindow()
    {
        InitializeComponent();

        _pickerVm = new WindowPickerViewModel(new Win32WindowEnumerator(), _capture);
        _pickerView = new WindowPickerView { DataContext = _pickerVm };
        _pickerView.CaptureAccepted += OnCaptureAccepted;

        ShowPicker();
        _pickerVm.Refresh();
    }

    private void SetContent(UIElement view)
    {
        Root.Children.Clear();
        Root.Children.Add(view);
    }

    private void ShowPicker()
    {
        SetContent(_pickerView);
    }

    private void OnCaptureAccepted(object? sender, EventArgs e)
    {
        var image = _pickerVm.TakeCapturedImage();
        if (image is null || _pickerVm.SelectedWindow is null)
        {
            return;
        }

        _sourceHandle = _pickerVm.SelectedWindow.Info.Handle;
        ShowRegion(new RegionSelectionViewModel(image));
    }

    private void ShowRegion(RegionSelectionViewModel vm)
    {
        _regionVm?.Dispose();
        _regionVm = vm;

        var view = new RegionSelectView();
        view.RegionConfirmed += OnRegionConfirmed;
        view.BackRequested += (_, _) => { DisposeRegion(); ShowPicker(); };
        view.Bind(vm);
        SetContent(view);
    }

    private void OnRegionConfirmed(object? sender, System.Drawing.Bitmap crop)
    {
        _confirmVm?.Dispose();
        _confirmVm = new PreviewConfirmViewModel(crop, _sourceHandle, _capture, _matcher, new CaptureSaver(SaveFolder()));

        var view = new PreviewConfirmView();
        view.Saved += (_, _) => { DisposeConfirm(); DisposeRegion(); ShowPicker(); };
        view.RetakeRequested += (_, _) => { DisposeConfirm(); if (_regionVm is not null) ReshowRegion(); };
        view.Bind(_confirmVm);
        SetContent(view);
    }

    // Retake: re-open region selection on the same source (the region VM still owns the source bitmap).
    private void ReshowRegion()
    {
        var view = new RegionSelectView();
        view.RegionConfirmed += OnRegionConfirmed;
        view.BackRequested += (_, _) => { DisposeRegion(); ShowPicker(); };
        view.Bind(_regionVm!);
        SetContent(view);
    }

    private static string SaveFolder()
    {
        // M6b standalone default; M6c adds a folder picker. Use a stable, user-writable location.
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "BotCapture");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private void DisposeRegion()
    {
        _regionVm?.Dispose();
        _regionVm = null;
    }

    private void DisposeConfirm()
    {
        _confirmVm?.Dispose();
        _confirmVm = null;
    }
}
```

> Note on Retake lifetimes: `PreviewConfirmViewModel.Crop` is a clone produced by `RegionSelectionViewModel.Crop()`, independent of the region's `Source`. Disposing the confirm VM on Retake frees that clone; the region VM keeps `Source` alive so the user can re-drag. On Save or Back, both are disposed and we return to the picker.

- [ ] **Step 5: Build** — `dotnet build ADB.slnx -c Debug --nologo` → 0 warnings, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add BotCapture/Views/WindowPickerView.xaml BotCapture/Views/WindowPickerView.xaml.cs BotCapture/MainWindow.xaml BotCapture/MainWindow.xaml.cs
git commit -m "feat(capture): wire picker->region->confirm navigation flow"
```

---

## Task 9: Full verification

**Files:** none (verification only).

- [ ] **Step 1: Build the whole solution** — `dotnet build ADB.slnx -c Debug --nologo` → `0 Warning(s) 0 Error(s)`.

- [ ] **Step 2: Run the whole test suite** — `dotnet test ADB.slnx -c Debug --nologo --no-build`. Expect all projects PASS. New BotCapture.Core tests added this slice: ConfidenceSidecar 4, CaptureSaver 3, RegionSelectionViewModel 5, PreviewConfirmViewModel 8, WindowPickerViewModel +3 (now 12). BotCapture.Core.Tests should total 32 (was 9). AdbCore 226 / BotBuilder.Core 103 / BotRunner 19 unchanged.

- [ ] **Step 3: Manual run (user visual verification)** — `dotnet run --project BotCapture/BotCapture.csproj -c Debug`. Expected flow:
  1. Picker lists windows; select one and **Capture Selected** (preview shows), then **Use This Capture →**.
  2. Region screen shows the capture; drag a rectangle; **Confirm Region →**.
  3. Preview/confirm shows the crop at 1× and 2×; adjust the **Confidence** slider; **Test Match** re-captures the window and shows 🟢/🔴 with the score; **Save** writes `capture_NNN.png` + `.meta.json` to `Pictures\BotCapture`; **Retake** returns to region select; **← Back** returns to the picker.

> Hand off to the user for visual confirmation of the full capture flow before opening the PR.
