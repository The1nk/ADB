# BotCapture Theme Adoption Implementation Plan (Slice 3 of 3)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax. This slice is XAML/WPF wiring + colour migration — "verify" = clean build (`-warnaserror`) + the human's live visual check. The full solution test suite must stay green.

**Goal:** Make BotCapture consume `AdbUi.Theme` (already on `main`): initialise the `ThemeManager` at startup, add a compact theme selector (BotCapture has no menu bar), and migrate its inline colours to theme `{DynamicResource}` brushes. The shared `Controls.xaml` (incl. the templated menus from slice 2) is already on `main`, so BotCapture inherits all control/menu theming automatically.

**Architecture:** `App.OnStartup` (which already parses CLI args) merges `Controls.xaml` and initialises a `ThemeManager`, exposed as `App.Theme`. `MainWindow` (a host `Grid` that swaps views into `Root`) gains a thin top bar with a theme `ComboBox`; `Root` stays the content host. All view XAML inline colours become semantic brushes. BotCapture and BotBuilder share `%AppData%/ADB/settings.json`, so the theme stays in sync.

**Tech Stack:** .NET `net10.0-windows`, WPF, `AdbUi.Theme` (on `main`).

**Spec:** `Docs/Specs/2026-06-05-theming-system-design.md` (§5, §8 slice 3).

---

## Canonical colour → brush-key mapping
| Inline literal | Role | → Brush key |
| --- | --- | --- |
| `LightGray` | borders | `BorderBrush` |
| `Gray` | muted/secondary text | `SecondaryTextBrush` |
| `DarkRed` | error status text | `ErrorBrush` |
| `Green` | success status text | `SuccessBrush` |

**Leave as literals:** `RegionSelectView` selection `Stroke="Lime"` / `Fill="#330000FF"` (functional region overlay); the `🗑` glyph; the `RetestIndicatorConverter` fill (functional red/green/gray status dot).

---

## Task 1: Reference the library, wire startup, add the compact theme selector

**Files:**
- Modify: `BotCapture/BotCapture.csproj`
- Modify: `BotCapture/App.xaml.cs`
- Modify: `BotCapture/MainWindow.xaml`
- Modify: `BotCapture/MainWindow.xaml.cs`

- [ ] **Step 1: Project reference**

In `BotCapture/BotCapture.csproj`, add to the existing `<ItemGroup>` of `<ProjectReference>`s:
```xml
    <ProjectReference Include="..\AdbUi.Theme\AdbUi.Theme.csproj" />
```

- [ ] **Step 2: Wire `App.OnStartup`**

In `BotCapture/App.xaml.cs`: add `using AdbUi.Theme;` to the usings, add a `Theme` property, and initialise it at the very start of `OnStartup` (after `base.OnStartup(e);`, before the arg parsing). Replace the class body so it reads:

```csharp
using System;
using System.IO;
using System.Windows;
using AdbUi.Theme;
using BotCapture.Core;

namespace BotCapture;

public partial class App : Application
{
    /// <summary>The app-wide theme manager. Created at startup; the MainWindow theme selector drives it.</summary>
    public ThemeManager Theme { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Theme-agnostic control styles; the active brush dictionary is merged/swapped by the manager.
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/AdbUi.Theme;component/Themes/Controls.xaml", UriKind.Absolute),
        });
        Theme = new ThemeManager(
            new JsonSettingsStore(SettingsPaths.SettingsFile),
            new Win32OsThemeProbe(),
            new ResourceDictionaryThemeApplier());
        Theme.Initialize();

        string? outputPath;
        try
        {
            outputPath = CommandLineArgs.Parse(e.Args).OutputPath;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "BotCapture", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(2);
            return;
        }

        // Resolve to an absolute path so a relative --output saves where the user expects (against the
        // working directory) rather than depending on an empty GetDirectoryName downstream.
        if (outputPath is not null)
        {
            outputPath = Path.GetFullPath(outputPath);
        }

        new MainWindow(outputPath).Show();
    }
}
```

> NOTE: the original `App.xaml.cs` had `using System.IO; using System.Windows; using BotCapture.Core;`. The snippet above is the complete replacement; the only added usings are `using System;` (for `Uri`) and `using AdbUi.Theme;`.

- [ ] **Step 3: MainWindow top bar + theme selector**

Replace the entire body of `BotCapture/MainWindow.xaml` with:

```xml
<Window x:Class="BotCapture.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="BotCapture" Height="700" Width="1000"
        Background="{DynamicResource WindowBackgroundBrush}">
    <DockPanel>
        <Border DockPanel.Dock="Top" Background="{DynamicResource PanelBackgroundBrush}"
                BorderBrush="{DynamicResource BorderBrush}" BorderThickness="0,0,0,1" Padding="6,3">
            <DockPanel>
                <ComboBox x:Name="ThemeSelector" DockPanel.Dock="Right" Width="130"
                          SelectionChanged="ThemeSelector_SelectionChanged" ToolTip="Theme" />
                <TextBlock DockPanel.Dock="Right" Text="Theme:" VerticalAlignment="Center" Margin="0,0,6,0" />
                <TextBlock />
            </DockPanel>
        </Border>
        <Grid x:Name="Root" />
    </DockPanel>
</Window>
```

- [ ] **Step 4: MainWindow code-behind — selector init + handler**

