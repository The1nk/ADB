# Microcopy Reference

## Contents
- Microcopy Principles for ADB
- Action Labels
- Error Messages
- Empty States
- Tooltips and Help Text
- Dialog Titles and Confirmations
- WARNING: Anti-Patterns
- Checklist

## Microcopy Principles for ADB

ADB is a developer tool. Users are technically capable but time-constrained. Copy must be:
- **Specific**: name the failing thing ("ADB device 'emulator-5554'" not "Device error")
- **Actionable**: say what to do next ("Connect a device via USB or start an emulator")
- **Honest**: never claim success before it is confirmed

## Action Labels

Use verb-noun pairs. Be consistent — pick one term and use it everywhere.

| Surface | DO | DON'T |
|---------|-----|-------|
| Run button | "Run" | "Execute", "Start", "Go" |
| Stop button | "Stop" | "Cancel", "Abort", "Kill" |
| Picker confirm | "Select" | "OK", "Choose", "Pick" |
| Dialog confirm | "Save" / "Apply" | "OK" (ambiguous) |
| Dialog dismiss | "Cancel" | "Close", "Back", "No" |
| Palette add | "Add to canvas" (tooltip) | "Insert", "Place" |

```xml
<!-- new code to add — consistent label on TargetPickerDialog -->
<Button Content="Select" IsDefault="True" Command="{Binding ConfirmCommand}"
        AutomationProperties.Name="Select target" />
<Button Content="Cancel" IsCancel="True" Command="{Binding CancelCommand}" />
```

## Error Messages

Structure: **What failed** + **Why (if known)** + **What to do**.

| Scenario | Message |
|----------|---------|
| ADB not on PATH | "ADB is not available. Add ADB to your PATH and restart the application." |
| No Android devices | "No Android devices found. Connect a device via USB or start an emulator." |
| No windows found | "No open windows found. Open the target application and click Refresh." |
| Image match failed | "Template image not found on screen. Check the template image and target region." |
| Selector not found | "Selector '{selector}' did not match any element within {timeout}ms." |
| Bot file corrupt | "Could not load bot file. The file may be corrupted or from an incompatible version." |

```csharp
// new code to add — interpolate specifics into error messages
catch (AdbException ex)
{
    ErrorMessage = $"ADB error on device '{TargetDevice}': {ex.Message}. " +
                   "Check that the device is connected and ADB is authorized.";
}
```

**NEVER expose stack traces or internal exception types to the user.**

## Empty States

Every collection-based surface needs a non-blank empty state:

| Surface | Empty message |
|---------|---------------|
| Canvas (no nodes) | "Drag an action from the palette to get started." |
| Palette (all greyed) | "Install ADB or Playwright to enable more actions." |
| Target picker (no devices) | "No Android devices found. Connect a device or start an emulator." |
| Run log (no runs yet) | "Run the bot with F5 to see output here." |
| Properties panel (no selection) | "Select an action node to configure it." |

```xml
<!-- new code to add — properties panel empty state -->
<TextBlock Text="Select an action node to configure it."
           Visibility="{Binding SelectedNode, Converter={StaticResource NullToVisible}}"
           HorizontalAlignment="Center" VerticalAlignment="Center"
           Foreground="{StaticResource SubtleForegroundBrush}" />
```

## Tooltips and Help Text

- Every palette item: tooltip = action description from `IActionDefinition.Description`
- Every greyed palette item: tooltip = "Requires [dependency name]"
- Every toolbar icon button: tooltip = "Label (Shortcut)" e.g. "Reset zoom (Ctrl+0)"
- Config fields with non-obvious format: `ToolTip` or placeholder text

```xml
<!-- new code to add — greyed palette item tooltip -->
<ListBoxItem ToolTip="{Binding IsAvailable,
             Converter={StaticResource AvailabilityToTooltip},
             ConverterParameter={Binding ActionDefinition.DependencyName}}" />
```

## Dialog Titles and Confirmations

Dialog titles name the object being acted on, not the action:
- **DO**: "Select Target" / "Edit Selector" / "Pick Region"
- **DON'T**: "Dialog" / "Options" / "Settings"

Destructive confirmations must name the object:
- **DO**: "Delete node 'Click Button'? This cannot be undone."
- **DON'T**: "Are you sure?"

```xml
<!-- new code to add — delete confirmation -->
<Button Content="Delete node" Command="{Binding DeleteCommand}"
        ToolTip="{Binding SelectedNode.DisplayName,
                  StringFormat='Delete node \'{0}\' (cannot be undone)'}" />
```

### WARNING: Anti-Patterns

**Premature success messaging:**
```csharp
// BAD — tells user "Done" before async op completes
StatusText = "Saved!";
await SaveAsync(); // if this throws, the lie is already shown
```
```csharp
// GOOD
await SaveAsync();
StatusText = "Saved."; // only after confirmed success
```

**Generic error copy:**
```
// BAD
"An error occurred."

// GOOD
"Could not connect to device 'emulator-5554'. Check that the emulator is running."
```

**Action labels that lie:**
```xml
<!-- BAD — "OK" on a destructive action gives no warning -->
<Button Content="OK" Command="{Binding DeleteAllCommand}" />

<!-- GOOD -->
<Button Content="Delete all nodes" Command="{Binding DeleteAllCommand}" />
```

## Checklist

- [ ] Every button uses verb-noun label (not "OK")
- [ ] Error messages name the failing object and suggest next action
- [ ] No stack traces or internal exception types shown to user
- [ ] Every empty collection has a guidance sentence (not blank space)
- [ ] Destructive confirmations name the object being deleted
- [ ] Success messages appear only after the operation is confirmed complete
- [ ] Greyed/disabled controls have a tooltip explaining why
- [ ] Tooltip on icon buttons includes keyboard shortcut