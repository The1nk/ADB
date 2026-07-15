# Color Dropper Picker Implementation Plan — Slice 2b of the fast-reads effort

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.
>
> **WORKTREE PATHS:** runs in `C:\git\ADB\.claude\worktrees\color-dropper`. For ALL Write/Edit ops use that ABSOLUTE prefix (the Write tool default can land in the main checkout `C:\git\ADB`). After writing, `git -C C:/git/ADB/.claude/worktrees/color-dropper status` to confirm.

**Goal:** Let the user pick Measure Bar's **Fill** and **Empty** colours with an eyedropper — click a pixel on a captured frame — instead of typing hex.

**Architecture:** Mirror the existing **"Pick region…"** flow exactly. A `ColorDropperDialog` (WPF) is a near-clone of `CoordinatePickerDialog`: it displays a captured frame, and on a single click it maps the click to a source pixel (reusing the already-tested `CoordinateMapping.ToSourcePixel`), reads that pixel's colour, and returns its hex. `PropertiesViewModel.SupportsColorPicking` (mirroring `SupportsRegionPicking`) drives two "Pick fill/empty colour…" buttons in the properties panel; their handlers capture a frame via the existing `FrameCapturer` and write the picked hex into the Measure Bar `fillColor`/`emptyColor` config fields. The colours remain plain hex String fields (still editable by hand) — the dropper just populates them.

**Tech Stack:** C# / .NET 10, WPF. Builds on Slice 2a (Measure Bar: `BarMeasureCore.FillColorKey`/`EmptyColorKey`).

**Design doc:** `Docs/superpowers/specs/2026-07-14-fast-frame-reads-and-library-manager-design.md`

**Branch:** `worktree-color-dropper`, stacked on `worktree-measure-bar` (Slice 2a, PR #84). Task 1 is testable core; Tasks 2–3 are WPF (the user validates visually before merge); Task 4 is docs.

**Reuse (read these first):** `BotBuilder/CoordinatePickerDialog.xaml`(.cs) (the dialog to clone), `BotBuilder.Core/Picker/CoordinateMapping.cs` (`ToSourcePixel` — pure, tested), `BotBuilder.Core/Properties/PropertiesViewModel.cs` (`SupportsRegionPicking` at ~line 44, and the `OnPropertyChanged(nameof(SupportsRegionPicking))` in `Rebuild()` at ~line 207), `BotBuilder/MainWindow.xaml.cs` (`PickRegion_Click` at ~line 956; `FieldByKey`; `_frameCapturer`), `BotBuilder/MainWindow.xaml` (the "Pick region…" button at ~line 481).

---

### Task 1: `ColorHex` helper + `PropertiesViewModel.SupportsColorPicking`

**Files:**
- Create: `BotBuilder.Core/Picker/ColorHex.cs`
- Modify: `BotBuilder.Core/Properties/PropertiesViewModel.cs` (add `SupportsColorPicking` + raise it in `Rebuild`)
- Test: `BotBuilder.Core.Tests/Picker/ColorHexTests.cs`
- Test: `BotBuilder.Core.Tests/Properties/SupportsColorPickingTests.cs`

- [ ] **Step 1: Write the failing tests.** Create `BotBuilder.Core.Tests/Picker/ColorHexTests.cs`:

```csharp
using BotBuilder.Core.Picker;
using Xunit;

namespace BotBuilder.Core.Tests.Picker;

public class ColorHexTests
{
    [Theory]
    [InlineData(0, 0, 0, "#000000")]
    [InlineData(255, 255, 255, "#FFFFFF")]
    [InlineData(255, 0, 128, "#FF0080")]
    [InlineData(12, 34, 56, "#0C2238")]
    public void ToHex_FormatsUppercaseRRGGBB(int r, int g, int b, string expected)
    {
        Assert.Equal(expected, ColorHex.ToHex(r, g, b));
    }
}
```

Create `BotBuilder.Core.Tests/Properties/SupportsColorPickingTests.cs`:

```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests.Properties;

public class SupportsColorPickingTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    [Fact]
    public void True_ForMeasureBar_False_ForFindImage()
    {
        var editor = NewEditor();

        var measureBar = editor.AddNode("screen.measureBar", 0, 0);
        editor.Select(measureBar);
        Assert.True(editor.Properties.SupportsColorPicking);

        var findImage = editor.AddNode("screen.findImage", 0, 0);
        editor.Select(findImage);
        Assert.False(editor.Properties.SupportsColorPicking);
    }
}
```

Note: confirm the editor-selection API (`editor.Select(node)` and `editor.Properties`) against existing tests (grep `BotBuilder.Core.Tests` for `.Select(` / `.Properties.`); if the real API differs, match it. `AddNode(typeKey, x, y)` returns a `NodeViewModel`.)

