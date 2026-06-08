# Forms Reference

## Contents
- Forms in ADB
- Properties Panel Pattern
- Config Field Validation
- Picker Integration
- DO / DON'T Pairs
- WARNING: Validation Anti-Patterns
- Checklist

## Forms in ADB

"Forms" in ADB are the Properties panel (`PropertiesViewModel`, `ConfigFieldViewModel`) and dialog inputs (TargetPicker, SelectorPicker, CoordinatePicker). They configure `BotAction` instances with typed `IActionField` values.

## Properties Panel Pattern

Key files: `BotBuilder.Core/Properties/PropertiesViewModel.cs`, `BotBuilder.Core/Properties/ConfigFieldViewModel.cs`

Each selected node exposes its `IActionDefinition.Fields` as `ConfigFieldViewModel` instances. The panel binds to these via `ItemsControl`.

```csharp
// Existing pattern — ConfigFieldViewModel wraps a field value
public class ConfigFieldViewModel : INotifyPropertyChanged
{
    public string Label { get; }
    public object? Value { get; set; } // triggers OnPropertyChanged + validation
}
```

### Field validation rule

Client-side validation is for guidance and speed only — the executor validates at runtime. Show inline errors early but never trust client validation alone.

```csharp
// new code to add
public string? ValidationError =>
    Field.IsRequired && string.IsNullOrWhiteSpace(Value?.ToString())
        ? $"{Label} is required."
        : null;
```

```xml
<!-- new code to add — inline error under each field -->
<TextBlock Text="{Binding ValidationError}"
           Foreground="{StaticResource ErrorBrush}"
           Visibility="{Binding ValidationError, Converter={StaticResource NullToCollapsed}}"
           FontSize="11" />
```

## Config Field Types

ADB fields are typed: string, int, bool, enum, image path, selector string, target reference. Each type needs appropriate input control:

| Field type | Control | Notes |
|------------|---------|-------|
| string | `TextBox` | Multi-line if content is a script |
| int | `TextBox` with numeric filter | Validate on lost focus |
| bool | `CheckBox` | Label right of checkbox |
| enum | `ComboBox` | Use item VM `ToString()` for display |
| image path | `TextBox` + Browse button | Show preview thumbnail |
| selector | `TextBox` + Pick button → `SelectorPickerDialog` | Syntax-highlight hint |
| target ref | `ComboBox` bound to `TargetBarViewModel.Targets` | |

## Picker Integration

When a field needs a dialog picker, the VM opens the dialog and writes back only on confirm:

```csharp
// new code to add
public ICommand PickSelectorCommand => new RelayCommand(_ =>
{
    var dialog = new SelectorPickerDialog(CurrentValue);
    if (dialog.ShowDialog() == true)
        Value = dialog.SelectedSelector; // write-back only on confirm
});
```

**NEVER write back in the dialog's constructor or on selection-changed** — only on explicit confirm.

## DO / DON'T Pairs

**DO** — show required field error inline, not as a popup:
```xml
<!-- new code to add -->
<Border BorderBrush="{StaticResource ErrorBrush}" BorderThickness="1"
        Visibility="{Binding ValidationError, Converter={StaticResource NullToCollapsed}}">
    <TextBox Text="{Binding Value, UpdateSourceTrigger=PropertyChanged}" />
</Border>
```

**DON'T** — block form submission with a `MessageBox`:
```csharp
// BAD — interrupts flow, provides no inline context
if (string.IsNullOrEmpty(field.Value))
    MessageBox.Show("Field is required.");
```

**DO** — disable the confirm/OK button when required fields are invalid:
```csharp
// new code to add
public bool CanConfirm => Fields.All(f => f.ValidationError is null);
```

**DON'T** — allow silent empty submits that fail at runtime with no user feedback.

### WARNING: Validation Anti-Patterns

**The Problem:**
```csharp
// BAD — validates only on submit, no inline feedback
private void OkButton_Click(object sender, RoutedEventArgs e)
{
    if (!Validate()) return;
}
```

**Why This Breaks:**
1. User fills 5 fields, clicks OK, gets a generic error — has to find which field is wrong.
2. No `CanExecute` means the button is always enabled, giving false confidence.
3. Executor-level errors look the same as validation errors to the user.

**The Fix:** Validate on `PropertyChanged`, show inline, disable confirm via `CanExecute`.

## Checklist

- [ ] Required fields show inline error when empty (not modal popup)
- [ ] Confirm/OK button disabled when any required field is invalid
- [ ] Picker dialogs write back only on explicit confirm
- [ ] Enum fields use ComboBox with item VM `ToString()` for display
- [ ] Numeric fields reject non-numeric input on lost focus
- [ ] Client validation is for guidance only; executor validates at runtime