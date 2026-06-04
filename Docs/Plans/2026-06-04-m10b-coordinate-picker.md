# M10b — Coordinate Picker Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an in-process BotBuilder "Pick coordinates…" helper that captures a still frame of the selected action's bound target, lets the author click point(s) on it, and writes the clicked X/Y back into the action's coordinate fields — serving Android Tap/Swipe and Windows Click/Right Click/Double Click/Mouse Move.

**Architecture:** Testable, WPF-free, System.Drawing-free **Core** in `BotBuilder.Core/Picker` (coordinate-field metadata, display→source mapping math, and a picker view-model that sequences 1- or 2-point picks). A thin **WPF** layer in `BotBuilder` does the live work: a `FrameCapturer` resolves the target selector to a `Bitmap` (Win32 client capture for Window targets; ADB framebuffer for Android), a `CoordinatePickerDialog` shows the frame and collects clicks, and a `MainWindow` "Pick coordinates…" button wires it to the Properties panel and writes results into the `ConfigFieldViewModel`s. Capture mirrors the runner's `WindowTargetBinder`/`AndroidTargetBinder` resolution.

**Tech Stack:** C# / .NET 10, WPF, CommunityToolkit.Mvvm, xUnit, AdbCore (`Win32WindowResolver`, `Win32WindowCapture`, `AdvancedSharpAdbDevice`, `AdbSelector`), System.Drawing (`Bitmap`).

**Reference spec:** `Docs/Specs/2026-06-04-m10-android-visual-coord-picker-design.md` (§4).

**Merge handling:** M10b has a real WPF dialog with interactive behavior. It is **NOT** self-merged — after the build + unit tests are green, it goes to the user for visual verification (the watch-items in spec §6) and the user merges. Subagents build it to compile-clean + unit-green; the controller hands off at the end.

**Confirmed config keys** (from the action sources): `android.tap` → `x`,`y`; `android.swipe` → `x1`,`y1`,`x2`,`y2`; `input.click`/`input.rightClick`/`input.doubleClick`/`input.mouseMove` (`PointerActionBase`) → `x`,`y`.

---

## File Structure

**Create — BotBuilder.Core (testable, no WPF / no System.Drawing):**
- `BotBuilder.Core/Picker/CoordinatePoint.cs` — record `(XKey, YKey, Label)`.
- `BotBuilder.Core/Picker/CoordinateFieldMap.cs` — TypeKey → point list.
- `BotBuilder.Core/Picker/CoordinateMapping.cs` — display→source-pixel math under `Stretch=Uniform`.
- `BotBuilder.Core/Picker/CoordinatePickerViewModel.cs` — sequences 1/2-point picks, yields write-back pairs.

**Modify — BotBuilder.Core:**
- `BotBuilder.Core/Properties/PropertiesViewModel.cs` — add `SupportsCoordinatePicking`.

**Create — BotBuilder.Core.Tests:**
- `BotBuilder.Core.Tests/Picker/CoordinateFieldMapTests.cs`
- `BotBuilder.Core.Tests/Picker/CoordinateMappingTests.cs`
- `BotBuilder.Core.Tests/Picker/CoordinatePickerViewModelTests.cs`

**Create — BotBuilder (WPF, build-verified + user-verified):**
- `BotBuilder/FrameCapturer.cs` — `Bitmap? TryCapture(BotTargetType, selector, out error)`.
- `BotBuilder/CoordinatePickerDialog.xaml` + `.xaml.cs`.

**Modify — BotBuilder (WPF):**
- `BotBuilder/MainWindow.xaml` — "Pick coordinates…" button in the Properties panel.
- `BotBuilder/MainWindow.xaml.cs` — `PickCoordinates_Click` + target resolution + write-back.

---

## Task 1: Coordinate-field metadata (`CoordinatePoint` + `CoordinateFieldMap`)

**Files:**
- Create: `BotBuilder.Core/Picker/CoordinatePoint.cs`, `BotBuilder.Core/Picker/CoordinateFieldMap.cs`
- Test: `BotBuilder.Core.Tests/Picker/CoordinateFieldMapTests.cs`

- [ ] **Step 1: Write the failing test**

Create `BotBuilder.Core.Tests/Picker/CoordinateFieldMapTests.cs`:

```csharp
using System.Linq;
using BotBuilder.Core.Picker;
using Xunit;

namespace BotBuilder.Core.Tests.Picker;

public class CoordinateFieldMapTests
{
    [Theory]
    [InlineData("android.tap")]
    [InlineData("input.click")]
    [InlineData("input.rightClick")]
    [InlineData("input.doubleClick")]
    [InlineData("input.mouseMove")]
    public void SinglePointActions_HaveOnePoint_XY(string typeKey)
    {
        var points = CoordinateFieldMap.ForTypeKey(typeKey);
        Assert.True(CoordinateFieldMap.Supports(typeKey));
        var p = Assert.Single(points);
        Assert.Equal("x", p.XKey);
        Assert.Equal("y", p.YKey);
    }

    [Fact]
    public void Swipe_HasTwoPoints_StartThenEnd()
    {
        var points = CoordinateFieldMap.ForTypeKey("android.swipe");
        Assert.Equal(2, points.Count);
        Assert.Equal(("x1", "y1", "Start"), (points[0].XKey, points[0].YKey, points[0].Label));
        Assert.Equal(("x2", "y2", "End"), (points[1].XKey, points[1].YKey, points[1].Label));
    }

    [Theory]
    [InlineData("screen.findImage")]
    [InlineData("data.log")]
    [InlineData("android.screenshot")]
    public void NonCoordinateActions_AreUnsupported_AndEmpty(string typeKey)
    {
        Assert.False(CoordinateFieldMap.Supports(typeKey));
        Assert.Empty(CoordinateFieldMap.ForTypeKey(typeKey));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test C:\git\ADB-wt-m10b\BotBuilder.Core.Tests --filter "FullyQualifiedName~CoordinateFieldMapTests"`
Expected: compile FAIL (types missing).

- [ ] **Step 3: Create the types**

Create `BotBuilder.Core/Picker/CoordinatePoint.cs`:

```csharp
namespace BotBuilder.Core.Picker;

/// <summary>One coordinate the picker collects for an action: the config field keys its X and Y are
/// written to, plus a user-facing label (e.g. "Start", "End", "Target").</summary>
public sealed record CoordinatePoint(string XKey, string YKey, string Label);
```

Create `BotBuilder.Core/Picker/CoordinateFieldMap.cs`:

```csharp
namespace BotBuilder.Core.Picker;

/// <summary>Maps an action TypeKey to the coordinate point(s) the picker fills. Actions absent from the
/// map don't support coordinate picking. Keys here mirror each action's config field keys.</summary>
public static class CoordinateFieldMap
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<CoordinatePoint>> Map =
        new Dictionary<string, IReadOnlyList<CoordinatePoint>>
        {
            ["android.tap"] = [new CoordinatePoint("x", "y", "Target")],
            ["android.swipe"] = [new CoordinatePoint("x1", "y1", "Start"), new CoordinatePoint("x2", "y2", "End")],
            ["input.click"] = [new CoordinatePoint("x", "y", "Target")],
            ["input.rightClick"] = [new CoordinatePoint("x", "y", "Target")],
            ["input.doubleClick"] = [new CoordinatePoint("x", "y", "Target")],
            ["input.mouseMove"] = [new CoordinatePoint("x", "y", "Target")],
        };

    public static bool Supports(string typeKey) => Map.ContainsKey(typeKey);

    public static IReadOnlyList<CoordinatePoint> ForTypeKey(string typeKey) =>
        Map.TryGetValue(typeKey, out var points) ? points : [];
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test C:\git\ADB-wt-m10b\BotBuilder.Core.Tests --filter "FullyQualifiedName~CoordinateFieldMapTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git -C C:\git\ADB-wt-m10b add BotBuilder.Core/Picker/CoordinatePoint.cs BotBuilder.Core/Picker/CoordinateFieldMap.cs BotBuilder.Core.Tests/Picker/CoordinateFieldMapTests.cs
git -C C:\git\ADB-wt-m10b commit -m "feat(builder): coordinate-field metadata (CoordinatePoint + CoordinateFieldMap)"
```

---

## Task 2: Display→source mapping math (`CoordinateMapping`)

**Files:**
- Create: `BotBuilder.Core/Picker/CoordinateMapping.cs`
- Test: `BotBuilder.Core.Tests/Picker/CoordinateMappingTests.cs`

- [ ] **Step 1: Write the failing test**

Create `BotBuilder.Core.Tests/Picker/CoordinateMappingTests.cs`:

```csharp
using BotBuilder.Core.Picker;
using Xunit;

namespace BotBuilder.Core.Tests.Picker;

public class CoordinateMappingTests
{
    [Fact]
    public void NoLetterbox_SameAspect_MapsProportionally()
    {
        // 200x100 display showing a 400x200 source (exact 0.5 scale, no letterbox).
        var p = CoordinateMapping.ToSourcePixel(100, 50, 200, 100, 400, 200);
        Assert.Equal((200, 100), p);
    }

    [Fact]
    public void Letterboxed_WiderDisplay_AccountsForHorizontalMargin()
    {
        // Source 100x100 shown in a 300x100 area: uniform scale = 1.0, rendered 100x100 centered → x-offset 100.
        // A click at display (150,50) is the center of the rendered image → source (50,50).
        var p = CoordinateMapping.ToSourcePixel(150, 50, 300, 100, 100, 100);
        Assert.Equal((50, 50), p);
    }

    [Fact]
    public void ClickInLetterboxMargin_ReturnsNull()
    {
        // Same 300x100 area / 100x100 source: x=10 is in the left margin (image starts at x=100).
        Assert.Null(CoordinateMapping.ToSourcePixel(10, 50, 300, 100, 100, 100));
    }

    [Fact]
    public void ClickAtFarEdge_ClampsToLastPixel()
    {
        // Bottom-right corner of a 100x100 source shown 1:1; click exactly at the edge maps to (99,99), not 100.
        var p = CoordinateMapping.ToSourcePixel(100, 100, 100, 100, 100, 100);
        Assert.Equal((99, 99), p);
    }

    [Theory]
    [InlineData(0, 0, 100, 100)]
    [InlineData(100, 100, 0, 0)]
    public void DegenerateSizes_ReturnNull(double areaW, double areaH, int srcW, int srcH)
    {
        Assert.Null(CoordinateMapping.ToSourcePixel(5, 5, areaW, areaH, srcW, srcH));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test C:\git\ADB-wt-m10b\BotBuilder.Core.Tests --filter "FullyQualifiedName~CoordinateMappingTests"`
Expected: compile FAIL.

- [ ] **Step 3: Create `CoordinateMapping`**

Create `BotBuilder.Core/Picker/CoordinateMapping.cs`:

```csharp
namespace BotBuilder.Core.Picker;

/// <summary>Pure geometry mapping a click within a <c>Stretch=Uniform</c> image area (display units) to a
/// source-pixel coordinate. Mirrors WPF's uniform (letterboxed) layout: the source is scaled by the
/// smaller of the width/height ratios and centered, so equal margins appear on the long axis.</summary>
public static class CoordinateMapping
{
    /// <summary>Returns the source pixel under a click, or null when the source/area is degenerate or the
    /// click falls in the letterbox margin (outside the rendered image). The result is clamped to the
    /// last in-bounds pixel ([0, sourceW-1] x [0, sourceH-1]).</summary>
    public static (int X, int Y)? ToSourcePixel(double clickX, double clickY, double areaWidth, double areaHeight, int sourceWidth, int sourceHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || areaWidth <= 0 || areaHeight <= 0)
        {
            return null;
        }

        var scale = Math.Min(areaWidth / sourceWidth, areaHeight / sourceHeight);
        var renderedWidth = sourceWidth * scale;
        var renderedHeight = sourceHeight * scale;
        var offsetX = (areaWidth - renderedWidth) / 2;
        var offsetY = (areaHeight - renderedHeight) / 2;

        var localX = clickX - offsetX;
        var localY = clickY - offsetY;
        if (localX < 0 || localY < 0 || localX > renderedWidth || localY > renderedHeight)
        {
            return null;
        }

        var sourceX = Math.Clamp((int)(localX / scale), 0, sourceWidth - 1);
        var sourceY = Math.Clamp((int)(localY / scale), 0, sourceHeight - 1);
        return (sourceX, sourceY);
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test C:\git\ADB-wt-m10b\BotBuilder.Core.Tests --filter "FullyQualifiedName~CoordinateMappingTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git -C C:\git\ADB-wt-m10b add BotBuilder.Core/Picker/CoordinateMapping.cs BotBuilder.Core.Tests/Picker/CoordinateMappingTests.cs
git -C C:\git\ADB-wt-m10b commit -m "feat(builder): Stretch=Uniform display->source coordinate mapping"
```

---

## Task 3: Picker sequencing view-model (`CoordinatePickerViewModel`)

**Files:**
- Create: `BotBuilder.Core/Picker/CoordinatePickerViewModel.cs`
- Test: `BotBuilder.Core.Tests/Picker/CoordinatePickerViewModelTests.cs`

- [ ] **Step 1: Write the failing test**