- [ ] **Step 2: Run tests to verify they fail** — `dotnet test ADB.slnx --filter "FullyQualifiedName~ColorHexTests|FullyQualifiedName~SupportsColorPickingTests"` — expect FAIL (`ColorHex` missing; `SupportsColorPicking` missing).

- [ ] **Step 3: Implement.** Create `BotBuilder.Core/Picker/ColorHex.cs`:

```csharp
using System.Globalization;

namespace BotBuilder.Core.Picker;

/// <summary>Formats an R/G/B triple as an uppercase <c>#RRGGBB</c> hex string (the format Measure Bar's
/// fill/empty colour fields expect). Kept dependency-free (no System.Drawing) so it lives in BotBuilder.Core.</summary>
public static class ColorHex
{
    public static string ToHex(int r, int g, int b)
        => string.Create(CultureInfo.InvariantCulture, $"#{r:X2}{g:X2}{b:X2}");
}
```

Then in `BotBuilder.Core/Properties/PropertiesViewModel.cs`, add this property right after `SupportsRegionPicking` (mirror its shape; `BarMeasureCore` is in `AdbCore.Actions.BuiltIn`, already imported or add the using):

```csharp
    /// <summary>Whether the selected action exposes Measure Bar fill/empty colour fields the dropper can fill.</summary>
    public bool SupportsColorPicking =>
        Node is not null
        && _registry.TryGet(Node.TypeKey, out var def) && def is not null
        && def.ConfigFields.Any(f => f.Key == BarMeasureCore.FillColorKey);
```

And in the `Rebuild()` method, next to the existing `OnPropertyChanged(nameof(SupportsRegionPicking));`, add:

```csharp
        OnPropertyChanged(nameof(SupportsColorPicking));
```

- [ ] **Step 4: Run tests to verify they pass** — `dotnet test ADB.slnx --filter "FullyQualifiedName~ColorHexTests|FullyQualifiedName~SupportsColorPickingTests"` → PASS. Then `dotnet build ADB.slnx`.

- [ ] **Step 5: Commit**

```bash
git add BotBuilder.Core/Picker/ColorHex.cs BotBuilder.Core/Properties/PropertiesViewModel.cs BotBuilder.Core.Tests/Picker/ColorHexTests.cs BotBuilder.Core.Tests/Properties/SupportsColorPickingTests.cs
git commit -m "feat: ColorHex + PropertiesViewModel.SupportsColorPicking"
```

---

### Task 2: `ColorDropperDialog` (WPF)  [VISUAL — user validates]

**Files:**
- Create: `BotBuilder/ColorDropperDialog.xaml`
- Create: `BotBuilder/ColorDropperDialog.xaml.cs`

Read `BotBuilder/CoordinatePickerDialog.xaml`(.cs) first — this is a near-clone (single click samples a colour instead of collecting coordinates).

- [ ] **Step 1: Create `BotBuilder/ColorDropperDialog.xaml`** (same structure as CoordinatePickerDialog.xaml):

```xml
<Window x:Class="BotBuilder.ColorDropperDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
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
        <Grid x:Name="ImageHost" ClipToBounds="True">
            <Image x:Name="FrameImage" Stretch="Uniform" MouseLeftButtonDown="OnImageClick" Cursor="Cross" />
            <Canvas x:Name="MarkerCanvas" IsHitTestVisible="False" />
        </Grid>
    </DockPanel>
</Window>
```

- [ ] **Step 2: Create `BotBuilder/ColorDropperDialog.xaml.cs`** (mirror CoordinatePickerDialog.xaml.cs; single click → sample colour):

