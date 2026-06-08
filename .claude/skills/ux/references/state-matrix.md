# State Matrix Reference

## Contents
- Why a State Matrix
- Standard States for ADB Surfaces
- Per-Surface Matrices
- Binding State to XAML
- WARNING: Missing States
- Checklist

## Why a State Matrix

WPF data binding makes it easy to wire a single `bool IsLoading` and call it done. That leaves error, retry, disabled, and empty states unhandled — the user sees a frozen UI with no explanation. Define the full matrix before editing XAML.

## Standard States for ADB Surfaces

Every async or device-dependent surface must cover:

| State | Description | UI obligation |
|-------|-------------|---------------|
| Idle | Default, ready for input | Controls enabled |
| Loading | Async op in progress | ProgressBar visible, controls disabled |
| Success | Op completed | Confirmation visible briefly or list refreshed |
| Error | Op failed | Error message visible, retry available |
| Disabled | Prerequisites not met | Controls greyed, tooltip explains why |
| Empty | Collection has no items | Empty-state text, not blank whitespace |

## Per-Surface Matrices

### Palette (`BotBuilder.Core/Palette/PaletteViewModel.cs`)

| State | Trigger | UI |
|-------|---------|-----|
| Available | Dependency present (`IDependencyProbe`) | Normal item color |
| Greyed | Dependency absent | Soft-grey item, tooltip: "Requires [dep]" |

```csharp
// Existing pattern — DependencyProbe drives IsAvailable
public bool IsAvailable => _dependencyProbe.IsAvailable(ActionDefinition);
```

### Picker Dialogs (`TargetPickerViewModel`, etc.)

| State | Trigger | UI |
|-------|---------|-----|
| Idle | Dialog opened, list loaded | List shown, OK enabled if selection |
| Loading | Refresh in progress | ProgressBar, list disabled |
| Error | ADB/Win32 call failed | Error TextBlock, Retry button |
| Empty | No devices/windows found | "No targets found. Connect a device or open a window." |
| Confirmed | OK pressed with selection | DialogResult = true |
| Cancelled | Cancel pressed | DialogResult = false, no mutation |

### Run Toolbar

| State | Trigger | UI |
|-------|---------|-----|
| Ready | Not running | Run button enabled, Stop hidden |
| Running | `IsRunning = true` | Stop button shown, Run disabled |
| Error | `LastError != null` | Error badge on toolbar, log scrolled to error |
| No nodes | Canvas empty | Run button disabled, tooltip: "Add actions to run" |

## Binding State to XAML

Prefer an enum + converter over multiple `bool` properties:

```csharp
// new code to add
public enum SurfaceState { Idle, Loading, Error, Empty }

// In ViewModel:
public SurfaceState State { get; private set; }
```

```xml
<!-- new code to add — single converter drives multiple bindings -->
<ProgressBar IsIndeterminate="True"
             Visibility="{Binding State, Converter={StaticResource EnumToVisibility},
                          ConverterParameter=Loading}" />
<StackPanel Visibility="{Binding State, Converter={StaticResource EnumToVisibility},
                          ConverterParameter=Error}">
    <TextBlock Text="{Binding ErrorMessage}" />
    <Button Content="Retry" Command="{Binding RetryCommand}" />
</StackPanel>
<TextBlock Text="No items found."
           Visibility="{Binding State, Converter={StaticResource EnumToVisibility},
                        ConverterParameter=Empty}" />
```

### WARNING: Missing States

**The Problem:**
```csharp
// BAD — only two states; error and empty are invisible to the user
public bool IsLoading { get; set; }
public bool IsLoaded { get; set; }
```

**Why This Breaks:**
1. Network/ADB failure leaves `IsLoading = false`, `IsLoaded = false` — blank UI, no explanation.
2. Empty device list looks identical to "still loading" — user waits forever.
3. Adding a third state later requires refactoring all XAML bindings.

**The Fix:** Use the enum pattern above from the start.

## State Matrix Checklist

- [ ] Every async surface has: Idle, Loading, Error, Empty (if collection), Success
- [ ] `CanExecute` on commands reflects state (no enabled button during Loading)
- [ ] Error state shows message + retry, never just hides the error
- [ ] Empty state shows guidance text, not blank space
- [ ] Disabled state has a tooltip explaining why