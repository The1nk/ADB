# BotBuilder Theme Adoption Implementation Plan (Slice 2 of 3)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax. NOTE: this slice is XAML/WPF wiring + colour migration — there are no meaningful unit tests for visual XAML, so "verify" = clean build (`dotnet build ... -warnaserror`) + the human's live visual check at the end. The full existing solution test suite must stay green (no regressions).

**Goal:** Make BotBuilder consume the merged `AdbUi.Theme` library: initialise the `ThemeManager` at startup, add a `View ▸ Theme` menu (System / Light / Dark / High-Contrast), and migrate BotBuilder's inline hex colours to theme `{DynamicResource}` brushes so the whole window recolours live.

**Architecture:** `App.OnStartup` merges the shared `Controls.xaml` and initialises a `ThemeManager` (live `JsonSettingsStore` + `Win32OsThemeProbe` + `ResourceDictionaryThemeApplier`), exposed as `App.Theme` for the menu. All BotBuilder XAML inline brushes become `{DynamicResource <semantic key>}`. The shared `Controls.xaml` is extended with implicit styles for the few extra control types BotBuilder uses (StatusBar, ListBox, CheckBox) so they theme too.

**Tech Stack:** .NET `net10.0-windows`, WPF, `AdbUi.Theme` (already on `main`).

**Spec:** `Docs/Specs/2026-06-05-theming-system-design.md` (§5 flow, §8 slice 2). **Slice 1** (the `AdbUi.Theme` library) is merged.

---

## Canonical colour → brush-key mapping

Apply this mapping wherever the listed inline colour appears in a BotBuilder XAML file (Tasks 3–4). Replace the literal with `{DynamicResource <key>}`.

| Inline literal | Role | → Brush key |
| --- | --- | --- |
| `#F7F7F7`, `#EEE` | side panels / bars / toolbars | `PanelBackgroundBrush` |
| `White`, `#FFF` (panel/card/input backgrounds) | surfaces (cards, chips, palette items, input preview) | `SurfaceBackgroundBrush` |
| `#FAFAFA` | node-graph canvas backdrop | `CanvasBackgroundBrush` |
| `#222` | picker-dialog window background | `WindowBackgroundBrush` |
| `#333` | picker-dialog header bar | `PanelBackgroundBrush` |
| `#666`, `#888`, `#999`, `#555`, `#333` (as a **Foreground**), `Gray` | muted / secondary text | `SecondaryTextBrush` |
| `White` used as **Foreground** on a dark picker header | header text | `PrimaryTextBrush` |
| `#CCC`, `#DDD`, `#BBB` | borders / dividers | `BorderBrush` |
| `Fill="#555"` (graph ports) | port dots | `SecondaryTextBrush` |

**Deliberately left as literals (do NOT migrate):**
- The node-header label `Foreground="White"` (`MainWindow.xaml` ~L228) — that text sits on the saturated per-category colour (`CategoryColor`), which is theme-independent; white reads on every category colour in every theme.
- The marquee rectangle `Fill="#332962D6"` / `Stroke="#2962D6"` (`MainWindow.xaml` ~L272) — a transient selection indicator that reads on all themes.
- The region rubber-band `Stroke="Lime"` / `Fill="#3000FF00"` (`RegionPickerDialog.xaml` L17) — a functional selection overlay.
- The `CategoryColor` node-header backgrounds (driven by `CategoryColorToBrushConverter`) — semantic accents, unchanged.

---

## Task 1: Reference the theme library + wire startup + add the View ▸ Theme menu

**Files:**
- Modify: `BotBuilder/BotBuilder.csproj`
- Modify: `BotBuilder/App.xaml.cs`
- Modify: `BotBuilder/MainWindow.xaml` (add the View menu)
- Modify: `BotBuilder/MainWindow.xaml.cs` (menu handlers + checkmark sync)

- [ ] **Step 1: Add the project reference**

In `BotBuilder/BotBuilder.csproj`, add to the existing `<ItemGroup>` that holds `ProjectReference`s:

