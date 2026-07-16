# Get Pixel Color coordinate picker + hex colour swatch — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** (1) Give **Get Pixel Color** (Screen + Android) a coordinate **Pick…** button reusing the existing zoomable coordinate picker; (2) show a small live colour **swatch** next to hex colour fields (Measure Bar Fill/Empty Colour).

**Architecture:** #1 is purely a registry add — `CoordinateFieldMap` drives `PropertiesViewModel.SupportsCoordinatePicking` and the existing `PickCoordinates_Click` handler, so adding the two Get-Pixel TypeKeys lights up the (already zoomable) picker. #2 adds an editor-only `ConfigFieldType.Color` (stored value stays the hex string — no `.bot` schema change), a pure `ColorHex.TryParse`, a thin WPF `HexToBrushConverter`, and a `FieldColor` template with a live swatch.

**Tech Stack:** .NET 10, WPF, C# nullable, xUnit.

---

## Task A: Backend + core (map, enum, colour parsing) — testable

**Files:**
- Modify: `BotBuilder.Core/Picker/CoordinateFieldMap.cs`
- Modify: `BotBuilder.Core.Tests/Picker/CoordinateFieldMapTests.cs`
- Modify: `AdbCore/Actions/ConfigFieldType.cs`
- Modify: `AdbCore/Actions/BuiltIn/BarMeasureCore.cs`
- Modify: `BotBuilder.Core/Picker/ColorHex.cs`
- Modify: `BotBuilder.Core.Tests/Picker/ColorHexTests.cs`

- [ ] **Step 1: Add Get Pixel Color to the coordinate map.** In `CoordinateFieldMap.cs`, inside the dictionary initializer (after the `input.mouseMove` line), add:

```csharp
            ["screen.getPixelColor"] = [new CoordinatePoint("x", "y", "Pixel")],
            ["android.getPixelColor"] = [new CoordinatePoint("x", "y", "Pixel")],
```

- [ ] **Step 2: Extend the map test.** In `CoordinateFieldMapTests.cs`, add:

```csharp
    [Theory]
    [InlineData("screen.getPixelColor")]
    [InlineData("android.getPixelColor")]
    public void GetPixelColor_HasOnePoint_XY_Pixel(string typeKey)
    {
        var points = CoordinateFieldMap.ForTypeKey(typeKey);
        Assert.True(CoordinateFieldMap.Supports(typeKey));
        var p = Assert.Single(points);
        Assert.Equal(("x", "y", "Pixel"), (p.XKey, p.YKey, p.Label));
    }
```

- [ ] **Step 3: Add the `Color` field type.** In `AdbCore/Actions/ConfigFieldType.cs`, add `Color,` to the enum (after `ImageTemplate,`):

```csharp
public enum ConfigFieldType
{
    String,
    MultilineString,
    Number,
    Boolean,
    Enum,
    FilePath,
    ImageTemplate,
    Color,
}
```

- [ ] **Step 4: Mark the Measure Bar colour fields as `Color`.** In `AdbCore/Actions/BuiltIn/BarMeasureCore.cs`, change the two colour field definitions' `Type` from `ConfigFieldType.String` to `ConfigFieldType.Color` (labels unchanged):

```csharp
        new ConfigField { Key = FillColorKey, Label = "Fill Color (hex)", Type = ConfigFieldType.Color },
        new ConfigField { Key = EmptyColorKey, Label = "Empty Color (hex)", Type = ConfigFieldType.Color },
```

- [ ] **Step 5: Add `ColorHex.TryParse` (pure).** In `BotBuilder.Core/Picker/ColorHex.cs`, add a parse method (inverse of `ToHex`). Accept an optional leading `#` and exactly 6 hex digits; reject anything else:

```csharp
    /// <summary>Parses a <c>#RRGGBB</c> (or <c>RRGGBB</c>) hex string into 0–255 R/G/B. Returns false for null,
    /// wrong length, or non-hex input. The inverse of <see cref="ToHex"/>.</summary>
    public static bool TryParse(string? hex, out (int R, int G, int B) rgb)
    {
        rgb = default;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var s = hex.Trim();
        if (s.StartsWith('#'))
        {
            s = s[1..];
        }

        if (s.Length != 6
            || !int.TryParse(s.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            || !int.TryParse(s.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            || !int.TryParse(s.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return false;
        }

        rgb = (r, g, b);
        return true;
    }
```

- [ ] **Step 6: Test `TryParse`.** In `ColorHexTests.cs`, add:

```csharp
    [Theory]
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("#FFFFFF", 255, 255, 255)]
    [InlineData("FF0080", 255, 0, 128)]
    [InlineData("  #0c2238  ", 12, 34, 56)]
    public void TryParse_ParsesValidHex(string hex, int r, int g, int b)
    {
        Assert.True(ColorHex.TryParse(hex, out var rgb));
        Assert.Equal((r, g, b), rgb);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#FFF")]
    [InlineData("#12345")]
    [InlineData("nothex!")]
    [InlineData("#GGGGGG")]
    public void TryParse_RejectsInvalid(string? hex)
    {
        Assert.False(ColorHex.TryParse(hex, out _));
    }
```

