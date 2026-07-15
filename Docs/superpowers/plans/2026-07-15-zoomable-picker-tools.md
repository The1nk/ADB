# Zoomable Frame-Picker Tools Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add wheel-zoom (to cursor) + middle-drag pan to all four "click/drag on a captured frame" tools — CoordinatePicker, RegionPicker, ColorDropper (BotBuilder) and RegionSelectView (BotCapture) — via one shared control.

**Architecture:** A pure geometry class `ViewportTransform` and a reusable WPF `UserControl` `ZoomPanImageHost` both live in the shared `AdbUi.Theme` library (referenced by both apps). The control sizes the image exactly to `source × scale` inside a `ScrollViewer` (no letterbox), owns all zoom/pan/toolbar behavior, and exposes an API where consumers speak **only in source-pixel coordinates**. Each of the four surfaces drops its `Grid > Image + Canvas` block and mapping code, keeping only its own behavior.

**Tech Stack:** .NET 10, WPF, C# (nullable enabled), xUnit (`AdbUi.Theme.Tests`).

---

## Slice / PR boundary

- **Slice 1 (all dev): Tasks 1–9.** One PR. Because it changes the WPF pickers, the user visually verifies before merge.
- **Slice 2 (docs): Task 10.** A follow-up PR done after Slice 1 is verified/merged.

## File Structure

**Created:**
- `AdbUi.Theme/Controls/ViewportTransform.cs` — pure zoom/pan/mapping geometry (no WPF types).
- `AdbUi.Theme/Controls/ImagePointerEventArgs.cs` — source-pixel pointer event args for the control.
- `AdbUi.Theme/Controls/ZoomPanImageHost.xaml` + `.xaml.cs` — the reusable zoom/pan image control.
- `AdbUi.Theme.Tests/Controls/ViewportTransformTests.cs` — geometry tests.
- `BotBuilder/FrameImageSource.cs` — one shared `Bitmap → BitmapSource` helper for the three BotBuilder dialogs (replaces three copies).

**Modified:**
- `BotBuilder/CoordinatePickerDialog.xaml` + `.xaml.cs`
- `BotBuilder/ColorDropperDialog.xaml` + `.xaml.cs`
- `BotBuilder/RegionPickerDialog.xaml` + `.xaml.cs`
- `BotCapture/Views/RegionSelectView.xaml` + `.xaml.cs`

**Deleted (once dead):**
- `BotBuilder.Core/Picker/CoordinateMapping.cs`
- `BotBuilder.Core.Tests/Picker/CoordinateMappingTests.cs`

**Unchanged (kept):** `BotBuilder.Core/Picker/RegionSelection.cs` (still clamps drag corners), `CoordinatePickerViewModel`, `ColorHex`, `RegionSelectionViewModel`, `BotCapture/Views/BitmapInterop.cs`. Dialog constructors and `RegionSelectView.Bind` keep their existing signatures, so no callers in `MainWindow.xaml.cs` change.

---

## Task 1: `ViewportTransform` pure geometry

**Files:**
- Create: `AdbUi.Theme/Controls/ViewportTransform.cs`
- Test: `AdbUi.Theme.Tests/Controls/ViewportTransformTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `AdbUi.Theme.Tests/Controls/ViewportTransformTests.cs`:

```csharp
using AdbUi.Theme.Controls;

namespace AdbUi.Theme.Tests.Controls;

public class ViewportTransformTests
{
    [Fact]
    public void FitScale_WideSource_UsesWidthRatio()
    {
        // 1920x1080 into 900x700 -> min(900/1920, 700/1080) = 0.46875
        var scale = ViewportTransform.FitScale(900, 700, 1920, 1080);
        Assert.Equal(0.46875, scale, 5);
    }

    [Fact]
    public void FitScale_TallSource_UsesHeightRatio()
    {
        // 1080x1920 into 900x700 -> min(900/1080, 700/1920) = 0.364583...
        var scale = ViewportTransform.FitScale(900, 700, 1080, 1920);
        Assert.Equal(700.0 / 1920.0, scale, 5);
    }

    [Fact]
    public void FitScale_DegenerateInput_ReturnsOne()
    {
        Assert.Equal(1.0, ViewportTransform.FitScale(0, 700, 100, 100));
        Assert.Equal(1.0, ViewportTransform.FitScale(900, 700, 0, 100));
    }

    [Fact]
    public void ClampScale_HonorsBounds()
    {
        Assert.Equal(ViewportTransform.MaxScale, ViewportTransform.ClampScale(100));
        Assert.Equal(ViewportTransform.MinScale, ViewportTransform.ClampScale(0.0001));
        Assert.Equal(1.0, ViewportTransform.ClampScale(1.0));
    }

    [Fact]
    public void StepScale_AppliesGeometricFactorAndClamps()
    {
        Assert.Equal(1.2, ViewportTransform.StepScale(1.0, 1), 5);
        Assert.Equal(1.0 / 1.2, ViewportTransform.StepScale(1.0, -1), 5);
        Assert.Equal(ViewportTransform.MaxScale, ViewportTransform.StepScale(30, 5));
    }

    [Fact]
    public void ZoomToCursorOffset_KeepsSourcePixelUnderCursorFixed()
    {
        double oldScale = 1.0, newScale = 2.0, oldOffset = 40, cursor = 100;
        var newOffset = ViewportTransform.ZoomToCursorOffset(oldOffset, cursor, oldScale, newScale);

        var sourceBefore = (oldOffset + cursor) / oldScale;
        var sourceAfter = (newOffset + cursor) / newScale;
        Assert.Equal(sourceBefore, sourceAfter, 6);
    }