```csharp
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

public partial class ColorDropperDialog : Window
{
    private readonly Bitmap _frame;
    private readonly int _sourceWidth;
    private readonly int _sourceHeight;

    public ColorDropperDialog(Bitmap frame)
    {
        InitializeComponent();
        _frame = frame;
        _sourceWidth = frame.Width;
        _sourceHeight = frame.Height;
        FrameImage.Source = ToImageSource(frame);
    }

    /// <summary>The sampled colour as <c>#RRGGBB</c> — valid after the dialog returns true.</summary>
    public string? PickedHex { get; private set; }

    private void OnImageClick(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(FrameImage);
        var mapped = CoordinateMapping.ToSourcePixel(
            pos.X, pos.Y, FrameImage.ActualWidth, FrameImage.ActualHeight, _sourceWidth, _sourceHeight);
        if (mapped is not (int sx, int sy))
        {
            return; // clicked the letterbox margin — ignore
        }

        var c = _frame.GetPixel(sx, sy);
        PickedHex = ColorHex.ToHex(c.R, c.G, c.B);
        DrawMarker(pos, c);
        DialogResult = true;
        Close();
    }

    private void DrawMarker(System.Windows.Point at, Color sampled)
    {
        var dot = new Ellipse
        {
            Width = 14,
            Height = 14,
            Stroke = System.Windows.Media.Brushes.White,
            StrokeThickness = 2,
            Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(sampled.R, sampled.G, sampled.B)),
        };
        var origin = FrameImage.TranslatePoint(new System.Windows.Point(0, 0), MarkerCanvas);
        Canvas.SetLeft(dot, origin.X + at.X - 7);
        Canvas.SetTop(dot, origin.Y + at.Y - 7);
        MarkerCanvas.Children.Add(dot);
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

- [ ] **Step 3: Build.** `dotnet build ADB.slnx` — expect clean (0 warnings/errors). (No unit test — WPF dialog, validated visually by the user.)

- [ ] **Step 4: Commit**

```bash
git add BotBuilder/ColorDropperDialog.xaml BotBuilder/ColorDropperDialog.xaml.cs
git commit -m "feat: ColorDropperDialog (eyedropper on a captured frame)"
```

---

### Task 3: "Pick fill/empty colour…" buttons + handlers  [VISUAL — user validates]

**Files:**
- Modify: `BotBuilder/MainWindow.xaml` (two buttons near "Pick region…")
- Modify: `BotBuilder/MainWindow.xaml.cs` (two handlers + a shared helper)

- [ ] **Step 1: Add the buttons.** In `BotBuilder/MainWindow.xaml`, find the "Pick region…" button (bound `Visibility="{Binding SupportsRegionPicking, Converter={StaticResource BoolToVis}}"`). Immediately after it, add:

```xml
                            <Button Content="Pick fill colour…" Margin="0,4,0,0" Padding="6,3" HorizontalAlignment="Left"
                                    Click="PickFillColor_Click"
                                    Visibility="{Binding SupportsColorPicking, Converter={StaticResource BoolToVis}}" />

                            <Button Content="Pick empty colour…" Margin="0,4,0,0" Padding="6,3" HorizontalAlignment="Left"
                                    Click="PickEmptyColor_Click"
                                    Visibility="{Binding SupportsColorPicking, Converter={StaticResource BoolToVis}}" />
