# WPF Workflows Reference

## Contents
- Adding a New Dialog
- Adding a New ViewModel Property with Binding
- Adding a New Themed Control
- Checklist: New WPF Feature
- Debugging Binding Failures
- Validating Theme Correctness

---

## Adding a New Dialog

Dialogs follow the Owner/ShowDialog pattern. Logic lives in a `*.Core` ViewModel.

**Copy this checklist:**
- [ ] Create `MyDialogViewModel.cs` in the appropriate `*.Core` project (no WPF references)
- [ ] Create `MyDialog.xaml` + `MyDialog.xaml.cs` in the WPF project
- [ ] Set `DataContext` in the dialog constructor: `DataContext = new MyDialogViewModel();`
- [ ] Expose a `Result` property on the ViewModel for the caller to read
- [ ] Set `DialogResult = true` in a confirm command (code-behind acceptable here)
- [ ] Set `Owner` at call site before `ShowDialog()`
- [ ] Apply themed styles: `Background="{DynamicResource WindowBackgroundBrush}"` on root element

```csharp
// MyDialog.xaml.cs — minimal code-behind
public MyDialog()
{
    InitializeComponent();
    DataContext = new MyDialogViewModel();
}

public MyDialogViewModel ViewModel => (MyDialogViewModel)DataContext;
```

```csharp
// Call site
var dlg = new MyDialog { Owner = this };
if (dlg.ShowDialog() == true)
    UseResult(dlg.ViewModel.Result);
```

---

## Adding a New ViewModel Property with Binding

1. Add property to ViewModel in `*.Core`:

```csharp
private string _label = "";
public string Label
{
    get => _label;
    set { _label = value; OnPropertyChanged(); }
}
```

2. Bind in XAML:

```xml
<TextBlock Text="{Binding Label}" />
```

3. Validate: run `dotnet build ADB.slnx` and check Output for `BindingExpression path error`.

**Iterate until pass:**
1. Add property and binding
2. `dotnet build ADB.slnx`
3. If binding errors appear in Output at runtime, fix the property name mismatch
4. Only proceed when Output is clean

---

## Adding a New Themed Control

Controls must use `DynamicResource` brushes from `AdbUi.Theme`. If an existing brush covers your need, use it. Only add new brushes to `AdbUi.Theme` if no existing brush fits.

```xml
<!-- new code to add — standard themed panel -->
<Border Background="{DynamicResource PanelBackgroundBrush}"
        BorderBrush="{DynamicResource BorderBrush}"
        BorderThickness="1"
        CornerRadius="4"
        Padding="8">
    <TextBlock Text="{Binding SomeText}"
               Foreground="{DynamicResource ForegroundBrush}" />
</Border>
```

For `ComboBox` or `ListBox` with item templates — see [patterns.md](patterns.md) WARNING sections. Full `ControlTemplate` is required; style setters alone are insufficient.

---

## Checklist: New WPF Feature

Copy this checklist for any non-trivial WPF feature:

- [ ] ViewModel created in `*.Core` (no `using System.Windows` references)
- [ ] All commands are `ICommand` implementations in `*.Core`
- [ ] All colors use `DynamicResource` brush references from `AdbUi.Theme`
- [ ] `ComboBox`/`ListBox` use explicit `ItemTemplate`, not `DisplayMemberPath`
- [ ] Item ViewModels override `ToString()` if used in a ComboBox selection box
- [ ] Dialogs set `Owner` before `ShowDialog()`
- [ ] Any screen coordinates are DPI-scaled before passing to Win32
- [ ] `dotnet build ADB.slnx` passes with no binding errors in Output
- [ ] Feature tested in both Light and Dark themes manually

---

## Debugging Binding Failures

WPF binding failures are silent by default — the UI just shows nothing.

**Step 1:** Check Visual Studio / Rider Output window for:
```
BindingExpression path error: 'PropertyName' property not found on 'Namespace.ClassName'
```

**Step 2:** Enable trace for a specific binding:
```xml
<TextBlock Text="{Binding SomeProp,
    diag:PresentationTraceSources.TraceLevel=High}"
    xmlns:diag="clr-namespace:System.Diagnostics;assembly=WindowsBase" />
```

**Step 3:** Verify `DataContext` is set. The most common cause of "binding works in preview but not at runtime" is `DataContext` being null because the ViewModel constructor threw.

**Step 4:** For `ICommand` bindings that silently do nothing — check `CanExecute`. If it returns `false`, the button is disabled and no click fires.

---

## Validating Theme Correctness

After any UI change, verify both themes visually:

1. Launch BotBuilder
2. Switch to Dark theme via the theme menu
3. Check: no hardcoded colors, no white-on-white or black-on-black text, menus/dropdowns themed
4. Switch to Light theme
5. Check same elements
6. Switch to High Contrast if applicable

**Anti-flash baseline:** The app sets the dark theme before the window renders (established in PR #48). Don't add any pre-window initialization that changes colors — it will cause a visible flash on startup.