Create `BotBuilder.Core.Tests/Picker/CoordinatePickerViewModelTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using BotBuilder.Core.Picker;
using Xunit;

namespace BotBuilder.Core.Tests.Picker;

public class CoordinatePickerViewModelTests
{
    private static CoordinatePickerViewModel ForSwipe() =>
        new(CoordinateFieldMap.ForTypeKey("android.swipe"));

    private static CoordinatePickerViewModel ForTap() =>
        new(CoordinateFieldMap.ForTypeKey("android.tap"));

    [Fact]
    public void SinglePoint_CompletesAfterOneClick_AndYieldsPair()
    {
        var vm = ForTap();
        Assert.False(vm.IsComplete);
        Assert.Contains("Target", vm.CurrentPrompt);

        vm.RecordClick(120, 240);

        Assert.True(vm.IsComplete);
        var r = Assert.Single(vm.Results());
        Assert.Equal(("x", "y", 120, 240), (r.XKey, r.YKey, r.X, r.Y));
    }

    [Fact]
    public void TwoPoints_PromptsStartThenEnd_AndYieldsBothPairs()
    {
        var vm = ForSwipe();
        Assert.Contains("Start", vm.CurrentPrompt);

        vm.RecordClick(10, 20);
        Assert.False(vm.IsComplete);
        Assert.Contains("End", vm.CurrentPrompt);

        vm.RecordClick(30, 40);
        Assert.True(vm.IsComplete);

        var results = vm.Results().ToList();
        Assert.Equal(("x1", "y1", 10, 20), (results[0].XKey, results[0].YKey, results[0].X, results[0].Y));
        Assert.Equal(("x2", "y2", 30, 40), (results[1].XKey, results[1].YKey, results[1].X, results[1].Y));
    }

    [Fact]
    public void ClicksBeyondCompletion_AreIgnored()
    {
        var vm = ForTap();
        vm.RecordClick(1, 2);
        vm.RecordClick(9, 9); // ignored — already complete

        var r = Assert.Single(vm.Results());
        Assert.Equal((1, 2), (r.X, r.Y));
    }

    [Fact]
    public void ResultsBeforeCompletion_OnlyIncludesCollectedPoints()
    {
        var vm = ForSwipe();
        vm.RecordClick(5, 6);
        var r = Assert.Single(vm.Results()); // only the first point so far
        Assert.Equal(("x1", "y1", 5, 6), (r.XKey, r.YKey, r.X, r.Y));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test C:\git\ADB-wt-m10b\BotBuilder.Core.Tests --filter "FullyQualifiedName~CoordinatePickerViewModelTests"`
Expected: compile FAIL.

- [ ] **Step 3: Create `CoordinatePickerViewModel`**

Create `BotBuilder.Core/Picker/CoordinatePickerViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotBuilder.Core.Picker;

/// <summary>Sequences a 1- or 2-point coordinate pick. The dialog feeds it source-pixel clicks via
/// <see cref="RecordClick"/>; it advances through the action's <see cref="CoordinatePoint"/>s, exposes a
/// prompt for the current point, and yields the (fieldKey, value) pairs to write back.</summary>
public partial class CoordinatePickerViewModel : ObservableObject
{
    private readonly IReadOnlyList<CoordinatePoint> _points;
    private readonly List<(int X, int Y)> _collected = new();

    public CoordinatePickerViewModel(IReadOnlyList<CoordinatePoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        _points = points;
    }

    /// <summary>True once every point has a recorded click.</summary>
    public bool IsComplete => _collected.Count >= _points.Count;

    /// <summary>Instruction for the next point, e.g. "Click the Start point". Empty when complete.</summary>
    public string CurrentPrompt => IsComplete ? string.Empty : $"Click the {_points[_collected.Count].Label} point";

    /// <summary>Records a source-pixel click for the current point and advances. No-op once complete.</summary>
    public void RecordClick(int sourceX, int sourceY)
    {
        if (IsComplete)
        {
            return;
        }

        _collected.Add((sourceX, sourceY));
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(CurrentPrompt));
    }

    /// <summary>The collected (XKey, YKey, X, Y) write-back tuples — only points recorded so far.</summary>
    public IReadOnlyList<(string XKey, string YKey, int X, int Y)> Results() =>
        _collected.Select((c, i) => (_points[i].XKey, _points[i].YKey, c.X, c.Y)).ToList();
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test C:\git\ADB-wt-m10b\BotBuilder.Core.Tests --filter "FullyQualifiedName~CoordinatePickerViewModelTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git -C C:\git\ADB-wt-m10b add BotBuilder.Core/Picker/CoordinatePickerViewModel.cs BotBuilder.Core.Tests/Picker/CoordinatePickerViewModelTests.cs
git -C C:\git\ADB-wt-m10b commit -m "feat(builder): CoordinatePickerViewModel (1/2-point sequencing + write-back pairs)"
```