```xml
    <ProjectReference Include="..\AdbUi.Theme\AdbUi.Theme.csproj" />
```

Also register `AdbUi.Theme` in `ADB.slnx` if not already present — it IS present (merged in slice 1), so no change needed there.

- [ ] **Step 2: Wire `App.OnStartup`**

Replace the entire contents of `BotBuilder/App.xaml.cs` with:

```csharp
using System;
using System.Windows;
using AdbUi.Theme;

namespace BotBuilder;

public partial class App : Application
{
    /// <summary>The app-wide theme manager. Created at startup; the View ▸ Theme menu drives it.</summary>
    public ThemeManager Theme { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Theme-agnostic control styles. Their colours come from the active theme brush dictionary,
        // which the ThemeManager's applier merges/swaps. Merge this once, before MainWindow loads.
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/AdbUi.Theme;component/Themes/Controls.xaml", UriKind.Absolute),
        });

        Theme = new ThemeManager(
            new JsonSettingsStore(SettingsPaths.SettingsFile),
            new Win32OsThemeProbe(),
            new ResourceDictionaryThemeApplier());
        Theme.Initialize();
    }
}
```

`App.xaml` keeps `StartupUri="MainWindow.xaml"` (the window is created after `OnStartup`, so the theme is already applied when it loads). No change to `App.xaml`.

- [ ] **Step 3: Add the View ▸ Theme menu**

In `BotBuilder/MainWindow.xaml`, inside the `<Menu DockPanel.Dock="Top">`, add a new top-level `MenuItem` after the `_Run` menu item (after its closing `</MenuItem>` on line ~98, before `</Menu>`):

```xml
            <MenuItem Header="_View">
                <MenuItem Header="_Theme">
                    <MenuItem x:Name="ThemeSystemItem" Header="_System" IsCheckable="True" Click="ThemeSystem_Click" />
                    <MenuItem x:Name="ThemeLightItem" Header="_Light" IsCheckable="True" Click="ThemeLight_Click" />
                    <MenuItem x:Name="ThemeDarkItem" Header="_Dark" IsCheckable="True" Click="ThemeDark_Click" />
                    <MenuItem x:Name="ThemeHighContrastItem" Header="_High Contrast" IsCheckable="True" Click="ThemeHighContrast_Click" />
                </MenuItem>
            </MenuItem>
```

- [ ] **Step 4: Add the menu handlers**

In `BotBuilder/MainWindow.xaml.cs`, add `using AdbUi.Theme;` to the using block at the top, and add these methods to the `MainWindow` class (e.g. near the other `_Click` handlers):

```csharp
    private void ThemeSystem_Click(object sender, RoutedEventArgs e) => SetTheme(ThemeSelection.System);
    private void ThemeLight_Click(object sender, RoutedEventArgs e) => SetTheme(ThemeSelection.Light);
    private void ThemeDark_Click(object sender, RoutedEventArgs e) => SetTheme(ThemeSelection.Dark);
    private void ThemeHighContrast_Click(object sender, RoutedEventArgs e) => SetTheme(ThemeSelection.HighContrast);

    private void SetTheme(ThemeSelection selection)
    {
        ((App)Application.Current).Theme.Apply(selection);
        SyncThemeChecks();
    }

    // Reflects the active selection as the single checked item (radio-style). Called on startup and after
    // each change. OS-follow changes keep the selection at System, so no checkmark update is needed for them.
    private void SyncThemeChecks()
    {
        var selection = ((App)Application.Current).Theme.CurrentSelection;
        ThemeSystemItem.IsChecked = selection == ThemeSelection.System;
        ThemeLightItem.IsChecked = selection == ThemeSelection.Light;
        ThemeDarkItem.IsChecked = selection == ThemeSelection.Dark;
        ThemeHighContrastItem.IsChecked = selection == ThemeSelection.HighContrast;
    }
```

