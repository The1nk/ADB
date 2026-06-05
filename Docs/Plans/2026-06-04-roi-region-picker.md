# ROI Region Picker Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A "Pick region…" button in the Properties panel that captures the selected action's bound target, lets the author drag a box, and writes `regionX/Y/Width/Height` — the region analog of the M10b coordinate picker.

**Architecture:** Generic detection (any action with region fields shows the button); reuse M10b's `FrameCapturer` (capture bound target → Bitmap) and `CoordinateMapping.ToSourcePixel`; a pure `RegionSelection` helper turns two source-pixel corners into a clamped rectangle; a thin `RegionPickerDialog` does the drag interaction.

**Tech Stack:** C# / .NET 10, WPF, CommunityToolkit.Mvvm, xUnit. Branches off `main` (M10b's FrameCapturer/CoordinateMapping + the region-bearing Find Image action are merged).

**Reference spec:** `Docs/Specs/2026-06-04-roi-region-picker-design.md`.

**Merge handling:** WPF dialog → NOT self-merged; built compile-clean + unit-green, PR opened, user visually verifies + merges.

**`<WORKTREE>` below = the actual worktree path the controller provides (e.g. `C:\git\ADB\.claude\worktrees\roi-region-picker`).**

---

## File Structure

- Create `BotBuilder.Core/Picker/RegionSelection.cs` — pure corner→clamped-rect helper.
- Modify `BotBuilder.Core/Properties/PropertiesViewModel.cs` — add `SupportsRegionPicking`.
- Create `BotBuilder/RegionPickerDialog.xaml` + `.xaml.cs` — drag-a-box dialog.
- Modify `BotBuilder/MainWindow.xaml` — "Pick region…" button.
- Modify `BotBuilder/MainWindow.xaml.cs` — `PickRegion_Click`.
- Tests: `BotBuilder.Core.Tests/Picker/RegionSelectionTests.cs`; append to `PropertiesViewModelTests.cs`.

---

## Task 1: `RegionSelection.FromCorners`

**Files:** Create `BotBuilder.Core/Picker/RegionSelection.cs`; `BotBuilder.Core.Tests/Picker/RegionSelectionTests.cs`.

- [ ] **Step 1: Write the failing test**

Create `BotBuilder.Core.Tests/Picker/RegionSelectionTests.cs`:
```csharp
using BotBuilder.Core.Picker;
using Xunit;

namespace BotBuilder.Core.Tests.Picker;

public class RegionSelectionTests
{
    [Fact]
    public void FromCorners_NormalizesTopLeftAndSize()
    {
        var r = RegionSelection.FromCorners(40, 30, 10, 5, 1000, 1000);
        Assert.Equal((10, 5, 30, 25), r); // left=10, top=5, w=40-10, h=30-5
    }

    [Fact]
    public void FromCorners_AlreadyOrdered_IsUnchanged()
    {
        var r = RegionSelection.FromCorners(10, 20, 60, 80, 1000, 1000);
        Assert.Equal((10, 20, 50, 60), r);
    }

    [Fact]
    public void FromCorners_ClampsToImageBounds()
    {
        var r = RegionSelection.FromCorners(-50, -10, 5000, 5000, 800, 600);
        Assert.Equal((0, 0, 800, 600), r);
    }

    [Fact]
    public void FromCorners_DegeneratePoint_ZeroSize()
    {
        var r = RegionSelection.FromCorners(100, 100, 100, 100, 800, 600);
        Assert.Equal((100, 100, 0, 0), r);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test <WORKTREE>\BotBuilder.Core.Tests --filter "FullyQualifiedName~RegionSelectionTests"`
Expected: compile FAIL.

- [ ] **Step 3: Create `BotBuilder.Core/Picker/RegionSelection.cs`**
```csharp
namespace BotBuilder.Core.Picker;

/// <summary>Turns two source-pixel corners into a top-left-origin rectangle with positive size,
/// clamped to the image bounds. Used by the ROI region picker.</summary>
public static class RegionSelection
{
    public static (int X, int Y, int Width, int Height) FromCorners(int x1, int y1, int x2, int y2, int imageWidth, int imageHeight)
    {
        var left = Math.Clamp(Math.Min(x1, x2), 0, imageWidth);
        var top = Math.Clamp(Math.Min(y1, y2), 0, imageHeight);
        var right = Math.Clamp(Math.Max(x1, x2), 0, imageWidth);
        var bottom = Math.Clamp(Math.Max(y1, y2), 0, imageHeight);
        return (left, top, right - left, bottom - top);
    }
}
```

