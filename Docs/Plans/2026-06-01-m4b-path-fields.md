# M4b — File/Image Path Field Editors Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the Properties Panel (M4) by adding the **FilePath** (TextBox + Browse) and **ImagePath** (TextBox + Browse + preview thumbnail) config-field editors, per `Docs/Design/V1.md` §5.4.

**Architecture:** Purely the WPF shell — the core `ConfigFieldViewModel` already treats FilePath/ImagePath values as strings (no core change). This adds two `DataTemplate`s wired into the existing `ConfigFieldTemplateSelector`, a Browse click handler using `Microsoft.Win32.OpenFileDialog`, and a `string`-path→`ImageSource` converter for the thumbnail. The `.meta.json` confidence-sidecar pre-fill (§7.3) and the live test-match belong to BotCapture (M6/M8) and are out of scope.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), WPF. Builds on merged M4a.

## Verification model
WPF-only; not headlessly testable. Verification = `dotnet build ADB.slnx` 0 warnings + `dotnet test` still green (149) + the Manual Verification Checklist.

## File Structure
```
BotBuilder/
  ConfigFieldTemplateSelector.cs  # MODIFIED: add FilePath + ImagePath templates
  PathToImageConverter.cs         # NEW: string path -> BitmapImage (or null if missing)
  MainWindow.xaml                 # MODIFIED: FilePath/ImagePath DataTemplates + register them
  MainWindow.xaml.cs              # MODIFIED: BrowseField_Click handler
```

---

### Task 1: FilePath + ImagePath field editors

**Files:**
- Create: `BotBuilder/PathToImageConverter.cs`
- Modify: `BotBuilder/ConfigFieldTemplateSelector.cs`, `BotBuilder/MainWindow.xaml`, `BotBuilder/MainWindow.xaml.cs`

- [ ] **Step 1: Add the path→image converter**

Create `BotBuilder/PathToImageConverter.cs`:
```csharp
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace BotBuilder;

/// <summary>Converts an image file path to a loaded <see cref="BitmapImage"/> (cached on load so the
/// file isn't locked); returns null when the path is empty or the file does not exist.</summary>
public sealed class PathToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var path = value as string;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

- [ ] **Step 2: Extend the template selector**

In `BotBuilder/ConfigFieldTemplateSelector.cs`, add two template properties and route the two path types to them. Add the properties (alongside the existing ones):
```csharp
    public DataTemplate? FilePathTemplate { get; set; }
    public DataTemplate? ImagePathTemplate { get; set; }
```
And update the `switch` in `SelectTemplate` to add the two cases (before the `_ =>` default):
```csharp
            AdbCore.Actions.ConfigFieldType.FilePath => FilePathTemplate,
            AdbCore.Actions.ConfigFieldType.ImagePath => ImagePathTemplate,
```

- [ ] **Step 3: Add the templates + converter resource, and wire the selector**

In `BotBuilder/MainWindow.xaml`, inside `<Window.Resources>`:

(a) Add the converter resource (next to the other converters):
```xml
        <local:PathToImageConverter x:Key="PathToImage" />
```

(b) Add the two field templates (next to the existing `FieldString`/`FieldEnum` templates):
```xml
        <DataTemplate x:Key="FieldFilePath">
            <StackPanel Margin="0,4">
                <TextBlock Text="{Binding Label}" FontSize="11" Foreground="#666" />
                <DockPanel>
                    <Button DockPanel.Dock="Right" Content="Browse..." Margin="4,0,0,0" Padding="6,0"
                            Click="BrowseField_Click" />
                    <TextBox Text="{Binding Value, UpdateSourceTrigger=PropertyChanged}" />
                </DockPanel>
            </StackPanel>
        </DataTemplate>
        <DataTemplate x:Key="FieldImagePath">
            <StackPanel Margin="0,4">
                <TextBlock Text="{Binding Label}" FontSize="11" Foreground="#666" />
                <DockPanel>
                    <Button DockPanel.Dock="Right" Content="Browse..." Margin="4,0,0,0" Padding="6,0"
                            Click="BrowseField_Click" />
                    <TextBox Text="{Binding Value, UpdateSourceTrigger=PropertyChanged}" />
                </DockPanel>
                <Border BorderBrush="#DDD" BorderThickness="1" Margin="0,4,0,0" Background="White"
                        HorizontalAlignment="Left">
                    <Image Source="{Binding Value, Converter={StaticResource PathToImage}}"
                           MaxHeight="120" MaxWidth="220" Stretch="Uniform" Margin="2" />
                </Border>
            </StackPanel>
        </DataTemplate>