In `BotCapture/MainWindow.xaml.cs`: add `using AdbUi.Theme;` to the usings. Add a call to `InitThemeSelector();` at the END of the `MainWindow(string? outputPath = null)` constructor (after the existing `if (_outputPath is not null) { ... } else { ShowSession(); }` block). Then add these two members to the class:

```csharp
    private void InitThemeSelector()
    {
        ThemeSelector.ItemsSource = Enum.GetValues<ThemeSelection>();
        ThemeSelector.SelectedItem = ((App)Application.Current).Theme.CurrentSelection;
    }

    private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeSelector.SelectedItem is ThemeSelection selection)
        {
            ((App)Application.Current).Theme.Apply(selection);
        }
    }
```

(`SelectionChangedEventArgs` is in `System.Windows.Controls`, already imported in this file. Setting `SelectedItem` during init fires the handler once, which re-applies the already-active theme — harmless/idempotent.)

- [ ] **Step 5: Build**

Run: `dotnet build BotCapture/BotCapture.csproj -warnaserror`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add BotCapture/BotCapture.csproj BotCapture/App.xaml.cs BotCapture/MainWindow.xaml BotCapture/MainWindow.xaml.cs
git commit -m "feat(theme): wire ThemeManager at BotCapture startup + compact theme selector"
```

---

## Task 2: Migrate the view XAML colours

**Files:**
- Modify: `BotCapture/Views/SessionView.xaml`
- Modify: `BotCapture/Views/WindowPickerView.xaml`
- Modify: `BotCapture/Views/RegionSelectView.xaml`
- Modify: `BotCapture/Views/PreviewConfirmView.xaml`

Read each file; replace per the mapping. `DynamicResource` only.

- [ ] **Step 1: `SessionView.xaml`**
- `<TextBlock Text="Saved this session ..." Foreground="Gray" ...>` → `Foreground="{DynamicResource SecondaryTextBrush}"`.
- `<Border BorderBrush="LightGray" ...>` → `BorderBrush="{DynamicResource BorderBrush}"`.
- The retest `<Ellipse ... Stroke="Gray" ...>` → `Stroke="{DynamicResource SecondaryTextBrush}"` (leave its `Fill="{Binding ... Converter=...}"` unchanged).
- Leave the `🗑` button content unchanged.

- [ ] **Step 2: `WindowPickerView.xaml`**
- `<TextBlock Text="{Binding StatusMessage}" Foreground="DarkRed" ...>` → `Foreground="{DynamicResource ErrorBrush}"`.
- `<TextBlock Text="{Binding ProcessName}" Foreground="Gray" ...>` → `Foreground="{DynamicResource SecondaryTextBrush}"`.
- `<Border Grid.Column="1" BorderBrush="LightGray" ...>` → `BorderBrush="{DynamicResource BorderBrush}"`.

- [ ] **Step 3: `RegionSelectView.xaml`**
- `<Border BorderBrush="LightGray" ...>` → `BorderBrush="{DynamicResource BorderBrush}"`.
- Leave the `<Rectangle x:Name="SelectionRect" Stroke="Lime" ... Fill="#330000FF" ...>` unchanged.

- [ ] **Step 4: `PreviewConfirmView.xaml`**
- Both preview `<Border BorderBrush="LightGray" ...>` (1× and 2×) → `BorderBrush="{DynamicResource BorderBrush}"`.
- `<TextBlock x:Name="SaveStatus" Foreground="Green" ...>` → `Foreground="{DynamicResource SuccessBrush}"`.

- [ ] **Step 5: Build**

Run: `dotnet build BotCapture/BotCapture.csproj -warnaserror`
Expected: Build succeeded, 0 warnings. Then confirm only intended literals remain:
Run: `git grep -nE "LightGray|\"Gray\"|DarkRed|\"Green\"" BotCapture/Views BotCapture/MainWindow.xaml`
Expected: no matches (all migrated). `Lime`/`#330000FF` and `🗑` may still appear — that's intended.

- [ ] **Step 6: Commit**

```bash
git add BotCapture/Views/SessionView.xaml BotCapture/Views/WindowPickerView.xaml BotCapture/Views/RegionSelectView.xaml BotCapture/Views/PreviewConfirmView.xaml
git commit -m "feat(theme): migrate BotCapture view colours to theme brushes"
```

---

## Task 3: Full build + test gate + human visual verification

**Files:** none.

- [ ] **Step 1: Full solution build**

Run: `dotnet build ADB.slnx -warnaserror`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 2: Full test suite**

Run: `dotnet test ADB.slnx`
Expected: All tests pass (693), no regressions.

- [ ] **Step 3: Hand off for visual verification**

Checklist for the user (run BotCapture standalone — no `--output`):
- The top bar shows a "Theme:" selector; switching System/Light/Dark/HighContrast recolours BotCapture live.
- In Dark/HC: the session panel, window-picker list, region-select, and preview-confirm views all recolour (lists, textboxes, buttons, borders, status text). The `DarkRed` status reads as the theme error colour; the `Green` save status as the success colour.
- The region-select Lime selection box and the retest status dot still render (functional colours kept).
- Theme matches whatever BotBuilder is set to (shared `settings.json`); persists across restart; follows OS while on System.
- Known minor: the `Slider` in Preview/Confirm keeps default chrome (not themed) — acceptable; tune later if desired.

---

## Done — Slice 3 complete (pending user visual sign-off) → theming milestone COMPLETE

After sign-off this is a PR for the user to merge. With slices 1–3 done, the theming milestone is complete, and the **disabled-dependency palette greying** (the original request) can proceed next, consuming `DisabledTextBrush`.