Then call `SyncThemeChecks();` at the END of the existing `MainWindow()` constructor (after `DataContext = _editor;`), so the initial checkmark reflects the loaded theme.

- [ ] **Step 5: Build + smoke**

Run: `dotnet build BotBuilder/BotBuilder.csproj -warnaserror`
Expected: Build succeeded, 0 warnings. (Functional check that the app launches + the menu switches themes is part of the final human visual verification — Task 5.)

- [ ] **Step 6: Commit**

```bash
git add BotBuilder/BotBuilder.csproj BotBuilder/App.xaml.cs BotBuilder/MainWindow.xaml BotBuilder/MainWindow.xaml.cs
git commit -m "feat(theme): wire ThemeManager at BotBuilder startup + View>Theme menu"
```

---

## Task 2: Extend the shared `Controls.xaml` for BotBuilder's extra control types

**Files:**
- Modify: `AdbUi.Theme/Themes/Controls.xaml`

BotBuilder uses `StatusBar`, `ListBox` (the log panel), and `CheckBox` (boolean fields). The slice-1 `Controls.xaml` didn't style these, so they'd stay system-light in dark mode. Add implicit styles (theme-agnostic, all `{DynamicResource}`). This lives in the shared assembly so BotCapture (slice 3) benefits too.

- [ ] **Step 1: Add the styles**

In `AdbUi.Theme/Themes/Controls.xaml`, add these styles just before the closing `</ResourceDictionary>`:

```xml
    <!-- StatusBar -->
    <Style TargetType="StatusBar">
        <Setter Property="Background" Value="{DynamicResource PanelBackgroundBrush}" />
        <Setter Property="Foreground" Value="{DynamicResource PrimaryTextBrush}" />
    </Style>

    <Style TargetType="StatusBarItem">
        <Setter Property="Foreground" Value="{DynamicResource PrimaryTextBrush}" />
    </Style>

    <!-- ListBox -->
    <Style TargetType="ListBox">
        <Setter Property="Background" Value="{DynamicResource SurfaceBackgroundBrush}" />
        <Setter Property="Foreground" Value="{DynamicResource PrimaryTextBrush}" />
        <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}" />
    </Style>

    <Style TargetType="ListBoxItem">
        <Setter Property="Foreground" Value="{DynamicResource PrimaryTextBrush}" />
    </Style>

    <!-- CheckBox -->
    <Style TargetType="CheckBox">
        <Setter Property="Foreground" Value="{DynamicResource PrimaryTextBrush}" />
    </Style>
```

- [ ] **Step 2: Build**

Run: `dotnet build AdbUi.Theme/AdbUi.Theme.csproj -warnaserror`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add AdbUi.Theme/Themes/Controls.xaml
git commit -m "feat(theme): style StatusBar/ListBox/CheckBox in shared Controls.xaml"
```

---

## Task 3: Migrate `MainWindow.xaml` inline colours

**Files:**
- Modify: `BotBuilder/MainWindow.xaml`

Apply the canonical mapping. Read the file and replace each inline brush per the table. Specific replacements (line numbers approximate — match by content):

- [ ] **Step 1: Add a window background**

On the root `<Window ...>` element, add the attribute:
```
Background="{DynamicResource WindowBackgroundBrush}"
```

- [ ] **Step 2: Replace the field-label foregrounds**

Every `Foreground="#666"` in the `Window.Resources` field DataTemplates (FieldString/Multiline/Number/Enum/FilePath/ImagePath labels) AND in the properties panel (the `Label`, `Target`, `Max Attempts`, `Delay (ms)` labels) → `Foreground="{DynamicResource SecondaryTextBrush}"`. (There are several; replace all.)

- [ ] **Step 3: Replace the image-preview border** (FieldImagePath template)

`BorderBrush="#DDD"` → `BorderBrush="{DynamicResource BorderBrush}"`; `Background="White"` → `Background="{DynamicResource SurfaceBackgroundBrush}"`.

- [ ] **Step 4: Replace the target bar**

The target-bar `<Border ... Background="#EEE" ... BorderBrush="#CCC" ...>`: `#EEE` → `{DynamicResource PanelBackgroundBrush}`, `#CCC` → `{DynamicResource BorderBrush}`.
The target chip `<Border Background="White" BorderBrush="#BBB" ...>`: `White` → `{DynamicResource SurfaceBackgroundBrush}`, `#BBB` → `{DynamicResource BorderBrush}`.

