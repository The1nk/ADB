# WPF Patterns Reference

## Contents
- MVVM Structure in This Repo
- Theming: DynamicResource and AdbUi.Theme
- WARNING: Menu and ComboBox Template Anti-Patterns
- WARNING: Hardcoded Colors
- Data Binding Best Practices
- DPI and Coordinate Safety

---

## MVVM Structure in This Repo

ViewModels live in `*.Core` projects (no WPF references). WPF projects hold only `.xaml` and minimal code-behind.

```
BotBuilder.Core/BotEditorViewModel.cs   ← logic, commands, state
BotBuilder/MainWindow.xaml              ← binds to BotEditorViewModel
BotBuilder/MainWindow.xaml.cs           ← sets DataContext, handles focus/animation only
```

**DO:** Put all command logic, property state, and validation in `*.Core`.  
**DON'T:** Write business logic in `.xaml.cs`. It becomes untestable and entangled with WPF lifecycle.

---

## Theming: DynamicResource and AdbUi.Theme

`AdbUi.Theme` provides named brushes. All color references MUST use `DynamicResource` so theme switches propagate at runtime.

```xml
<!-- DO -->
<Border Background="{DynamicResource WindowBackgroundBrush}"
        BorderBrush="{DynamicResource BorderBrush}" />

<!-- DON'T -->
<Border Background="#1E1E1E" />
```

**Why:** `StaticResource` resolves once at load time. Theme switches after startup are invisible to static resources, causing half-themed UI that's nearly impossible to debug.

---

### WARNING: Menu and ComboBox Template Anti-Patterns

**The Problem:**

```xml
<!-- BAD — Menu with only Style setters -->
<Menu>
    <Menu.Style>
        <Style TargetType="Menu">
            <Setter Property="Background" Value="{DynamicResource MenuBackgroundBrush}" />
        </Style>
    </Menu.Style>
</Menu>
```

**Why This Breaks:**
1. WPF's default `Menu` and `MenuItem` `ControlTemplate` hardcodes system colors internally — `Background` setters on the style are overridden by the template.
2. The popup portion of `ComboBox` uses a separate adorner-layer template entirely unaffected by item styles.
3. Result: menus and dropdowns render in system colors regardless of your theme brushes.

**The Fix:** Provide full `ControlTemplate` overrides for `Menu`, `MenuItem`, and `ComboBox`. See `AdbUi.Theme/` for the existing templates already built for this repo — reuse them, don't duplicate.

```xml
<!-- DO — reference the theme's control template -->
<MenuItem Style="{DynamicResource ThemedMenuItemStyle}" />
```

---

### WARNING: DisplayMemberPath Bypasses Templates

**The Problem:**

```xml
<!-- BAD -->
<ComboBox ItemsSource="{Binding Targets}" DisplayMemberPath="Name" />
```

**Why This Breaks:**
1. `DisplayMemberPath` generates an internal `TextBlock` outside your `DataTemplate`/`ItemTemplate` — theme brushes on items are ignored.
2. The selection box (the part showing the selected item) uses a separate content presenter that also bypasses your template.
3. Themed foreground/background won't apply to the displayed text.

**The Fix:**

```xml
<!-- DO — explicit ItemTemplate + ToString() on VM for selection box -->
<ComboBox ItemsSource="{Binding Targets}" SelectedItem="{Binding SelectedTarget}">
    <ComboBox.ItemTemplate>
        <DataTemplate>
            <TextBlock Text="{Binding DisplayName}"
                       Foreground="{DynamicResource ForegroundBrush}" />
        </DataTemplate>
    </ComboBox.ItemTemplate>
</ComboBox>
```

And override `ToString()` on the item ViewModel so the selection box renders correctly:

```csharp
// In TargetViewModel.cs (existing pattern established in PR #48)
public override string ToString() => DisplayName;
```

---

## Data Binding Best Practices

**Use `UpdateSourceTrigger=PropertyChanged` for text inputs** where you want immediate validation:

```xml
<TextBox Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}" />
```

**Diagnose binding errors** — silent failures are WPF's default behavior. In development, set:

```xml
<!-- In App.xaml.cs or debug launch -->
PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
```

Or check Output window for `BindingExpression path error` messages — these are always bugs, never warnings to ignore.

**Two-way binding on non-string types** requires a converter or matching property type:

```xml
<CheckBox IsChecked="{Binding IsEnabled, Mode=TwoWay}" />
```

---

## DPI and Coordinate Safety

This repo sets `PerMonitorV2` DPI awareness. Coordinates from WPF layout (logical pixels) differ from Win32 screen coordinates (physical pixels).

```csharp
// new code to add — when converting WPF coords to screen coords
var source = PresentationSource.FromVisual(this);
double dpiX = source.CompositionTarget.TransformToDevice.M11;
double dpiY = source.CompositionTarget.TransformToDevice.M22;
int screenX = (int)(logicalX * dpiX);
int screenY = (int)(logicalY * dpiY);
```

**NEVER** pass raw WPF `Canvas` coordinates directly to Win32 input APIs — they will be off on non-100% DPI displays. See the **csharp** skill for Win32 interop patterns.