```

(c) Add the two new templates to the `ConfigFieldTemplateSelector` resource (extend the existing element):
```xml
        <local:ConfigFieldTemplateSelector x:Key="FieldSelector"
            StringTemplate="{StaticResource FieldString}"
            MultilineTemplate="{StaticResource FieldMultiline}"
            NumberTemplate="{StaticResource FieldNumber}"
            BooleanTemplate="{StaticResource FieldBoolean}"
            EnumTemplate="{StaticResource FieldEnum}"
            FilePathTemplate="{StaticResource FieldFilePath}"
            ImagePathTemplate="{StaticResource FieldImagePath}" />
```

- [ ] **Step 4: Add the Browse handler**

In `BotBuilder/MainWindow.xaml.cs`, add the handler (and ensure `using Microsoft.Win32;` and `using BotBuilder.Core.Properties;` are present — `Microsoft.Win32` is already used for the File menu dialogs):
```csharp
    private void BrowseField_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ConfigFieldViewModel field })
        {
            return;
        }

        var isImage = field.Type == AdbCore.Actions.ConfigFieldType.ImagePath;
        var dialog = new OpenFileDialog
        {
            Filter = isImage
                ? "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*"
                : "All files (*.*)|*.*",
            CheckFileExists = false,
        };
        if (dialog.ShowDialog(this) == true)
        {
            field.Value = dialog.FileName;
        }
    }
```

- [ ] **Step 5: Build and test**

Run: `dotnet build ADB.slnx`
Expected: 0 warnings, 0 errors. (MSB3021/MSB3027 exe-lock only → app running; report it. Compile must be clean.)
Run: `dotnet test`
Expected: 149 tests pass (no core changes; no new tests).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(builder): file-path and image-path field editors (browse + preview)"
```

**Manual Verification Checklist (`dotnet run --project BotBuilder`):**
- (No current built-in action has a FilePath/ImagePath field, so this is best verified once M5 adds one — e.g. Find Image's templatePath. For now, confirm the build runs and the existing String/Target/Retry panel still works.) Optionally hand-edit a `.bot` to give a `data.log`-style action an ImagePath config field, or defer visual verification of these editors to M5 when Find Image exists.
- Existing panel behavior (label, target dropdown, message field) is unchanged.

---

## Self-Review

**Spec coverage (design §5.4, M4b portion):**
- FilePath → TextBox + Browse — Task 1 (FieldFilePath template + BrowseField_Click). ✓
- ImagePath → TextBox + Browse + Preview thumbnail — Task 1 (FieldImagePath template + PathToImageConverter). ✓

**Placeholder scan:** None. The manual-verification note about no current action using these types is a factual limitation (resolved in M5), not a code placeholder.

**Type consistency:** `ConfigFieldTemplateSelector` gains `FilePathTemplate`/`ImagePathTemplate` matching the XAML wiring; `PathToImageConverter` keyed `PathToImage`; `BrowseField_Click` reads `ConfigFieldViewModel.Type`/sets `.Value`. Field value stays string (the core `ConfigFieldViewModel` default path). ✓

**Scope:** No `.meta.json` confidence sidecar (M6/M8), no live test-match (M6), no core changes. ✓