- [ ] **Step 5: Replace the status bar run-status foreground**

`<TextBlock x:Name="RunStatusText" Foreground="#555" />` → `Foreground="{DynamicResource SecondaryTextBrush}"`.

- [ ] **Step 6: Replace the palette panel + items**

Palette `<DockPanel Grid.Column="0" Background="#F7F7F7">` → `{DynamicResource PanelBackgroundBrush}`.
Palette item `<Border Background="#FFF" BorderBrush="#DDD" ...>` → `Background="{DynamicResource SurfaceBackgroundBrush}"`, `BorderBrush="{DynamicResource BorderBrush}"`.

- [ ] **Step 7: Replace the viewport/canvas**

`<Border Grid.Column="1" x:Name="ViewportHost" Background="#FAFAFA" BorderBrush="#CCC" ...>` → `Background="{DynamicResource CanvasBackgroundBrush}"`, `BorderBrush="{DynamicResource BorderBrush}"`.

- [ ] **Step 8: Replace the node card + badge + ports**

Node card `<Border Width="160" ... Background="White" ...>` → `Background="{DynamicResource SurfaceBackgroundBrush}"`.
Node header label `<TextBlock Text="{Binding Label}" Foreground="White" .../>` → **leave `Foreground="White"`** (text on the category colour — do not change).
Target badge `<TextBlock Text="{Binding TargetBadge}" ... Foreground="#888" ...>` → `Foreground="{DynamicResource SecondaryTextBrush}"`.
Both port `<Ellipse ... Fill="#555" ...>` (input + output) → `Fill="{DynamicResource SecondaryTextBrush}"`.
Marquee `<Rectangle x:Name="MarqueeRect" ... Fill="#332962D6" Stroke="#2962D6" ...>` → **leave as-is** (transient selection indicator).

- [ ] **Step 9: Replace the properties panel**

Properties `<Border Grid.Column="2" Background="#F7F7F7" BorderBrush="#CCC" ...>` → `Background="{DynamicResource PanelBackgroundBrush}"`, `BorderBrush="{DynamicResource BorderBrush}"`.
Placeholder `<TextBlock Text="Select a node to edit its properties." Foreground="#999" ...>` → `Foreground="{DynamicResource SecondaryTextBrush}"`.
The retry-section divider `<Border BorderBrush="#DDD" ...>` → `BorderBrush="{DynamicResource BorderBrush}"`.

- [ ] **Step 10: Build**

Run: `dotnet build BotBuilder/BotBuilder.csproj -warnaserror`
Expected: Build succeeded, 0 warnings. Then grep to confirm only the intentional literals remain:
Run: `git grep -nE "#[0-9A-Fa-f]{3,8}|\"White\"|\"Gray\"" BotBuilder/MainWindow.xaml`
Expected: only the node-header `Foreground="White"` and the two marquee colours (`#332962D6`, `#2962D6`) remain.

- [ ] **Step 11: Commit**

```bash
git add BotBuilder/MainWindow.xaml
git commit -m "feat(theme): migrate MainWindow.xaml inline colours to theme brushes"
```

---

## Task 4: Migrate the four secondary XAML files

**Files:**
- Modify: `BotBuilder/LogPanelView.xaml`
- Modify: `BotBuilder/TargetPickerDialog.xaml`
- Modify: `BotBuilder/CoordinatePickerDialog.xaml`
- Modify: `BotBuilder/RegionPickerDialog.xaml`

- [ ] **Step 1: `LogPanelView.xaml`**

