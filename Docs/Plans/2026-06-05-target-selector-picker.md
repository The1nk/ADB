# Target Selector Picker (chip QOL) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** A "Pick…" button on each target chip that opens a per-type chooser (window/device/browser) and writes the selector straight into the chip.

**Architecture:** A pure `SelectorFormat` helper (testable) + a small WPF `SelectorPickerDialog` reusing the existing enumeration (`IWindowEnumerator`/`IAdbDevices`/`BrowserSelector.Engines`) + a chip button & handler.

**Tech Stack:** C# / .NET 10, BotBuilder.Core + BotBuilder WPF, xUnit.

**Reference spec:** `Docs/Specs/2026-06-05-target-selector-picker-design.md`.

**Merge handling:** `SelectorFormat` unit-tested; the WPF picker is **user-verified PR, not self-merged.** Conflict-free with current main.

**`<WT>` = `C:\git\ADB\.claude\worktrees\selector-picker`. Windows: PowerShell for `dotnet`/`git`; NEVER redirect to `/dev/null`/`NUL`.**

---

## Task 1: `SelectorFormat` pure helper

**Files:** Create `BotBuilder.Core/Targets/SelectorFormat.cs`, `BotBuilder.Core.Tests/Targets/SelectorFormatTests.cs`.

- [ ] **Step 1: Write the failing test.** `BotBuilder.Core.Tests/Targets/SelectorFormatTests.cs`:
```csharp
using BotBuilder.Core.Targets;
using Xunit;

namespace BotBuilder.Core.Tests.Targets;

public class SelectorFormatTests
{
    [Fact]
    public void Window_WithProcess_UsesProcessSelector()
        => Assert.Equal("process:Notepad", SelectorFormat.Window("Notepad", "Untitled - Notepad"));

    [Fact]
    public void Window_EmptyProcess_FallsBackToTitle()
        => Assert.Equal("title:Some Window", SelectorFormat.Window("", "Some Window"));

    [Fact]
    public void Android_UsesSerial()
        => Assert.Equal("serial:emulator-5554", SelectorFormat.Android("emulator-5554"));

    [Fact]
    public void Browser_UsesEngine()
        => Assert.Equal("browser:chromium", SelectorFormat.Browser("chromium"));
}
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test "<WT>\BotBuilder.Core.Tests" --filter "FullyQualifiedName~SelectorFormatTests"` → compile FAIL.

- [ ] **Step 3: Create `BotBuilder.Core/Targets/SelectorFormat.cs`:**
```csharp
namespace BotBuilder.Core.Targets;

/// <summary>Builds target selector strings from picked window/device/browser choices. Matches the formats
/// the Test-Run target picker uses (process:/title: for windows, serial: for Android, browser: for browsers).</summary>
public static class SelectorFormat
{
    public static string Window(string processName, string title)
        => string.IsNullOrEmpty(processName) ? $"title:{title}" : $"process:{processName}";

    public static string Android(string serial) => $"serial:{serial}";

    public static string Browser(string engine) => $"browser:{engine}";
}
```

- [ ] **Step 4: Run to verify it passes** — green.

- [ ] **Step 5: Commit:**
```
git -C "<WT>" add BotBuilder.Core/Targets/SelectorFormat.cs BotBuilder.Core.Tests/Targets/SelectorFormatTests.cs
git -C "<WT>" commit -m "feat(targets): SelectorFormat helper (process/title/serial/browser selector strings)"
```

---

## Task 2: `SelectorPickerDialog` + chip "Pick…" button

**Files:** Create `BotBuilder/SelectorPickerDialog.xaml` + `.xaml.cs`; modify `BotBuilder/MainWindow.xaml` (chip button) + `BotBuilder/MainWindow.xaml.cs` (handler).

- [ ] **Step 1: Read** `BotBuilder/TargetPickerDialog.xaml.cs` (the existing enumeration: `IWindowEnumerator.Enumerate() → WindowInfo {ProcessName, Title}`; `new AdbCore.Android.AdvancedSharpAdbDevices().List() → AdbDeviceInfo {Serial, State}`; `AdbCore.Browser.BrowserSelector.Engines`). Read `BotBuilder/MainWindow.xaml` lines ~100-125 (the `TargetBar.Targets` ItemsControl chip `DataTemplate` with the Type `ComboBox`, Selector `TextBox`, and remove `Button`) and how `MainWindow.xaml.cs` constructs/uses `IWindowEnumerator` (e.g. `Win32WindowEnumerator`). Read `BotBuilder.Core/Targets/TargetViewModel.cs` (`Type` is `BotTargetType`, `Selector` is settable observable).

- [ ] **Step 2: Create `BotBuilder/SelectorPickerDialog.xaml`** — a small modal: a label ("Choose a target:"), a `ListBox` or `ComboBox` `x:Name="Choices"` (DisplayMemberPath set to a display string), and OK/Cancel buttons. Title "Pick target". Size ~ 420x360. Mirror the window-chrome style of `TargetPickerDialog.xaml`/`RegionPickerDialog.xaml` (read one for the `Window` root + button bar pattern).