---

## Task 4: `PropertiesViewModel.SupportsCoordinatePicking`

**Files:**
- Modify: `BotBuilder.Core/Properties/PropertiesViewModel.cs`
- Test: `BotBuilder.Core.Tests/Picker/CoordinatePickerViewModelTests.cs` is unrelated; add to existing `BotBuilder.Core.Tests/PropertiesViewModelTests.cs`.

- [ ] **Step 1: Write the failing test**

Append to `BotBuilder.Core.Tests/PropertiesViewModelTests.cs` (inside the existing test class). If a helper to select a node of a given TypeKey already exists in that file, reuse it; otherwise this self-contained test builds an editor, adds a Tap node and a Log node, and checks the flag. Inspect the existing file's helpers first and adapt the node-creation to match them; the assertions are:

```csharp
    [Fact]
    public void SupportsCoordinatePicking_TrueForCoordinateActions_FalseOtherwise()
    {
        var properties = NewPropertiesViewModel(out var editor); // adapt to the file's existing setup helper

        SelectNewNode(editor, "android.tap");   // adapt to the file's existing add+select helper
        Assert.True(properties.SupportsCoordinatePicking);

        SelectNewNode(editor, "data.log");
        Assert.False(properties.SupportsCoordinatePicking);
    }
```

If the existing test file has no reusable `NewPropertiesViewModel`/`SelectNewNode` helpers, instead model this test on the existing tests in that file (they already construct a `PropertiesViewModel` and select nodes) — match their exact construction pattern. Do not invent new infrastructure; mirror what the file already does to put a node of a given TypeKey into `editor.SelectedNode`.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test C:\git\ADB-wt-m10b\BotBuilder.Core.Tests --filter "FullyQualifiedName~PropertiesViewModelTests"`
Expected: FAIL — `SupportsCoordinatePicking` not defined.

- [ ] **Step 3: Add the property**

In `BotBuilder.Core/Properties/PropertiesViewModel.cs`:

Add the using at the top (with the other usings):
```csharp
using BotBuilder.Core.Picker;
```

Add this property (e.g. just after the `SelectedTargetId` property):
```csharp
    /// <summary>Whether the selected action exposes coordinate fields the picker can fill.</summary>
    public bool SupportsCoordinatePicking => Node is not null && CoordinateFieldMap.Supports(Node.TypeKey);
```

In `Rebuild()`, after the existing `OnPropertyChanged(nameof(SelectedTargetId));` / `OnPropertyChanged(nameof(Targets));` lines, add:
```csharp
        OnPropertyChanged(nameof(SupportsCoordinatePicking));
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test C:\git\ADB-wt-m10b\BotBuilder.Core.Tests --filter "FullyQualifiedName~PropertiesViewModelTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git -C C:\git\ADB-wt-m10b add BotBuilder.Core/Properties/PropertiesViewModel.cs BotBuilder.Core.Tests/PropertiesViewModelTests.cs
git -C C:\git\ADB-wt-m10b commit -m "feat(builder): PropertiesViewModel.SupportsCoordinatePicking"
```

---

## Task 5: `FrameCapturer` (WPF — live capture; build-verified)

Resolves a target selector to a still `Bitmap`: Win32 client capture for Window targets, ADB framebuffer for Android. No unit tests (live adapter, mirrors the runner's binders); verified by compile + the user's hands-on pass.

**Files:**
- Create: `BotBuilder/FrameCapturer.cs`

- [ ] **Step 1: Create `BotBuilder/FrameCapturer.cs`**

```csharp
using System.Drawing;
using System.IO;
using System.Linq;
using AdbCore.Android;
using AdbCore.Models;
using AdbCore.Screen;
using AdbCore.Targets;
using AdvancedSharpAdbClient;

namespace BotBuilder;

/// <summary>Captures a still frame of a coordinate-pick target at authoring time. Window targets are
/// resolved to an HWND and client-captured; Android targets are resolved to a device and framebuffer-grabbed.
/// Returns null and an explanatory message on any failure (target not running, no ADB server, bad selector).
/// Live adapter — mirrors the runner's WindowTargetBinder/AndroidTargetBinder; verified by hand.</summary>
public sealed class FrameCapturer
{
    private readonly IWindowResolver _windowResolver = new Win32WindowResolver();
    private readonly IWindowCapture _windowCapture = new Win32WindowCapture();

