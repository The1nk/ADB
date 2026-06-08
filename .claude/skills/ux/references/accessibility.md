# Accessibility Reference

## Contents
- WPF Accessibility Baseline
- Required Automation Properties
- Keyboard Navigation
- Focus Management in Dialogs
- Contrast and Theme
- WARNING: Anti-Patterns
- Checklist

## WPF Accessibility Baseline

WPF exposes UI Automation (UIA) automatically for native controls. Custom controls and Canvas-drawn elements get nothing for free. ADB's canvas nodes (`NodeViewModel` rendered as custom ItemsControl items) and picker dialogs must explicitly set automation properties.

## Required Automation Properties

Every interactive control must have an accessible name. Set it in XAML:

```xml
<!-- new code to add — canvas node item -->
<Border AutomationProperties.Name="{Binding DisplayName}"
        AutomationProperties.HelpText="{Binding ActionDefinition.Description}"
        Focusable="True">
    ...
</Border>
```

```xml
<!-- new code to add — icon-only toolbar button -->
<Button Command="{Binding RunCommand}"
        AutomationProperties.Name="Run bot"
        AutomationProperties.AcceleratorKey="F5"
        ToolTip="Run bot (F5)">
    <Image Source="/Assets/run.png" />
</Button>
```

```xml
<!-- new code to add — palette item -->
<ListBoxItem AutomationProperties.Name="{Binding DisplayName}"
             AutomationProperties.IsOffscreen="{Binding IsAvailable, Converter={StaticResource BoolToOffscreen}}" />
```

## Keyboard Navigation

All dialogs must be fully keyboard-operable:

| Action | Key | XAML mechanism |
|--------|-----|----------------|
| Confirm dialog | Enter | `IsDefault="True"` on OK button |
| Cancel dialog | Escape | `IsCancel="True"` on Cancel button |
| Canvas shortcuts | Ctrl+0, Ctrl+Shift+0 | `InputBindings` on canvas `Window` |
| Move between fields | Tab / Shift+Tab | WPF default tab order; set `TabIndex` when order is non-obvious |

```xml
<!-- Existing pattern — keyboard shortcuts on BotBuilder MainWindow -->
<Window.InputBindings>
    <KeyBinding Key="D0" Modifiers="Ctrl" Command="{Binding ResetZoomCommand}" />
    <KeyBinding Key="D0" Modifiers="Ctrl+Shift" Command="{Binding FitToNodesCommand}" />
</Window.InputBindings>
```

## Focus Management in Dialogs

When a dialog opens, focus must land on a meaningful control (not the window chrome):

```csharp
// new code to add — in dialog code-behind
protected override void OnContentRendered(EventArgs e)
{
    base.OnContentRendered(e);
    PrimaryInputControl.Focus(); // e.g., the search TextBox in TargetPickerDialog
}
```

When a dialog closes, focus must return to the element that triggered it:

```csharp
// new code to add
var focusedElement = Keyboard.FocusedElement;
dialog.ShowDialog();
(focusedElement as UIElement)?.Focus();
```

## Contrast and Theme

ADB uses `AdbUi.Theme` for Light/Dark/HighContrast themes. See the **wpf** skill for `ThemeManager` usage.

- **NEVER** hardcode colors — use named brush resources.
- High-contrast mode must remain usable: test with Windows High Contrast Black.
- Disabled controls use `{StaticResource DisabledForegroundBrush}` — never hand-roll opacity tricks that break HC mode.

```xml
<!-- new code to add — disabled state using theme brush -->
<TextBlock Foreground="{Binding IsEnabled, RelativeSource={RelativeSource AncestorType=Control},
           Converter={StaticResource EnabledToForeground}}" />
```

### WARNING: Anti-Patterns

**The Problem:**
```xml
<!-- BAD — no accessible name, screen readers announce "Button" -->
<Button Width="24" Height="24">
    <Image Source="/Assets/delete.png" />
</Button>
```

**Why This Breaks:**
1. Screen readers announce "Button" with no context.
2. Automation tests cannot find the button by semantic name.
3. High-contrast mode may hide the icon entirely.

**The Fix:**
```xml
<!-- GOOD -->
<Button AutomationProperties.Name="Delete node" ToolTip="Delete node (Delete key)"
        Width="24" Height="24">
    <Image Source="/Assets/delete.png" />
</Button>
```

**The Problem:**
```csharp
// BAD — canvas items are not focusable, keyboard users cannot select nodes
<Border Background="..." MouseDown="...">
```

**Why This Breaks:** Canvas node selection is mouse-only. No keyboard path exists to select, move, or delete nodes.

**The Fix:** Set `Focusable="True"`, handle `KeyDown` for Delete/arrow keys on canvas items.

## Checklist

- [ ] Every interactive control has `AutomationProperties.Name`
- [ ] Icon-only buttons have `ToolTip` matching their automation name
- [ ] All dialogs have `IsDefault` and `IsCancel` buttons
- [ ] Dialog focus lands on primary input on open
- [ ] Focus returns to trigger element after dialog closes
- [ ] No hardcoded colors — all from `AdbUi.Theme` brush resources
- [ ] Canvas nodes are focusable with keyboard delete/move support
- [ ] Tab order is logical (set `TabIndex` when non-obvious)