- [ ] **Step 3: Create `BotBuilder/SelectorPickerDialog.xaml.cs`:**
```csharp
using System;
using System.Linq;
using System.Windows;
using AdbCore.Android;
using AdbCore.Models;
using AdbCore.Targets;
using BotBuilder.Core.Targets;

namespace BotBuilder;

/// <summary>Picks a single target selector for a chip, populated for the chip's <see cref="BotTargetType"/>:
/// open windows, connected ADB devices, or browser engines. Writes the chosen selector via SelectorFormat.</summary>
public partial class SelectorPickerDialog : Window
{
    private readonly BotTargetType _type;

    public SelectorPickerDialog(BotTargetType type, IWindowEnumerator windows)
    {
        InitializeComponent();
        _type = type;
        Choices.ItemsSource = BuildChoices(type, windows);
    }

    /// <summary>The chosen selector string, valid after the dialog returns true.</summary>
    public string? ChosenSelector { get; private set; }

    private sealed record Choice(string Display, string Selector);

    private static System.Collections.Generic.IReadOnlyList<Choice> BuildChoices(BotTargetType type, IWindowEnumerator windows) => type switch
    {
        BotTargetType.Window => windows.Enumerate()
            .Select(w => new Choice(
                string.IsNullOrEmpty(w.ProcessName) ? w.Title : $"{w.ProcessName} — {w.Title}",
                SelectorFormat.Window(w.ProcessName, w.Title)))
            .ToList(),
        BotTargetType.AndroidDevice => ListDevices(),
        BotTargetType.Browser => AdbCore.Browser.BrowserSelector.Engines
            .Select(e => new Choice(e, SelectorFormat.Browser(e))).ToList(),
        _ => Array.Empty<Choice>(),
    };

    private static System.Collections.Generic.IReadOnlyList<Choice> ListDevices()
    {
        try
        {
            return new AdvancedSharpAdbDevices().List()
                .Select(d => new Choice($"{d.Serial} ({d.State})", SelectorFormat.Android(d.Serial)))
                .ToList();
        }
        catch
        {
            return Array.Empty<Choice>();   // no ADB server / devices — leave manual entry
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Choices.SelectedItem is Choice choice)
        {
            ChosenSelector = choice.Selector;
            DialogResult = true;
        }
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
```
Set the `ListBox`/`ComboBox` `DisplayMemberPath="Display"` in XAML. Wire `Ok_Click`/`Cancel_Click` to the buttons, and a double-click on the list to OK is a nice touch (optional). **Adapt** the exact `AdbDeviceInfo` member names (`Serial`/`State`) + `IWindowEnumerator.Enumerate()` + `BrowserSelector.Engines` to what `TargetPickerDialog.xaml.cs` actually uses (you read it in Step 1).

- [ ] **Step 4: Add the chip "Pick…" button** in `BotBuilder/MainWindow.xaml` — in the `TargetBar.Targets` chip `DataTemplate`, add next to the Selector `TextBox`:
```xml
                                    <Button Content="Pick…" Click="PickSelector_Click" Margin="4,0,0,0" Padding="4,0" />
```
(Place it between the Selector TextBox and the remove "x" button; match the existing inline style.)

- [ ] **Step 5: Add the handler** in `BotBuilder/MainWindow.xaml.cs`:
```csharp
    private void PickSelector_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BotBuilder.Core.Targets.TargetViewModel target })
        {
            var dialog = new SelectorPickerDialog(target.Type, new AdbCore.Targets.Win32WindowEnumerator()) { Owner = this };
            if (dialog.ShowDialog() == true && dialog.ChosenSelector is string selector)
            {
                target.Selector = selector;
                _editor.MarkDirty();
            }
        }
    }
```
**Adapt:** if `MainWindow` already holds an `IWindowEnumerator` field, reuse it instead of `new Win32WindowEnumerator()`. Confirm `_editor.MarkDirty()` exists (it does — used by property edits). `target.Selector` is an observable settable property.

- [ ] **Step 6: Build + full sweep.** `dotnet build "<WT>\ADB.slnx" -warnaserror -v q --nologo` → 0 warnings; `dotnet test "<WT>\ADB.slnx"` → all green. Report totals.

- [ ] **Step 7: Commit:**
```
git -C "<WT>" add BotBuilder/SelectorPickerDialog.xaml BotBuilder/SelectorPickerDialog.xaml.cs BotBuilder/MainWindow.xaml BotBuilder/MainWindow.xaml.cs
git -C "<WT>" commit -m "feat(targets): per-chip Pick… selector picker (window/device/browser)"
```

---

## Self-Review Notes (addressed)

- **Spec coverage:** `SelectorFormat` helper + tests (Task 1); the per-type dialog reusing existing enumeration + chip button + handler writing the selector (Task 2). ✓
- **Reuse:** mirrors `TargetPickerDialog`'s enumeration; `SelectorFormat` matches its selector formats. ✓
- **Type consistency:** `SelectorFormat.Window/Android/Browser`, `SelectorPickerDialog(BotTargetType, IWindowEnumerator).ChosenSelector`, `PickSelector_Click`. ✓
- **WPF feature → user-verified PR.** No `.bot`/engine change.