    public Bitmap? TryCapture(BotTargetType type, string selector, out string? error)
    {
        error = null;
        try
        {
            return type switch
            {
                BotTargetType.Window => CaptureWindow(selector, out error),
                BotTargetType.AndroidDevice => CaptureAndroid(selector, out error),
                _ => Fail($"Coordinate picking isn't supported for {type} targets.", out error),
            };
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private Bitmap? CaptureWindow(string selector, out string? error)
    {
        error = null;
        var hwnd = _windowResolver.Resolve(selector);
        if (hwnd == IntPtr.Zero)
        {
            error = $"Couldn't find a window for '{selector}'. Make sure it's running, then try again.";
            return null;
        }

        return _windowCapture.Capture(hwnd, ScreenCaptureMethod.Auto);
    }

    private static Bitmap? CaptureAndroid(string selector, out string? error)
    {
        error = null;
        var serial = AdbSelector.ParseSerial(selector);
        if (serial is null)
        {
            error = $"Android selector must be in the form 'serial:<device>' (got '{selector}').";
            return null;
        }

        AdbServer.Instance.StartServer(adbPath: "adb", restartServerIfNewer: false);
        var client = new AdbClient();
        var device = client.GetDevices().FirstOrDefault(d => d.Serial == serial);
        if (device is null)
        {
            error = $"No connected Android device with serial '{serial}'. Run `adb devices` to check.";
            return null;
        }

        var png = new AdvancedSharpAdbDevice(client, device).Screenshot();
        using var ms = new MemoryStream(png);
        using var decoded = new Bitmap(ms);
        return new Bitmap(decoded); // detached copy so the MemoryStream can be disposed safely
    }

    private static Bitmap? Fail(string message, out string? error)
    {
        error = message;
        return null;
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build C:\git\ADB-wt-m10b\BotBuilder`
Expected: success. **If the build cannot find `AdbServer`/`AdbClient`/`AdvancedSharpAdbClient`**, the transitive package reference isn't surfacing — add this to `BotBuilder/BotBuilder.csproj` inside an `<ItemGroup>` (match the version AdbCore uses; check `AdbCore/AdbCore.csproj` for `AdvancedSharpAdbClient`):
```xml
    <PackageReference Include="AdvancedSharpAdbClient" Version="3.6.16" />
```
Then rebuild.

- [ ] **Step 3: Commit**

```bash
git -C C:\git\ADB-wt-m10b add BotBuilder/FrameCapturer.cs BotBuilder/BotBuilder.csproj
git -C C:\git\ADB-wt-m10b commit -m "feat(builder): FrameCapturer (Win32 client + ADB framebuffer still capture)"
```

---

## Task 6: `CoordinatePickerDialog` (WPF — interactive; build-verified)

A modal dialog showing the captured frame; the user clicks the point(s); on completion it sets `DialogResult=true` and exposes the results.

**Files:**
- Create: `BotBuilder/CoordinatePickerDialog.xaml`, `BotBuilder/CoordinatePickerDialog.xaml.cs`

- [ ] **Step 1: Create `BotBuilder/CoordinatePickerDialog.xaml`**

```xml
<Window x:Class="BotBuilder.CoordinatePickerDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Pick coordinates" Height="700" Width="900"
        WindowStartupLocation="CenterOwner" Background="#222">
    <DockPanel>
        <Border DockPanel.Dock="Top" Background="#333" Padding="10,6">
            <DockPanel>
                <Button DockPanel.Dock="Right" Content="Cancel" Padding="12,2" Click="OnCancel" />
                <TextBlock x:Name="PromptText" Foreground="White" FontSize="14" VerticalAlignment="Center" />
            </DockPanel>
        </Border>
        <Grid x:Name="ImageHost" ClipToBounds="True">
            <Image x:Name="FrameImage" Stretch="Uniform"
                   MouseLeftButtonDown="OnImageClick" Cursor="Cross" />
            <Canvas x:Name="MarkerCanvas" IsHitTestVisible="False" />
        </Grid>
    </DockPanel>
</Window>
```

- [ ] **Step 2: Create `BotBuilder/CoordinatePickerDialog.xaml.cs`**

```csharp
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using BotBuilder.Core.Picker;

namespace BotBuilder;

public partial class CoordinatePickerDialog : Window
{
    private readonly CoordinatePickerViewModel _vm;
    private readonly int _sourceWidth;
    private readonly int _sourceHeight;

    public CoordinatePickerDialog(CoordinatePickerViewModel vm, Bitmap frame)
    {
        InitializeComponent();
        _vm = vm;
        _sourceWidth = frame.Width;
        _sourceHeight = frame.Height;
        FrameImage.Source = ToImageSource(frame);
        PromptText.Text = _vm.CurrentPrompt;
    }

    /// <summary>The collected (XKey, YKey, X, Y) write-back tuples — valid after the dialog returns true.</summary>
    public IReadOnlyList<(string XKey, string YKey, int X, int Y)> Results => _vm.Results();

    private void OnImageClick(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(FrameImage);
        var mapped = CoordinateMapping.ToSourcePixel(
            pos.X, pos.Y, FrameImage.ActualWidth, FrameImage.ActualHeight, _sourceWidth, _sourceHeight);
        if (mapped is not (int sx, int sy))
        {
            return; // clicked the letterbox margin — ignore
        }

        _vm.RecordClick(sx, sy);
        DrawMarker(pos);
        PromptText.Text = _vm.CurrentPrompt;

        if (_vm.IsComplete)
        {
            DialogResult = true;
            Close();
        }
    }

    private void DrawMarker(Point at)
    {
        var dot = new Ellipse
        {
            Width = 14,
            Height = 14,
            Stroke = Brushes.Lime,
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(80, 0, 255, 0)),
        };
        Canvas.SetLeft(dot, at.X - 7);
        Canvas.SetTop(dot, at.Y - 7);
        MarkerCanvas.Children.Add(dot);
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    // Decodes the bitmap into a frozen WPF source so the caller can dispose the source Bitmap immediately.
    private static ImageSource ToImageSource(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = ms;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build C:\git\ADB-wt-m10b\BotBuilder`
Expected: success, 0 warnings.

- [ ] **Step 4: Commit**

```bash
git -C C:\git\ADB-wt-m10b add BotBuilder/CoordinatePickerDialog.xaml BotBuilder/CoordinatePickerDialog.xaml.cs
git -C C:\git\ADB-wt-m10b commit -m "feat(builder): CoordinatePickerDialog (click frame -> source-pixel points)"
```

---

## Task 7: MainWindow integration — "Pick coordinates…" button + write-back

**Files:**
- Modify: `BotBuilder/MainWindow.xaml`
- Modify: `BotBuilder/MainWindow.xaml.cs`

- [ ] **Step 1: Add the button to the Properties panel**

In `BotBuilder/MainWindow.xaml`, between the `Fields` ItemsControl and the Retry `Border` (after the `<ItemsControl ItemsSource="{Binding Fields}" .../>` element, before the `<Border ... Visibility="{Binding SupportsRetry ...}">`), add:

```xml
                            <Button Content="Pick coordinates…" Margin="0,8,0,0" Padding="6,3" HorizontalAlignment="Left"
                                    Click="PickCoordinates_Click"
                                    Visibility="{Binding SupportsCoordinatePicking, Converter={StaticResource BoolToVis}}" />
```

(`BoolToVis` is the same converter the Retry section already uses.)

- [ ] **Step 2: Add the click handler + helpers**

In `BotBuilder/MainWindow.xaml.cs`, add a field near the other private fields (e.g. by the editor field):

```csharp
    private readonly FrameCapturer _frameCapturer = new();
```

Add these methods (near `CaptureField_Click`):

```csharp
    private void PickCoordinates_Click(object sender, RoutedEventArgs e)
    {
        var node = _editor.Properties.Node;
        if (node is null)
        {
            return;
        }

        var points = BotBuilder.Core.Picker.CoordinateFieldMap.ForTypeKey(node.TypeKey);
        if (points.Count == 0)
        {
            return;
        }

        // Resolve the action's bound target: explicit TargetId, else the first configured target.
        var targets = _editor.TargetBar.Targets;
        var target = node.TargetId is System.Guid id
            ? targets.FirstOrDefault(t => t.Id == id)
            : targets.FirstOrDefault();
        if (target is null)
        {
            MessageBox.Show(
                "Add a target (Window or Android device) first, then pick coordinates against it.",
                "Pick coordinates", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var frame = _frameCapturer.TryCapture(target.Type, target.Selector, out var error);
        if (frame is null)
        {
            MessageBox.Show(error ?? "Couldn't capture the target.",
                "Pick coordinates", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        bool confirmed;
        IReadOnlyList<(string XKey, string YKey, int X, int Y)> results;
        try
        {
            var vm = new BotBuilder.Core.Picker.CoordinatePickerViewModel(points);
            var dialog = new CoordinatePickerDialog(vm, frame) { Owner = this };
            confirmed = dialog.ShowDialog() == true;
            results = dialog.Results;
        }
        finally
        {
            frame.Dispose();
        }

        if (!confirmed)
        {
            return;
        }

        foreach (var (xKey, yKey, x, y) in results)
        {
            if (FieldByKey(xKey) is { } fx)
            {
                fx.Value = (double)x;
            }
            if (FieldByKey(yKey) is { } fy)
            {
                fy.Value = (double)y;
            }
        }
    }

    /// <summary>The selected action's config field with the given key, or null.</summary>
    private ConfigFieldViewModel? FieldByKey(string key) =>
        _editor.Properties.Fields.FirstOrDefault(f => f.Key == key);
```

Note: `ConfigFieldViewModel` is `BotBuilder.Core.Properties.ConfigFieldViewModel` — confirm the using is already present in the file (it is used by `CaptureField_Click`/`ConfidenceFieldOrNull`), so no new using is needed. `FrameCapturer` is in the same `BotBuilder` namespace.

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build C:\git\ADB-wt-m10b\BotBuilder`
Expected: success, 0 warnings.

- [ ] **Step 4: Commit**

```bash
git -C C:\git\ADB-wt-m10b add BotBuilder/MainWindow.xaml BotBuilder/MainWindow.xaml.cs
git -C C:\git\ADB-wt-m10b commit -m "feat(builder): Pick coordinates button wires picker into the Properties panel"
```

---

## Task 8: Full build + test sweep + user-verification handoff

**Files:** none (verification).

- [ ] **Step 1: Build the whole solution, 0 warnings**

Run: `dotnet build C:\git\ADB-wt-m10b\ADB.slnx`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 2: Run the full test suite**

Run: `dotnet test C:\git\ADB-wt-m10b\ADB.slnx`
Expected: all pass. BotBuilder.Core.Tests gains the new Picker tests (CoordinateFieldMap, CoordinateMapping, CoordinatePickerViewModel) + the SupportsCoordinatePicking case; no other counts change (M10b adds no actions). Confirm 0 failures.

- [ ] **Step 3: Stop — hand off for visual verification**

This slice has interactive WPF that cannot be auto-verified. Do NOT merge. Report completion to the controller for user visual verification against spec §6 watch-items:
1. "Pick coordinates…" appears for Tap/Swipe (Android) and Click/Right/Double/Mouse Move (Windows); absent on other actions.
2. The picker captures the correct bound target (Windows window and Android device).
3. Clicking a point writes coordinates that land where expected at run time (validates mapping + DPI end-to-end).
4. Swipe: Start marker then End; both pairs written.
5. Target not connected → friendly message, no crash.

---

## Self-Review Notes (addressed)

- **Spec §4 coverage:** Pick button per coordinate action (Task 7 + `SupportsCoordinatePicking`); field metadata 1-point/2-point with exact keys (Task 1, verified against sources); target resolution Window/Android (Task 5/7); snapshot capture, not live (Task 5); source-pixel mapping under Stretch=Uniform (Task 2); start-then-end 2-point flow (Task 3/6); write-back to ConfigFieldViewModel (Task 7); friendly not-connected/failure messages (Task 5/7). ✓
- **Precision aid:** floor is scaled-to-fit click + source-pixel mapping (no magnifier in v1, per spec — magnifier deferred unless verification shows it's needed; explicitly NOT the BotCapture dual-panel). ✓
- **DPI:** mapping is ratio-based (display DIPs → source pixels), so picker-window DPI cancels out; captured client/device pixels match the runtime click space (Win32 client capture + client-relative runtime clicks, as established in M5). No new DPI code required; flagged for the user's end-to-end check (watch-item 3). ✓
- **Disposal:** dialog decodes the Bitmap into a frozen `BitmapImage` (OnLoad), so `MainWindow` disposes the source `Bitmap` in a `finally` right after `ShowDialog`. Android capture returns a detached copy so its `MemoryStream` is safely disposed. ✓
- **No placeholders:** every code step is complete. The one adaptive step is the `PropertiesViewModelTests` helper reuse (Task 4 Step 1) — the implementer mirrors the file's existing node-selection pattern rather than inventing infrastructure.
- **No new action counts:** M10b registers no actions, so registry/palette count assertions are untouched.