- [ ] **Step 4: Run to verify it passes** — same command → PASS (4 tests).

- [ ] **Step 5: Commit**
```bash
git -C <WORKTREE> add BotBuilder.Core/Picker/RegionSelection.cs BotBuilder.Core.Tests/Picker/RegionSelectionTests.cs
git -C <WORKTREE> commit -m "feat(builder): RegionSelection.FromCorners (normalize + clamp ROI rect)"
```

---

## Task 2: `PropertiesViewModel.SupportsRegionPicking`

**Files:** Modify `BotBuilder.Core/Properties/PropertiesViewModel.cs`; append to `BotBuilder.Core.Tests/PropertiesViewModelTests.cs`.

- [ ] **Step 1: Write the failing test**

Append to `BotBuilder.Core.Tests/PropertiesViewModelTests.cs` (use the file's existing `BuiltInEditor()` + `AddNode`/`Select` helpers — read the file and match them):
```csharp
    [Fact]
    public void SupportsRegionPicking_TrueForActionsWithRegionFields_FalseOtherwise()
    {
        var e = BuiltInEditor();                      // adapt to the file's helper

        e.Select(e.AddNode("screen.findImage", 0, 0));
        Assert.True(e.Properties.SupportsRegionPicking);

        e.Select(e.AddNode("data.log", 0, 0));
        Assert.False(e.Properties.SupportsRegionPicking);
    }
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test <WORKTREE>\BotBuilder.Core.Tests --filter "FullyQualifiedName~PropertiesViewModelTests"` → FAIL (member missing).

- [ ] **Step 3: Implement**

In `BotBuilder.Core/Properties/PropertiesViewModel.cs`:
Add usings (if not present):
```csharp
using System.Linq;
using AdbCore.Actions.BuiltIn;
```
Add the property (near `SupportsCoordinatePicking`):
```csharp
    /// <summary>Whether the selected action exposes ROI region fields the region picker can fill.</summary>
    public bool SupportsRegionPicking =>
        Node is not null
        && _registry.TryGet(Node.TypeKey, out var def) && def is not null
        && def.ConfigFields.Any(f => f.Key == TemplateMatchCore.RegionWidthKey);
```
In `Rebuild()`, after the other `OnPropertyChanged(...)` calls, add:
```csharp
        OnPropertyChanged(nameof(SupportsRegionPicking));
```

- [ ] **Step 4: Run to verify it passes** — same command → PASS.

- [ ] **Step 5: Commit**
```bash
git -C <WORKTREE> add BotBuilder.Core/Properties/PropertiesViewModel.cs BotBuilder.Core.Tests/PropertiesViewModelTests.cs
git -C <WORKTREE> commit -m "feat(builder): PropertiesViewModel.SupportsRegionPicking (detect region fields)"
```

---

## Task 3: `RegionPickerDialog` (WPF; build-only)

**Files:** Create `BotBuilder/RegionPickerDialog.xaml` + `.xaml.cs`.

- [ ] **Step 1: Create `BotBuilder/RegionPickerDialog.xaml`**
```xml
<Window x:Class="BotBuilder.RegionPickerDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Pick region" Height="700" Width="900"
        WindowStartupLocation="CenterOwner" Background="#222">
    <DockPanel>
        <Border DockPanel.Dock="Top" Background="#333" Padding="10,6">
            <DockPanel>
                <Button DockPanel.Dock="Right" Content="Cancel" Padding="12,2" Click="OnCancel" />
                <TextBlock Text="Drag a box around the region" Foreground="White" FontSize="14" VerticalAlignment="Center" />
            </DockPanel>
        </Border>
        <Grid x:Name="ImageHost" ClipToBounds="True">
            <Image x:Name="FrameImage" Stretch="Uniform"
                   MouseLeftButtonDown="OnDragStart" MouseMove="OnDragMove" MouseLeftButtonUp="OnDragEnd" Cursor="Cross" />
            <Canvas x:Name="OverlayCanvas" IsHitTestVisible="False">
                <Rectangle x:Name="RubberBand" Stroke="Lime" StrokeThickness="2" Visibility="Collapsed"
                           Fill="#3000FF00" />
            </Canvas>
        </Grid>
    </DockPanel>
</Window>
```

- [ ] **Step 2: Create `BotBuilder/RegionPickerDialog.xaml.cs`**
```csharp
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BotBuilder.Core.Picker;

namespace BotBuilder;

public partial class RegionPickerDialog : Window
{
    private readonly int _sourceWidth;
    private readonly int _sourceHeight;
    private System.Windows.Point? _dragStart;

    public RegionPickerDialog(Bitmap frame)
    {
        InitializeComponent();
        _sourceWidth = frame.Width;
        _sourceHeight = frame.Height;
        FrameImage.Source = ToImageSource(frame);
    }

    /// <summary>The chosen region in source pixels (X, Y, Width, Height); valid after the dialog returns true.</summary>
    public (int X, int Y, int Width, int Height)? Region { get; private set; }

    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(FrameImage);
        Canvas.SetLeft(RubberBand, _dragStart.Value.X);
        Canvas.SetTop(RubberBand, _dragStart.Value.Y);
        RubberBand.Width = 0;
        RubberBand.Height = 0;
        RubberBand.Visibility = Visibility.Visible;
        FrameImage.CaptureMouse();
    }

    private void OnDragMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not System.Windows.Point start)
        {
            return;
        }
        var p = e.GetPosition(FrameImage);
        Canvas.SetLeft(RubberBand, System.Math.Min(start.X, p.X));
        Canvas.SetTop(RubberBand, System.Math.Min(start.Y, p.Y));
        RubberBand.Width = System.Math.Abs(p.X - start.X);
        RubberBand.Height = System.Math.Abs(p.Y - start.Y);
    }

    private void OnDragEnd(object sender, MouseButtonEventArgs e)
    {
        FrameImage.ReleaseMouseCapture();
        if (_dragStart is not System.Windows.Point start)
        {
            return;
        }
        _dragStart = null;
        RubberBand.Visibility = Visibility.Collapsed;

        var end = e.GetPosition(FrameImage);
        var a = CoordinateMapping.ToSourcePixel(start.X, start.Y, FrameImage.ActualWidth, FrameImage.ActualHeight, _sourceWidth, _sourceHeight);
        var b = CoordinateMapping.ToSourcePixel(end.X, end.Y, FrameImage.ActualWidth, FrameImage.ActualHeight, _sourceWidth, _sourceHeight);
        if (a is not (int ax, int ay) || b is not (int bx, int by))
        {
            return; // a corner fell in the letterbox margin — ignore, let the user re-drag
        }

        var region = RegionSelection.FromCorners(ax, ay, bx, by, _sourceWidth, _sourceHeight);
        if (region.Width <= 0 || region.Height <= 0)
        {
            return; // degenerate — ignore
        }

        Region = region;
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

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

- [ ] **Step 3: Build** — `dotnet build <WORKTREE>\BotBuilder` → success, 0 warnings. (If `System.Drawing`/`System.Windows` type collisions appear — e.g. `Point`, `Rectangle` — fully-qualify as shown; `System.Windows.Shapes.Rectangle` for the XAML `RubberBand`, `System.Windows.Point` for drag points, `System.Drawing.Bitmap`/`ImageFormat` for the source. Report any qualification added.)

- [ ] **Step 4: Commit**
```bash
git -C <WORKTREE> add BotBuilder/RegionPickerDialog.xaml BotBuilder/RegionPickerDialog.xaml.cs
git -C <WORKTREE> commit -m "feat(builder): RegionPickerDialog (drag-a-box -> source-pixel ROI)"
```

---

## Task 4: MainWindow "Pick region…" button + handler

**Files:** Modify `BotBuilder/MainWindow.xaml`, `BotBuilder/MainWindow.xaml.cs`.

Context: MainWindow already has (from M10b) a "Pick coordinates…" button in the Properties panel bound to `SupportsCoordinatePicking`, a `private readonly FrameCapturer _frameCapturer = new();`, a `PickCoordinates_Click` handler, and a `FieldByKey(string key)` helper. Reuse them.

- [ ] **Step 1: Add the button to `MainWindow.xaml`**

Immediately after the existing `<Button Content="Pick coordinates…" .../>`, add:
```xml
                            <Button Content="Pick region…" Margin="0,4,0,0" Padding="6,3" HorizontalAlignment="Left"
                                    Click="PickRegion_Click"
                                    Visibility="{Binding SupportsRegionPicking, Converter={StaticResource BoolToVis}}" />
```

- [ ] **Step 2: Add the handler to `MainWindow.xaml.cs`**

Add near `PickCoordinates_Click`:
```csharp
    private void PickRegion_Click(object sender, RoutedEventArgs e)
    {
        var node = _editor.Properties.Node;
        if (node is null)
        {
            return;
        }

        var targets = _editor.TargetBar.Targets;
        var target = node.TargetId is System.Guid id
            ? targets.FirstOrDefault(t => t.Id == id)
            : targets.FirstOrDefault();
        if (target is null)
        {
            MessageBox.Show(
                "Add a target (Window or Android device) first, then pick a region against it.",
                "Pick region", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var frame = _frameCapturer.TryCapture(target.Type, target.Selector, out var error);
        if (frame is null)
        {
            MessageBox.Show(error ?? "Couldn't capture the target.",
                "Pick region", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        (int X, int Y, int Width, int Height)? region;
        bool confirmed;
        try
        {
            var dialog = new RegionPickerDialog(frame) { Owner = this };
            confirmed = dialog.ShowDialog() == true;
            region = dialog.Region;
        }
        finally
        {
            frame.Dispose();
        }

        if (!confirmed || region is not (int rx, int ry, int rw, int rh))
        {
            return;
        }

        if (FieldByKey(AdbCore.Actions.BuiltIn.TemplateMatchCore.RegionXKey) is { } fx) { fx.Value = (double)rx; }
        if (FieldByKey(AdbCore.Actions.BuiltIn.TemplateMatchCore.RegionYKey) is { } fy) { fy.Value = (double)ry; }
        if (FieldByKey(AdbCore.Actions.BuiltIn.TemplateMatchCore.RegionWidthKey) is { } fw) { fw.Value = (double)rw; }
        if (FieldByKey(AdbCore.Actions.BuiltIn.TemplateMatchCore.RegionHeightKey) is { } fh) { fh.Value = (double)rh; }
    }
```
(`FrameCapturer`, `RegionPickerDialog` are same-namespace; `FieldByKey`/`_frameCapturer`/`_editor` already exist. `TemplateMatchCore` is `AdbCore.Actions.BuiltIn` — fully-qualified inline so no new using needed, or add `using AdbCore.Actions.BuiltIn;` if the file prefers.)

- [ ] **Step 3: Build** — `dotnet build <WORKTREE>\BotBuilder` → success, 0 warnings.

- [ ] **Step 4: Commit**
```bash
git -C <WORKTREE> add BotBuilder/MainWindow.xaml BotBuilder/MainWindow.xaml.cs
git -C <WORKTREE> commit -m "feat(builder): Pick region button wires the ROI picker into the Properties panel"
```

---

## Task 5: Build + test sweep + PR (user-verified)

- [ ] **Step 1:** `dotnet build <WORKTREE>\ADB.slnx` → success, 0 warnings.
- [ ] **Step 2:** `dotnet test <WORKTREE>\ADB.slnx` → all pass (BotBuilder.Core.Tests +4 RegionSelection, +1 SupportsRegionPicking; no other counts change — no new actions). Report counts.
- [ ] **Step 3:** Push `worktree-roi-region-picker`; `gh pr create` (base main) with a summary + the visual-verify watch-items (Pick region appears on Find Image / Screen+Android image+OCR actions; drag a box; the 4 region fields populate; a picked region actually constrains a Find Image match at run time; target-not-connected → friendly message). **Do NOT merge** — parked for user visual verify. Report the PR URL.

---

## Self-Review Notes (addressed)

- **Spec coverage:** generic detection (Task 2 `SupportsRegionPicking` via region-field presence); corner→clamped-rect (Task 1); drag dialog reusing `CoordinateMapping` + frozen ImageSource (Task 3); MainWindow button + capture + write-back reusing `FrameCapturer`/`FieldByKey` (Task 4); degenerate-drag + margin-corner ignored (Task 3). ✓
- **Reuse:** `FrameCapturer`, `CoordinateMapping`, `FieldByKey`, `_frameCapturer`, the BoolToVis converter, and the ToImageSource pattern are all from merged M10b — no duplication. ✓
- **Type consistency:** `RegionSelection.FromCorners` signature, `Region` tuple shape, and the `TemplateMatchCore.Region*Key` constants are referenced consistently across tasks. ✓
- **No new action counts:** no actions added, so registry/palette counts untouched.
- **Adaptive points flagged:** Task 2 test reuses the file's existing editor helper; Task 3 notes the System.Drawing/System.Windows type-qualification that may be needed.