    [Fact]
    public void PointToSource_Inside_Maps()
    {
        var p = ViewportTransform.PointToSource(50, 50, 2.0, 100, 100);
        Assert.Equal((25, 25), p);
    }

    [Fact]
    public void PointToSource_Outside_ReturnsNull()
    {
        Assert.Null(ViewportTransform.PointToSource(-1, 10, 2.0, 100, 100));
        Assert.Null(ViewportTransform.PointToSource(50, 210, 2.0, 100, 100)); // 210/2=105 > 100
    }

    [Fact]
    public void PointToSource_RightEdge_ClampsToLastPixel()
    {
        // 200/2 = 100 == sourceWidth (edge inclusive) -> clamps to 99
        var p = ViewportTransform.PointToSource(200, 200, 2.0, 100, 100);
        Assert.Equal((99, 99), p);
    }

    [Fact]
    public void ClampToSource_AlwaysReturnsInBoundsPixel()
    {
        Assert.Equal((99, 99), ViewportTransform.ClampToSource(250, 250, 2.0, 100, 100));
        Assert.Equal((0, 0), ViewportTransform.ClampToSource(-10, -10, 2.0, 100, 100));
    }

    [Fact]
    public void SourceToDisplay_RoundTripsWithPointToSource()
    {
        var (dx, dy) = ViewportTransform.SourceToDisplay(25, 25, 2.0);
        Assert.Equal((50.0, 50.0), (dx, dy));
        Assert.Equal((25, 25), ViewportTransform.PointToSource(dx, dy, 2.0, 100, 100));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~ViewportTransformTests"`
Expected: FAIL — `ViewportTransform` does not exist (compile error).

- [ ] **Step 3: Write the implementation**

Create `AdbUi.Theme/Controls/ViewportTransform.cs`:

```csharp
namespace AdbUi.Theme.Controls;

/// <summary>Pure geometry for a zoom/pan image viewport. The content is sized exactly to
/// <c>source × scale</c> (no letterbox), so mapping a point on the image to a source pixel is a plain
/// divide by scale. No <c>System.Windows</c> types, so it is fully unit-testable.</summary>
public static class ViewportTransform
{
    /// <summary>Smallest allowed zoom (5%).</summary>
    public const double MinScale = 0.05;

    /// <summary>Largest allowed zoom (32×).</summary>
    public const double MaxScale = 32.0;

    private const double WheelStepFactor = 1.2;

    /// <summary>The zoom that makes the whole source fit inside the viewport (min of the axis ratios),
    /// clamped to [<see cref="MinScale"/>, <see cref="MaxScale"/>]. Returns 1.0 for degenerate input.</summary>
    public static double FitScale(double viewportWidth, double viewportHeight, int sourceWidth, int sourceHeight)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0 || sourceWidth <= 0 || sourceHeight <= 0)
        {
            return 1.0;
        }

        var scale = Math.Min(viewportWidth / sourceWidth, viewportHeight / sourceHeight);
        return ClampScale(scale);
    }

    /// <summary>Clamps a zoom into [<see cref="MinScale"/>, <see cref="MaxScale"/>].</summary>
    public static double ClampScale(double scale) => Math.Clamp(scale, MinScale, MaxScale);

    /// <summary>Applies <paramref name="wheelTicks"/> geometric zoom steps (positive = in), then clamps.</summary>
    public static double StepScale(double scale, int wheelTicks)
        => ClampScale(scale * Math.Pow(WheelStepFactor, wheelTicks));

    /// <summary>The new scroll offset (one axis) that keeps the source pixel currently under the cursor
    /// fixed across a zoom change. Offsets/cursor are in display units; the caller's ScrollViewer clamps
    /// the result into its scrollable range.</summary>
    public static double ZoomToCursorOffset(double oldOffset, double viewportCursor, double oldScale, double newScale)
    {
        if (oldScale <= 0)
        {
            return 0;
        }

        return (oldOffset + viewportCursor) * (newScale / oldScale) - viewportCursor;
    }

    /// <summary>Maps a point on the <c>source × scale</c> image to a source pixel, or null when the point
    /// falls outside the image. The far/bottom edge is inclusive and clamps to the last pixel.</summary>
    public static (int X, int Y)? PointToSource(double pointX, double pointY, double scale, int sourceWidth, int sourceHeight)
    {
        if (scale <= 0 || sourceWidth <= 0 || sourceHeight <= 0)
        {
            return null;
        }

        var sx = pointX / scale;
        var sy = pointY / scale;
        if (sx < 0 || sy < 0 || sx > sourceWidth || sy > sourceHeight)
        {
            return null;
        }

        return (Math.Clamp((int)sx, 0, sourceWidth - 1), Math.Clamp((int)sy, 0, sourceHeight - 1));
    }

    /// <summary>Like <see cref="PointToSource"/> but always returns an in-bounds pixel (never null) — used
    /// while dragging so a drag past the edge clamps to the border.</summary>
    public static (int X, int Y) ClampToSource(double pointX, double pointY, double scale, int sourceWidth, int sourceHeight)
    {
        if (scale <= 0 || sourceWidth <= 0 || sourceHeight <= 0)
        {
            return (0, 0);
        }

        var sx = Math.Clamp((int)(pointX / scale), 0, sourceWidth - 1);
        var sy = Math.Clamp((int)(pointY / scale), 0, sourceHeight - 1);
        return (sx, sy);
    }

    /// <summary>Maps a source coordinate to a display position on the <c>source × scale</c> image.</summary>
    public static (double X, double Y) SourceToDisplay(double sourceX, double sourceY, double scale)
        => (sourceX * scale, sourceY * scale);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~ViewportTransformTests"`
Expected: PASS (11 tests).

- [ ] **Step 5: Commit**

```bash
git add AdbUi.Theme/Controls/ViewportTransform.cs AdbUi.Theme.Tests/Controls/ViewportTransformTests.cs
git commit -m "feat(theme): add ViewportTransform zoom/pan geometry

Claude-Session: https://claude.ai/code/session_01VTaqqf4mLUpmRnYV2HVTay"
```

---

## Task 2: `ZoomPanImageHost` control

**Files:**
- Create: `AdbUi.Theme/Controls/ImagePointerEventArgs.cs`
- Create: `AdbUi.Theme/Controls/ZoomPanImageHost.xaml`
- Create: `AdbUi.Theme/Controls/ZoomPanImageHost.xaml.cs`

No unit tests (WPF interaction control); verified by a solution build here and visually at the end.

- [ ] **Step 1: Create the pointer event args**

Create `AdbUi.Theme/Controls/ImagePointerEventArgs.cs`:

```csharp
namespace AdbUi.Theme.Controls;

/// <summary>A left-button pointer event on the image, mapped to source pixels.</summary>
public sealed class ImagePointerEventArgs : EventArgs
{
    public ImagePointerEventArgs(int sourceX, int sourceY, bool insideImage)
    {
        SourceX = sourceX;
        SourceY = sourceY;
        InsideImage = insideImage;
    }

    /// <summary>Source-pixel X (always in-bounds; clamped when the raw point was outside the image).</summary>
    public int SourceX { get; }

    /// <summary>Source-pixel Y (always in-bounds; clamped when the raw point was outside the image).</summary>
    public int SourceY { get; }

    /// <summary>True when the raw pointer position was inside the image (not the centered margin / past the edge).</summary>
    public bool InsideImage { get; }
}
```

- [ ] **Step 2: Create the XAML**

Create `AdbUi.Theme/Controls/ZoomPanImageHost.xaml`:

```xml
<UserControl x:Class="AdbUi.Theme.Controls.ZoomPanImageHost"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Focusable="True">
    <DockPanel>
        <Border DockPanel.Dock="Top" Background="{DynamicResource PanelBackgroundBrush}" Padding="6,4">
            <StackPanel Orientation="Horizontal">
                <Button Content="&#8722;" Width="28" Click="OnZoomOut" ToolTip="Zoom out (-)"/>
                <TextBlock x:Name="ZoomLabel" MinWidth="52" TextAlignment="Center" VerticalAlignment="Center"
                           Margin="4,0" Foreground="{DynamicResource PrimaryTextBrush}" Text="100%"/>
                <Button Content="+" Width="28" Click="OnZoomIn" ToolTip="Zoom in (+)"/>
                <Button Content="Fit" Padding="8,0" Margin="8,0,0,0" Click="OnFit" ToolTip="Fit to window (0)"/>
                <Button Content="100%" Padding="8,0" Margin="4,0,0,0" Click="OnActualSize" ToolTip="Actual size"/>
            </StackPanel>
        </Border>
        <ScrollViewer x:Name="Scroller"
                      HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Auto"
                      Background="{DynamicResource WindowBackgroundBrush}">
            <Grid x:Name="ContentHost" HorizontalAlignment="Center" VerticalAlignment="Center">
                <Image x:Name="FrameImage" Stretch="Fill" Cursor="Cross"
                       HorizontalAlignment="Left" VerticalAlignment="Top"
                       RenderOptions.BitmapScalingMode="NearestNeighbor"/>
                <Canvas x:Name="Overlay" IsHitTestVisible="False"
                        HorizontalAlignment="Left" VerticalAlignment="Top"/>
            </Grid>
        </ScrollViewer>
    </DockPanel>
</UserControl>
```

- [ ] **Step 3: Create the code-behind**

Create `AdbUi.Theme/Controls/ZoomPanImageHost.xaml.cs`:

```csharp
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace AdbUi.Theme.Controls;

/// <summary>A zoomable/pannable image host with an overlay. Consumers set a <see cref="BitmapSource"/> and
/// receive pointer events already mapped to source pixels; overlay marks are placed in source-pixel space
/// and reproject on zoom. Wheel zooms toward the cursor, middle-drag pans, and a toolbar offers −/%/+/Fit/100%.</summary>
public partial class ZoomPanImageHost : UserControl
{
    private readonly record struct DotMark(double SourceX, double SourceY, Color Stroke, Color Fill);

    private readonly List<DotMark> _dots = new();
    private (int X, int Y, int W, int H)? _previewRect;

    private int _sourceWidth;
    private int _sourceHeight;
    private double _scale = 1.0;
    private bool _userAdjusted;

    private bool _panning;
    private Point _panStart;
    private double _panStartH;
    private double _panStartV;

    public ZoomPanImageHost()
    {
        InitializeComponent();

        Loaded += (_, _) => { if (!_userAdjusted) Fit(); };
        Scroller.SizeChanged += (_, _) => { if (!_userAdjusted) Fit(); };

        Scroller.PreviewMouseWheel += OnPreviewMouseWheel;
        Scroller.PreviewMouseDown += OnPreviewMouseDown;
        Scroller.PreviewMouseMove += OnPreviewMouseMove;
        Scroller.PreviewMouseUp += OnPreviewMouseUp;

        FrameImage.MouseLeftButtonDown += OnImageLeftDown;
        FrameImage.MouseMove += OnImageMove;
        FrameImage.MouseLeftButtonUp += OnImageLeftUp;

        KeyDown += OnKeyDown;
    }

    /// <summary>Fired on a left-button press/move/release over the image. Coordinates are source pixels
    /// (always in-bounds); <see cref="ImagePointerEventArgs.InsideImage"/> is false when the raw point was
    /// outside the image (e.g. dragged past the edge, or a click in the centered margin).</summary>
    public event EventHandler<ImagePointerEventArgs>? ImagePointerDown;

    public event EventHandler<ImagePointerEventArgs>? ImagePointerMove;

    public event EventHandler<ImagePointerEventArgs>? ImagePointerUp;

    /// <summary>Sets the frame to display and fits it to the viewport. Clears any overlay.</summary>
    public void SetImage(BitmapSource image)
    {
        ArgumentNullException.ThrowIfNull(image);
        FrameImage.Source = image;
        _sourceWidth = image.PixelWidth;
        _sourceHeight = image.PixelHeight;
        _dots.Clear();
        _previewRect = null;
        _userAdjusted = false;
        ApplyScale(_scale);
        Fit();
    }

    /// <summary>Removes all overlay dots and the preview rectangle.</summary>
    public void ClearOverlay()
    {
        _dots.Clear();
        _previewRect = null;
        RedrawOverlay();
    }

    /// <summary>Adds a constant-size ring marker centered on a source pixel.</summary>
    public void AddDot(double sourceX, double sourceY, Color stroke, Color fill)
    {
        _dots.Add(new DotMark(sourceX, sourceY, stroke, fill));
        RedrawOverlay();
    }

    /// <summary>Shows a single transient selection rectangle (source coords) that scales with zoom.</summary>
    public void SetPreviewRect(int x, int y, int width, int height)
    {
        _previewRect = (x, y, width, height);
        RedrawOverlay();
    }

    /// <summary>Hides the preview rectangle.</summary>
    public void ClearPreviewRect()
    {
        _previewRect = null;
        RedrawOverlay();
    }

    /// <summary>Scales the image to fit the current viewport.</summary>
    public void Fit()
    {
        if (_sourceWidth <= 0)
        {
            return;
        }

        var vw = Scroller.ViewportWidth;
        var vh = Scroller.ViewportHeight;
        if (vw <= 0 || vh <= 0)
        {
            return;
        }

        _userAdjusted = false;
        ApplyScale(ViewportTransform.FitScale(vw, vh, _sourceWidth, _sourceHeight));
    }

    /// <summary>Sets an explicit zoom (1.0 = 100%).</summary>
    public void SetScale(double scale)
    {
        _userAdjusted = true;
        ApplyScale(scale);
    }

    private void ApplyScale(double newScale)
    {
        _scale = ViewportTransform.ClampScale(newScale);
        var w = _sourceWidth * _scale;
        var h = _sourceHeight * _scale;
        FrameImage.Width = w;
        FrameImage.Height = h;
        Overlay.Width = w;
        Overlay.Height = h;
        RenderOptions.SetBitmapScalingMode(
            FrameImage, _scale >= 1.0 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.Linear);
        ZoomLabel.Text = $"{Math.Round(_scale * 100)}%";
        RedrawOverlay();
    }

    private void RedrawOverlay()
    {
        Overlay.Children.Clear();

        foreach (var d in _dots)
        {
            // Center the ring on the middle of the source pixel.
            var (cx, cy) = ViewportTransform.SourceToDisplay(d.SourceX + 0.5, d.SourceY + 0.5, _scale);
            var ring = new Ellipse
            {
                Width = 14,
                Height = 14,
                StrokeThickness = 2,
                Stroke = new SolidColorBrush(d.Stroke),
                Fill = new SolidColorBrush(d.Fill),
            };
            Canvas.SetLeft(ring, cx - 7);
            Canvas.SetTop(ring, cy - 7);
            Overlay.Children.Add(ring);
        }

        if (_previewRect is (int rx, int ry, int rw, int rh))
        {
            var (dx, dy) = ViewportTransform.SourceToDisplay(rx, ry, _scale);
            var box = new Rectangle
            {
                Width = rw * _scale,
                Height = rh * _scale,
                Stroke = Brushes.Lime,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0xFF, 0x00)),
            };
            Canvas.SetLeft(box, dx);
            Canvas.SetTop(box, dy);
            Overlay.Children.Add(box);
        }
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_sourceWidth <= 0)
        {
            return;
        }

        _userAdjusted = true;
        var cursor = e.GetPosition(Scroller);
        var oldScale = _scale;
        var newScale = ViewportTransform.StepScale(oldScale, e.Delta / 120);
        e.Handled = true;
        if (newScale == oldScale)
        {
            return;
        }

        ApplyScale(newScale);
        Scroller.UpdateLayout();
        Scroller.ScrollToHorizontalOffset(
            ViewportTransform.ZoomToCursorOffset(Scroller.HorizontalOffset, cursor.X, oldScale, newScale));
        Scroller.ScrollToVerticalOffset(
            ViewportTransform.ZoomToCursorOffset(Scroller.VerticalOffset, cursor.Y, oldScale, newScale));
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle)
        {
            return;
        }

        _panning = true;
        _panStart = e.GetPosition(Scroller);
        _panStartH = Scroller.HorizontalOffset;
        _panStartV = Scroller.VerticalOffset;
        Scroller.CaptureMouse();
        Cursor = Cursors.ScrollAll;
        e.Handled = true;
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_panning)
        {
            return;
        }

        var p = e.GetPosition(Scroller);
        Scroller.ScrollToHorizontalOffset(_panStartH - (p.X - _panStart.X));
        Scroller.ScrollToVerticalOffset(_panStartV - (p.Y - _panStart.Y));
        e.Handled = true;
    }

    private void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_panning || e.ChangedButton != MouseButton.Middle)
        {
            return;
        }

        _panning = false;
        Scroller.ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
        e.Handled = true;
    }

    private void OnImageLeftDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        FrameImage.CaptureMouse();
        Raise(ImagePointerDown, e);
        e.Handled = true;
    }

    private void OnImageMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            Raise(ImagePointerMove, e);
        }
    }

    private void OnImageLeftUp(object sender, MouseButtonEventArgs e)
    {
        FrameImage.ReleaseMouseCapture();
        Raise(ImagePointerUp, e);
        e.Handled = true;
    }

    private void Raise(EventHandler<ImagePointerEventArgs>? handler, MouseEventArgs e)
    {
        if (handler is null)
        {
            return;
        }

        var p = e.GetPosition(FrameImage);
        var inside = ViewportTransform.PointToSource(p.X, p.Y, _scale, _sourceWidth, _sourceHeight) is not null;
        var (sx, sy) = ViewportTransform.ClampToSource(p.X, p.Y, _scale, _sourceWidth, _sourceHeight);
        handler(this, new ImagePointerEventArgs(sx, sy, inside));
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.OemPlus or Key.Add:
                SetScale(ViewportTransform.StepScale(_scale, 1));
                e.Handled = true;
                break;
            case Key.OemMinus or Key.Subtract:
                SetScale(ViewportTransform.StepScale(_scale, -1));
                e.Handled = true;
                break;
            case Key.D0 or Key.NumPad0:
                Fit();
                e.Handled = true;
                break;
        }
    }

    private void OnZoomOut(object sender, RoutedEventArgs e) => SetScale(ViewportTransform.StepScale(_scale, -1));

    private void OnZoomIn(object sender, RoutedEventArgs e) => SetScale(ViewportTransform.StepScale(_scale, 1));

    private void OnFit(object sender, RoutedEventArgs e) => Fit();

    private void OnActualSize(object sender, RoutedEventArgs e) => SetScale(1.0);
}
```

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build ADB.slnx`
Expected: Build succeeded (0 errors).

- [ ] **Step 5: Commit**

```bash
git add AdbUi.Theme/Controls/ImagePointerEventArgs.cs AdbUi.Theme/Controls/ZoomPanImageHost.xaml AdbUi.Theme/Controls/ZoomPanImageHost.xaml.cs
git commit -m "feat(theme): add ZoomPanImageHost control

Claude-Session: https://claude.ai/code/session_01VTaqqf4mLUpmRnYV2HVTay"
```

---

## Task 3: Shared `FrameImageSource` helper (BotBuilder)

**Files:**
- Create: `BotBuilder/FrameImageSource.cs`

- [ ] **Step 1: Create the helper**

Create `BotBuilder/FrameImageSource.cs`:

```csharp
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;

namespace BotBuilder;

/// <summary>Bridges a System.Drawing.Bitmap capture to a frozen WPF BitmapSource (via an in-memory PNG), so
/// callers can dispose the source Bitmap immediately. Shared by the frame-picker dialogs.</summary>
internal static class FrameImageSource
{
    public static BitmapSource ToImageSource(Bitmap bitmap)
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

- [ ] **Step 2: Build**

Run: `dotnet build BotBuilder/BotBuilder.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add BotBuilder/FrameImageSource.cs
git commit -m "refactor(builder): add shared FrameImageSource helper for picker dialogs

Claude-Session: https://claude.ai/code/session_01VTaqqf4mLUpmRnYV2HVTay"
```

---

## Task 4: Adopt in `CoordinatePickerDialog`

**Files:**
- Modify: `BotBuilder/CoordinatePickerDialog.xaml`
- Modify: `BotBuilder/CoordinatePickerDialog.xaml.cs`

- [ ] **Step 1: Replace the XAML**

Overwrite `BotBuilder/CoordinatePickerDialog.xaml`:

```xml
<Window x:Class="BotBuilder.CoordinatePickerDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:controls="clr-namespace:AdbUi.Theme.Controls;assembly=AdbUi.Theme"
        Title="Pick coordinates" Height="700" Width="900"
        WindowStartupLocation="CenterOwner" Background="{DynamicResource WindowBackgroundBrush}">
    <DockPanel>
        <Border DockPanel.Dock="Top" Background="{DynamicResource PanelBackgroundBrush}" Padding="10,6">
            <DockPanel>
                <Button DockPanel.Dock="Right" Content="Cancel" Padding="12,2" Click="OnCancel" />
                <TextBlock x:Name="PromptText" Foreground="{DynamicResource PrimaryTextBrush}" FontSize="14" VerticalAlignment="Center" />
            </DockPanel>
        </Border>
        <controls:ZoomPanImageHost x:Name="Viewer" />
    </DockPanel>
</Window>
```

- [ ] **Step 2: Replace the code-behind**

Overwrite `BotBuilder/CoordinatePickerDialog.xaml.cs`:

```csharp
using System.Collections.Generic;
using System.Drawing;
using System.Windows;
using System.Windows.Media;
using AdbUi.Theme.Controls;
using BotBuilder.Core.Picker;

namespace BotBuilder;

public partial class CoordinatePickerDialog : Window
{
    private readonly CoordinatePickerViewModel _vm;

    public CoordinatePickerDialog(CoordinatePickerViewModel vm, Bitmap frame)
    {
        InitializeComponent();
        _vm = vm;
        Viewer.SetImage(FrameImageSource.ToImageSource(frame));
        Viewer.ImagePointerDown += OnPointerDown;
        PromptText.Text = _vm.CurrentPrompt;
    }

    /// <summary>The collected (XKey, YKey, X, Y) write-back tuples — valid after the dialog returns true.</summary>
    public IReadOnlyList<(string XKey, string YKey, int X, int Y)> Results => _vm.Results();

    private void OnPointerDown(object? sender, ImagePointerEventArgs e)
    {
        if (!e.InsideImage)
        {
            return; // clicked outside the image (centered margin) — ignore
        }

        _vm.RecordClick(e.SourceX, e.SourceY);
        Viewer.AddDot(e.SourceX, e.SourceY, Colors.Lime, Color.FromArgb(80, 0, 255, 0));
        PromptText.Text = _vm.CurrentPrompt;

        if (_vm.IsComplete)
        {
            DialogResult = true;
            Close();
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build BotBuilder/BotBuilder.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add BotBuilder/CoordinatePickerDialog.xaml BotBuilder/CoordinatePickerDialog.xaml.cs
git commit -m "feat(builder): zoom/pan in the coordinate picker

Claude-Session: https://claude.ai/code/session_01VTaqqf4mLUpmRnYV2HVTay"
```

---

## Task 5: Adopt in `ColorDropperDialog`

**Files:**
- Modify: `BotBuilder/ColorDropperDialog.xaml`
- Modify: `BotBuilder/ColorDropperDialog.xaml.cs`

- [ ] **Step 1: Replace the XAML**

Overwrite `BotBuilder/ColorDropperDialog.xaml`:

```xml
<Window x:Class="BotBuilder.ColorDropperDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:controls="clr-namespace:AdbUi.Theme.Controls;assembly=AdbUi.Theme"
        Title="Pick a colour" Height="700" Width="900"
        WindowStartupLocation="CenterOwner" Background="{DynamicResource WindowBackgroundBrush}">
    <DockPanel>
        <Border DockPanel.Dock="Top" Background="{DynamicResource PanelBackgroundBrush}" Padding="10,6">
            <DockPanel>
                <Button DockPanel.Dock="Right" Content="Cancel" Padding="12,2" Click="OnCancel" />
                <TextBlock x:Name="PromptText" Foreground="{DynamicResource PrimaryTextBrush}" FontSize="14"
                           VerticalAlignment="Center" Text="Click a pixel to sample its colour." />
            </DockPanel>
        </Border>
        <controls:ZoomPanImageHost x:Name="Viewer" />
    </DockPanel>
</Window>
```

- [ ] **Step 2: Replace the code-behind**

Overwrite `BotBuilder/ColorDropperDialog.xaml.cs`:

```csharp
using System.Drawing;
using System.Windows;
using System.Windows.Media;
using AdbUi.Theme.Controls;
using BotBuilder.Core.Picker;

namespace BotBuilder;

public partial class ColorDropperDialog : Window
{
    private readonly Bitmap _frame;

    public ColorDropperDialog(Bitmap frame)
    {
        InitializeComponent();
        _frame = frame;
        Viewer.SetImage(FrameImageSource.ToImageSource(frame));
        Viewer.ImagePointerDown += OnPointerDown;
    }

    /// <summary>The sampled colour as <c>#RRGGBB</c> — valid after the dialog returns true.</summary>
    public string? PickedHex { get; private set; }

    private void OnPointerDown(object? sender, ImagePointerEventArgs e)
    {
        if (!e.InsideImage)
        {
            return; // clicked outside the image (centered margin) — ignore
        }

        var c = _frame.GetPixel(e.SourceX, e.SourceY);
        PickedHex = ColorHex.ToHex(c.R, c.G, c.B);
        Viewer.AddDot(e.SourceX, e.SourceY, Colors.White, Color.FromRgb(c.R, c.G, c.B));
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build BotBuilder/BotBuilder.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add BotBuilder/ColorDropperDialog.xaml BotBuilder/ColorDropperDialog.xaml.cs
git commit -m "feat(builder): zoom/pan in the colour dropper

Claude-Session: https://claude.ai/code/session_01VTaqqf4mLUpmRnYV2HVTay"
```

---

## Task 6: Adopt in `RegionPickerDialog`

**Files:**
- Modify: `BotBuilder/RegionPickerDialog.xaml`
- Modify: `BotBuilder/RegionPickerDialog.xaml.cs`

- [ ] **Step 1: Replace the XAML**

Overwrite `BotBuilder/RegionPickerDialog.xaml`:

```xml
<Window x:Class="BotBuilder.RegionPickerDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:controls="clr-namespace:AdbUi.Theme.Controls;assembly=AdbUi.Theme"
        Title="Pick region" Height="700" Width="900"
        WindowStartupLocation="CenterOwner" Background="{DynamicResource WindowBackgroundBrush}">
    <DockPanel>
        <Border DockPanel.Dock="Top" Background="{DynamicResource PanelBackgroundBrush}" Padding="10,6">
            <DockPanel>
                <Button DockPanel.Dock="Right" Content="Cancel" Padding="12,2" Click="OnCancel" />
                <TextBlock Text="Drag a box around the region" Foreground="{DynamicResource PrimaryTextBrush}" FontSize="14" VerticalAlignment="Center" />
            </DockPanel>
        </Border>
        <controls:ZoomPanImageHost x:Name="Viewer" />
    </DockPanel>
</Window>
```

- [ ] **Step 2: Replace the code-behind**

Overwrite `BotBuilder/RegionPickerDialog.xaml.cs`:

```csharp
using System.Drawing;
using System.Windows;
using AdbUi.Theme.Controls;
using BotBuilder.Core.Picker;

namespace BotBuilder;

public partial class RegionPickerDialog : Window
{
    private readonly int _sourceWidth;
    private readonly int _sourceHeight;
    private bool _dragging;
    private int _startX;
    private int _startY;

    public RegionPickerDialog(Bitmap frame)
    {
        InitializeComponent();
        _sourceWidth = frame.Width;
        _sourceHeight = frame.Height;
        Viewer.SetImage(FrameImageSource.ToImageSource(frame));
        Viewer.ImagePointerDown += OnDown;
        Viewer.ImagePointerMove += OnMove;
        Viewer.ImagePointerUp += OnUp;
    }

    /// <summary>The chosen region in source pixels (X, Y, Width, Height); valid after the dialog returns true.</summary>
    public (int X, int Y, int Width, int Height)? Region { get; private set; }

    private void OnDown(object? sender, ImagePointerEventArgs e)
    {
        _dragging = true;
        _startX = e.SourceX;
        _startY = e.SourceY;
        Viewer.SetPreviewRect(e.SourceX, e.SourceY, 0, 0);
    }

    private void OnMove(object? sender, ImagePointerEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        var r = RegionSelection.FromCorners(_startX, _startY, e.SourceX, e.SourceY, _sourceWidth, _sourceHeight);
        Viewer.SetPreviewRect(r.X, r.Y, r.Width, r.Height);
    }

    private void OnUp(object? sender, ImagePointerEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        var r = RegionSelection.FromCorners(_startX, _startY, e.SourceX, e.SourceY, _sourceWidth, _sourceHeight);
        if (r.Width <= 0 || r.Height <= 0)
        {
            Viewer.ClearPreviewRect();
            return; // degenerate — let the user re-drag
        }

        Region = r;
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build BotBuilder/BotBuilder.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add BotBuilder/RegionPickerDialog.xaml BotBuilder/RegionPickerDialog.xaml.cs
git commit -m "feat(builder): zoom/pan in the region picker

Claude-Session: https://claude.ai/code/session_01VTaqqf4mLUpmRnYV2HVTay"
```

---

## Task 7: Adopt in BotCapture `RegionSelectView`

**Files:**
- Modify: `BotCapture/Views/RegionSelectView.xaml`
- Modify: `BotCapture/Views/RegionSelectView.xaml.cs`

- [ ] **Step 1: Replace the XAML**

Overwrite `BotCapture/Views/RegionSelectView.xaml`:

```xml
<UserControl x:Class="BotCapture.Views.RegionSelectView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:controls="clr-namespace:AdbUi.Theme.Controls;assembly=AdbUi.Theme">
    <DockPanel Margin="8">
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0,0,0,8">
            <TextBlock Text="Drag to select a region, then Confirm." VerticalAlignment="Center" />
            <Button Content="Confirm Region →" Click="OnConfirm" Width="140" Margin="12,0,0,0" />
            <Button Content="← Back" Click="OnBack" Width="80" Margin="8,0,0,0" />
        </StackPanel>
        <Border BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1">
            <controls:ZoomPanImageHost x:Name="Viewer" />
        </Border>
    </DockPanel>
</UserControl>
```

- [ ] **Step 2: Replace the code-behind**

Overwrite `BotCapture/Views/RegionSelectView.xaml.cs`:

```csharp
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using AdbUi.Theme.Controls;
using BotCapture.Core;

namespace BotCapture.Views;

public partial class RegionSelectView : UserControl
{
    private bool _dragging;
    private int _startX;
    private int _startY;

    public RegionSelectView()
    {
        InitializeComponent();
        Viewer.ImagePointerDown += OnDown;
        Viewer.ImagePointerMove += OnMove;
        Viewer.ImagePointerUp += OnUp;
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
        Viewer.SetImage(BitmapInterop.ToImageSource(vm.Source));
        Viewer.ClearOverlay();
        _dragging = false;
    }

    private void OnDown(object? sender, ImagePointerEventArgs e)
    {
        if (Vm is null)
        {
            return;
        }

        _dragging = true;
        _startX = e.SourceX;
        _startY = e.SourceY;
        UpdateSelection(e.SourceX, e.SourceY);
    }

    private void OnMove(object? sender, ImagePointerEventArgs e)
    {
        if (!_dragging || Vm is null)
        {
            return;
        }

        UpdateSelection(e.SourceX, e.SourceY);
    }

    private void OnUp(object? sender, ImagePointerEventArgs e)
    {
        if (!_dragging || Vm is null)
        {
            return;
        }

        _dragging = false;
        UpdateSelection(e.SourceX, e.SourceY);
    }

    private void UpdateSelection(int curX, int curY)
    {
        if (Vm is null)
        {
            return;
        }

        var x = Math.Min(_startX, curX);
        var y = Math.Min(_startY, curY);
        var w = Math.Abs(curX - _startX);
        var h = Math.Abs(curY - _startY);
        Vm.Selection = new Rectangle(x, y, w, h);
        Viewer.SetPreviewRect(x, y, w, h);
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (Vm is null)
        {
            return;
        }

        RegionConfirmed?.Invoke(this, Vm.Crop());
    }

    private void OnBack(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
}
```

- [ ] **Step 3: Build**

Run: `dotnet build BotCapture/BotCapture.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add BotCapture/Views/RegionSelectView.xaml BotCapture/Views/RegionSelectView.xaml.cs
git commit -m "feat(capture): zoom/pan in the template region selector

Claude-Session: https://claude.ai/code/session_01VTaqqf4mLUpmRnYV2HVTay"
```

---

## Task 8: Remove the dead `CoordinateMapping`

**Files:**
- Delete: `BotBuilder.Core/Picker/CoordinateMapping.cs`
- Delete: `BotBuilder.Core.Tests/Picker/CoordinateMappingTests.cs`

- [ ] **Step 1: Confirm there are no remaining callers**

Run: `git grep -n "CoordinateMapping"`
Expected: matches only in the two files about to be deleted (and, harmlessly, this plan/spec under `Docs/`). If any `.cs` under `AdbCore`, `BotBuilder`, or `BotCapture` still references it, stop and migrate that caller first.

- [ ] **Step 2: Delete the files**

```bash
git rm BotBuilder.Core/Picker/CoordinateMapping.cs BotBuilder.Core.Tests/Picker/CoordinateMappingTests.cs
```

- [ ] **Step 3: Build to confirm nothing broke**

Run: `dotnet build ADB.slnx`
Expected: Build succeeded (0 errors).

- [ ] **Step 4: Commit**

```bash
git commit -m "refactor: remove dead CoordinateMapping (superseded by ViewportTransform)

Claude-Session: https://claude.ai/code/session_01VTaqqf4mLUpmRnYV2HVTay"
```

---

## Task 9: Full build + test verification (end of Slice 1)

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build ADB.slnx`
Expected: Build succeeded (0 errors, 0 warnings introduced by this work).

- [ ] **Step 2: Run all tests**

Run: `dotnet test ADB.slnx`
Expected: All tests pass, including `ViewportTransformTests` (11).

- [ ] **Step 3: Manual visual verification (user)**

Launch BotBuilder (`dotnet run --project BotBuilder`) and BotCapture (`dotnet run --project BotCapture`) and confirm on each of the four tools:
- Mouse wheel zooms toward the cursor; the `%` label updates; `Fit` and `100%` work; `+`/`−`/`0` keys work.
- Middle-drag pans; scrollbars appear when zoomed past the viewport.
- Coordinate picker drops a marker on the clicked pixel; colour dropper samples the correct pixel (zoom in on a 1px feature to confirm precision); region picker and BotCapture region selector draw a rubber-band that stays glued to the image through zoom and produce the correct source rectangle/crop.

> This is the end of the Slice-1 dev PR. Do NOT proceed to Task 10 until the user has verified and the PR is ready.

---

## Task 10: Documentation sync (Slice 2 — follow-up PR)

**Files:**
- Modify: `CLAUDE.md`
- Modify: `README.md`
- Modify: `../ADB.wiki/*` (the picker / BotCapture reference page(s))

- [ ] **Step 1: Update `CLAUDE.md`**

Where the coordinate picker, colour dropper (Measure Bar / Get Pixel entries), and BotCapture region capture are described, add a short, accurate note that these frame-picker tools support zoom & pan (mouse wheel zooms toward the cursor, middle-drag pans, with a −/%/+/Fit/100% toolbar and +/−/0 keys). Keep the engineering-reference tone plain.

- [ ] **Step 2: Update `README.md`**

Find where the capture / picker tools are described and add the zoom/pan capability, keeping the goblin voice and every fact accurate (wheel = zoom, middle-drag = pan, Fit/100%). Do not imply any capability beyond what the control provides.

- [ ] **Step 3: Update the wiki**

In the sibling clone `../ADB.wiki`, edit the page(s) covering the coordinate picker / region picker / colour dropper / BotCapture region selection to document zoom & pan. Then:

```bash
cd ../ADB.wiki
git add -A
git commit -m "Docs: zoom & pan in the frame-picker tools"
git push
cd ../ADB
```

- [ ] **Step 4: Re-read the doc edits against the code**

Confirm each doc claim (gestures, toolbar buttons, keys, bounds) matches the shipped `ZoomPanImageHost` behavior. Downgrade any unbacked claim to a TODO or fix it.

- [ ] **Step 5: Commit the main-repo doc changes**

```bash
git add CLAUDE.md README.md
git commit -m "docs: zoom & pan in the frame-picker tools

Claude-Session: https://claude.ai/code/session_01VTaqqf4mLUpmRnYV2HVTay"
```

---

## Self-Review Notes

- **Spec coverage:** `ViewportTransform` (Task 1) ✓; `ZoomPanImageHost` with wheel-zoom/middle-drag/toolbar/keys and source-pixel API (Task 2) ✓; NearestNeighbor ≥1× / Linear <1× (Task 2 `ApplyScale`) ✓; overlay AddDot/SetPreviewRect reproject on zoom (Task 2 `RedrawOverlay`) ✓; all four surfaces adopt it (Tasks 4–7) ✓; dedupe `ToImageSource` (Task 3) ✓; remove dead `CoordinateMapping` (Task 8) ✓; DPI unchanged (no capture code touched) ✓; tests (Task 1) ✓; docs sync (Task 10) ✓.
- **Type consistency:** `ImagePointerEventArgs(int, int, bool)` and its `SourceX`/`SourceY`/`InsideImage` members are used identically in Tasks 2, 4, 5, 6, 7. `SetImage`/`AddDot`/`SetPreviewRect`/`ClearPreviewRect`/`ClearOverlay`/`Fit`/`SetScale` signatures match between the control (Task 2) and every caller. `RegionSelection.FromCorners` returns `(int X, int Y, int Width, int Height)` — matches `RegionPickerDialog.Region`. `FrameImageSource.ToImageSource(Bitmap) → BitmapSource` and `BitmapInterop.ToImageSource(Bitmap) → BitmapImage` both satisfy `SetImage(BitmapSource)`.
- **Note on the `ToImageSource` dedupe:** kept as a BotBuilder-local helper (Task 3) rather than moved into `AdbUi.Theme`, because the conversion needs `System.Drawing.Bitmap` and `AdbUi.Theme` deliberately has no `System.Drawing` dependency. This refines the spec's "co-located with the control" wording without changing intent.
```