```

- [ ] **Step 2: Add the handlers.** In `BotBuilder/MainWindow.xaml.cs`, add near `PickRegion_Click` (mirror its target-resolve + capture flow):

```csharp
    private void PickFillColor_Click(object sender, RoutedEventArgs e)
        => PickColorInto(AdbCore.Actions.BuiltIn.BarMeasureCore.FillColorKey, "Pick fill colour");

    private void PickEmptyColor_Click(object sender, RoutedEventArgs e)
        => PickColorInto(AdbCore.Actions.BuiltIn.BarMeasureCore.EmptyColorKey, "Pick empty colour");

    private void PickColorInto(string fieldKey, string title)
    {
        var node = _editor.Properties.Node;
        if (node is null)
        {
            return;
        }

        var target = _editor.TargetBar.ResolveForNode(node.TargetId);
        if (target is null)
        {
            MessageBox.Show(
                "Add a target (Window or Android device) first, then pick a colour against it.",
                title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var frame = _frameCapturer.TryCapture(target.Type, target.Selector, out var error);
        if (frame is null)
        {
            MessageBox.Show(error ?? "Couldn't capture the target.", title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string? hex;
        bool confirmed;
        try
        {
            var dialog = new ColorDropperDialog(frame) { Owner = this };
            confirmed = dialog.ShowDialog() == true;
            hex = dialog.PickedHex;
        }
        finally
        {
            frame.Dispose();
        }

        if (confirmed && hex is not null && FieldByKey(fieldKey) is { } field)
        {
            field.Value = hex;
        }
    }
```

Confirm against the real code: `_editor.Properties.Node`, `_editor.TargetBar.ResolveForNode(node.TargetId)`, `_frameCapturer.TryCapture(type, selector, out error)`, and `FieldByKey(key)` returning a config-field VM with a settable `Value` — all used verbatim by the existing `PickRegion_Click`; match whatever it actually does (e.g. if `FieldByKey` lives on a different object, use that path). Setting `field.Value = hex` (a String field) writes the hex into the config and marks dirty exactly as the region picker's writes do.

- [ ] **Step 3: Build + test.** `dotnet build ADB.slnx` — clean. `dotnet test ADB.slnx` — all pass (no regressions; dialog/wiring validated visually by the user).

- [ ] **Step 4: Commit**

```bash
git add BotBuilder/MainWindow.xaml BotBuilder/MainWindow.xaml.cs
git commit -m "feat: Pick fill/empty colour buttons (Measure Bar eyedropper)"
```

---

### Task 4: Docs

**Files:**
- Modify: `CLAUDE.md`
- Modify: `README.md` (only if it describes Measure Bar colour entry)
- Wiki: `C:/git/ADB.wiki/Measure-Bar.md` — note the eyedropper (write only; do NOT commit/push)

- [ ] **Step 1: Full build + test.** `dotnet build ADB.slnx` (clean) and `dotnet test ADB.slnx` (all pass).

- [ ] **Step 2: CLAUDE.md.** In the Measure Bar description (the Key Modules row or nearby), add a short clause: fill/empty colours can be entered as hex **or picked with an eyedropper** — the properties panel's "Pick fill/empty colour…" buttons capture the target and let you click a pixel to sample its colour (`ColorDropperDialog`, reusing `CoordinateMapping`; buttons gated on `SupportsColorPicking`). Ground it in the code.

- [ ] **Step 3: README.md.** If Measure Bar's bullet mentions telling it the fill colour, add "(pick it with the eyedropper or type hex)" in the goblin voice. Otherwise no change (say so).

- [ ] **Step 4: Wiki (write only, no commit/push).** In `C:/git/ADB.wiki/Measure-Bar.md`, add a sentence to the fill/empty colour docs noting the "Pick fill/empty colour…" eyedropper buttons in the properties panel (capture target → click a pixel). Do NOT run git in `C:/git/ADB.wiki`.

- [ ] **Step 5: Commit the worktree docs**

```bash
git add CLAUDE.md README.md
git commit -m "docs: Measure Bar colour eyedropper"
```

---

## Self-Review

**Spec coverage (Slice 2b):** eyedropper for fill + empty colours → Tasks 2–3 (dialog + buttons). Reuse of the tested `CoordinateMapping` for click→pixel → Task 2. `SupportsColorPicking` gating (Measure Bar only) + hex formatting → Task 1 (tested). Docs → Task 4. Colours stay editable hex fields; the dropper just populates them.

**Placeholder scan:** none — full code in every code step; the spots that depend on exact existing API (`editor.Select`/`.Properties`, `_editor.Properties.Node`, `ResolveForNode`, `FieldByKey`, `_frameCapturer`) are called out with "match the real PickRegion_Click path."

**Type consistency:** `ColorHex.ToHex(int,int,int)`, `PropertiesViewModel.SupportsColorPicking`, `ColorDropperDialog(Bitmap)` with `string? PickedHex`, handlers `PickFillColor_Click`/`PickEmptyColor_Click` → `PickColorInto(fieldKey, title)` writing `FieldByKey(key).Value = hex` — consistent across tasks. Keys `BarMeasureCore.FillColorKey`/`EmptyColorKey` from Slice 2a.

## Execution Handoff
Task 1 testable; Tasks 2–3 are WPF (the user validates the eyedropper visually before merge). Stacked on Slice 2a (PR #84) → stacked PR.