- Header `<StackPanel ... Background="#EEE">` → `Background="{DynamicResource PanelBackgroundBrush}"`.
- `<TextBlock x:Name="StatusText" ... Foreground="#333" />` → `Foreground="{DynamicResource SecondaryTextBrush}"`.

- [ ] **Step 2: `TargetPickerDialog.xaml`**

- On the root `<Window ...>` add `Background="{DynamicResource WindowBackgroundBrush}"`.
- `<TextBlock Text="Equivalent command ..." Foreground="Gray" />` → `Foreground="{DynamicResource SecondaryTextBrush}"`.
- `<TextBlock Text="{Binding Type}" Foreground="Gray" FontSize="11" />` → `Foreground="{DynamicResource SecondaryTextBrush}"`.

- [ ] **Step 3: `CoordinatePickerDialog.xaml`**

- Root `<Window ... Background="#222">` → `Background="{DynamicResource WindowBackgroundBrush}"`.
- Header `<Border ... Background="#333" ...>` → `Background="{DynamicResource PanelBackgroundBrush}"`.
- `<TextBlock x:Name="PromptText" Foreground="White" ...>` → `Foreground="{DynamicResource PrimaryTextBrush}"`.

- [ ] **Step 4: `RegionPickerDialog.xaml`**

- Root `<Window ... Background="#222">` → `Background="{DynamicResource WindowBackgroundBrush}"`.
- Header `<Border ... Background="#333" ...>` → `Background="{DynamicResource PanelBackgroundBrush}"`.
- `<TextBlock Text="Drag a box around the region" Foreground="White" ...>` → `Foreground="{DynamicResource PrimaryTextBrush}"`.
- The rubber-band `<Rectangle x:Name="RubberBand" Stroke="Lime" ... Fill="#3000FF00" ...>` → **leave as-is** (functional region overlay).

- [ ] **Step 5: Build**

Run: `dotnet build BotBuilder/BotBuilder.csproj -warnaserror`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add BotBuilder/LogPanelView.xaml BotBuilder/TargetPickerDialog.xaml BotBuilder/CoordinatePickerDialog.xaml BotBuilder/RegionPickerDialog.xaml
git commit -m "feat(theme): migrate LogPanel/TargetPicker/coordinate+region dialogs to theme brushes"
```

---

## Task 5: Full build + test gate + human visual verification

**Files:** none (verification only).

- [ ] **Step 1: Full solution build**

Run: `dotnet build ADB.slnx -warnaserror`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 2: Full test suite**

Run: `dotnet test ADB.slnx`
Expected: All tests pass (693), no regressions. (No new tests in this slice — it is visual XAML.)

- [ ] **Step 3: Hand off to the human for visual verification**

This slice's real acceptance is visual. The controller hands BotBuilder to the user to verify (the user runs the app). Checklist for the user:
- App launches; default theme matches the OS (light or dark) when selection is System.
- `View ▸ Theme` switches between System / Light / Dark / High-Contrast live, with the correct item checked.
- In **Dark**: window, panels, palette, properties, target bar, status bar, log panel, node cards, menus, textboxes, combo boxes, buttons all recolour (no stray white/light chrome). Node category-header colours remain their saturated accents with readable white text.
- In **High-Contrast**: black backgrounds, bright text, strong borders; everything legible.
- Quit and relaunch → the chosen theme persists (`%AppData%/ADB/settings.json`).
- While on **System**, toggle Windows' app theme → BotBuilder follows live.
- The coordinate/region picker dialogs and the F5 target picker also follow the theme.
- Report any surface that didn't recolour or any unreadable colour (brush starting values are tunable — adjust `AdbUi.Theme/Themes/*.xaml`).

---

## Done — Slice 2 complete (pending user visual sign-off)

BotBuilder now fully themed and switchable. After the user verifies + signs off (and any colour tuning), this becomes a PR for the user to merge (it is a visual slice). **Next:** Slice 3 — BotCapture adoption (reference `AdbUi.Theme`, init `ThemeManager`, add a compact theme selector since BotCapture has no menu bar, migrate its XAML).