- [ ] **Step 7: Build + test.** Run:
  `cd "C:/git/ADB/.claude/worktrees/getpixel-picker-swatch" && dotnet build ADB.slnx` → 0 errors.
  `dotnet test ADB.slnx --filter "FullyQualifiedName~CoordinateFieldMapTests|FullyQualifiedName~ColorHexTests"` → all pass.

- [ ] **Step 8: Commit.**
```bash
git add BotBuilder.Core/Picker/CoordinateFieldMap.cs BotBuilder.Core.Tests/Picker/CoordinateFieldMapTests.cs AdbCore/Actions/ConfigFieldType.cs AdbCore/Actions/BuiltIn/BarMeasureCore.cs BotBuilder.Core/Picker/ColorHex.cs BotBuilder.Core.Tests/Picker/ColorHexTests.cs
git commit -m "feat: coordinate pick for Get Pixel Color + Color field type & hex parse

Claude-Session: https://claude.ai/code/session_01VTaqqf4mLUpmRnYV2HVTay"
```

---

## Task B: WPF — hex→brush converter, colour field template

**Files:**
- Modify: `BotBuilder/ValueConverters.cs`
- Modify: `BotBuilder/ConfigFieldTemplateSelector.cs`
- Modify: `BotBuilder/MainWindow.xaml`

- [ ] **Step 1: Add the converter.** Append to `BotBuilder/ValueConverters.cs` a converter that turns a hex string into a brush via the pure `ColorHex.TryParse` (invalid/empty → transparent). Add any missing usings (`System`, `System.Globalization`, `System.Windows.Data`, `System.Windows.Media`, `BotBuilder.Core.Picker`):

```csharp
/// <summary>Turns a #RRGGBB hex string into a SolidColorBrush for the colour-field swatch; invalid/empty → transparent.</summary>
public sealed class HexToBrushConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (BotBuilder.Core.Picker.ColorHex.TryParse(value?.ToString(), out var rgb))
        {
            return new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb((byte)rgb.R, (byte)rgb.G, (byte)rgb.B));
        }

        return System.Windows.Media.Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
```

- [ ] **Step 2: Add the selector slot.** In `BotBuilder/ConfigFieldTemplateSelector.cs`, add a `ColorTemplate` property and switch arm:

```csharp
    public DataTemplate? ColorTemplate { get; set; }
```
and in the `switch`, add (before the `_ => StringTemplate` default):
```csharp
            AdbCore.Actions.ConfigFieldType.Color => ColorTemplate,
```

- [ ] **Step 3: Register the converter + colour template in XAML.** In `BotBuilder/MainWindow.xaml`, in the same resources block as `TemplateImageConverter` (near the top, where `x:Key="TemplateImage"` is declared), add:

```xml
        <local:HexToBrushConverter x:Key="HexToBrush" />
```
Add a `FieldColor` DataTemplate next to `FieldString`:
```xml
        <DataTemplate x:Key="FieldColor">
            <StackPanel Margin="0,4">
                <TextBlock Text="{Binding Label}" FontSize="11" Foreground="{DynamicResource SecondaryTextBrush}" />
                <DockPanel>
                    <Border DockPanel.Dock="Right" Width="22" Height="22" Margin="6,0,0,0" CornerRadius="2"
                            BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1"
                            Background="{Binding Value, Converter={StaticResource HexToBrush}}" />
                    <TextBox Text="{Binding Value, UpdateSourceTrigger=PropertyChanged}" />
                </DockPanel>
            </StackPanel>
        </DataTemplate>
```
Wire it into the selector (add the attribute alongside the others):
```xml
            ColorTemplate="{StaticResource FieldColor}"
```

- [ ] **Step 4: Build.** `cd "C:/git/ADB/.claude/worktrees/getpixel-picker-swatch" && dotnet build ADB.slnx` → 0 errors.

- [ ] **Step 5: Commit.**
```bash
git add BotBuilder/ValueConverters.cs BotBuilder/ConfigFieldTemplateSelector.cs BotBuilder/MainWindow.xaml
git commit -m "feat(builder): live colour swatch next to hex colour fields

Claude-Session: https://claude.ai/code/session_01VTaqqf4mLUpmRnYV2HVTay"
```

---

## Task C: Docs (CLAUDE.md + README) + verify

- [ ] **Step 1: CLAUDE.md.** In the **Get Pixel Color** module row, note it now has a coordinate **Pick…** button (the zoomable picker). In the **Measure Bar** row / properties note, mention the Fill/Empty colour fields render as a `ConfigFieldType.Color` editor with a live hex swatch. Keep it plain/precise.
- [ ] **Step 2: README.md.** Where Get Pixel Color and Measure Bar are described (the arsenal), add (goblin voice, accurate) that Get Pixel Color has a **Pick…** coordinate button and hex colour fields show a live swatch.
- [ ] **Step 3: Full verify.** `dotnet build ADB.slnx` (0 errors) + `dotnet test ADB.slnx` (all pass).
- [ ] **Step 4: Commit** the docs.

Wiki pages (`Get-Pixel-Color.md`, `Measure-Bar.md`) are updated separately, coordinated with the in-flight wiki rewrite.
