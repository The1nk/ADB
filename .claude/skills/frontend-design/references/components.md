# Components Reference

## Contents
- ComboBox (Full Template Required)
- Menu / MenuItem (Full Template Required)
- Disabled Palette Items
- Node Canvas Controls
- Dialog Windows

---

## ComboBox — Full Template Required

WPF ComboBox has two visual surfaces: the **selection box** (collapsed state) and the **popup dropdown**. Setters alone cannot theme both. A `ControlTemplate` is mandatory.

Additionally, if items are view-model objects, the selection box displays the result of `ToString()`. `DisplayMemberPath` only affects the dropdown items, not the selection box. **Always override `ToString()` on item VMs used in ComboBoxes.**

```csharp
// In TargetViewModel or picker VM — new code to add if missing
public override string ToString() => DisplayName ?? "Unknown";
```

```xml
<!-- GOOD — full template covering both popup and selection box -->
<Style TargetType="ComboBox">
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="ComboBox">
                <Grid>
                    <ToggleButton ...
                        Background="{DynamicResource PanelBackgroundBrush}"
                        BorderBrush="{DynamicResource BorderBrush}" />
                    <ContentPresenter ... /> <!-- selection box -->
                    <Popup ...>
                        <Border Background="{DynamicResource PanelBackgroundBrush}"
                                BorderBrush="{DynamicResource BorderBrush}">
                            <ItemsPresenter />
                        </Border>
                    </Popup>
                </Grid>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

See the **wpf** skill for full ComboBox template boilerplate.

---

## Menu / MenuItem — Full Template Required

WPF Menu and MenuItem ignore brush setters for their popup chrome. A `ControlTemplate` must replace the default visual tree. Confirmed required by PR #44 (`AdbUi.Theme` Menu/MenuItem TEMPLATE fix).

```xml
<!-- BAD — setters do not theme popup background -->
<Style TargetType="MenuItem">
    <Setter Property="Background" Value="{DynamicResource PanelBackgroundBrush}" />
</Style>

<!-- GOOD — template replaces popup chrome -->
<Style TargetType="MenuItem">
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="MenuItem">
                <!-- full visual tree including Popup -->
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

---

## Disabled Palette Items (Soft-Grey)

Palette items are greyed when their required dependency (Android/Browser) is unavailable, detected via `IDependencyProbe`. Do not use `IsEnabled=False` alone — the disabled brush must be theme-aware.

```xml
<!-- Palette item visual state for unavailable actions -->
<Style TargetType="local:PaletteItem">
    <Style.Triggers>
        <DataTrigger Binding="{Binding IsAvailable}" Value="False">
            <Setter Property="Foreground" Value="{DynamicResource DisabledForegroundBrush}" />
            <Setter Property="IsEnabled" Value="False" />
        </DataTrigger>
    </Style.Triggers>
</Style>
```

---

## Node Canvas Controls

Canvas nodes in `BotBuilder` are positioned absolutely. Do not use `Margin` for layout — use `Canvas.Left` / `Canvas.Top` bound to `NodeViewModel.X` / `NodeViewModel.Y`.

```xml
<!-- GOOD — canvas-absolute positioning -->
<ItemsControl ItemsSource="{Binding Nodes}">
    <ItemsControl.ItemContainerStyle>
        <Style TargetType="ContentPresenter">
            <Setter Property="Canvas.Left" Value="{Binding X}" />
            <Setter Property="Canvas.Top" Value="{Binding Y}" />
        </Style>
    </ItemsControl.ItemContainerStyle>
    <ItemsControl.ItemsPanel>
        <ItemsPanelTemplate><Canvas /></ItemsPanelTemplate>
    </ItemsControl.ItemsPanel>
</ItemsControl>
```

---

## Dialog Windows

All dialogs (CoordinatePickerDialog, TargetPickerDialog, SelectorPickerDialog) must initialize theme before `ShowDialog()`. Do not rely on inheritance from owner window — WPF does not propagate `DynamicResource` across window boundaries automatically.

```csharp
// new code to add — before ShowDialog()
ThemeManager.Apply(ThemeManager.Current, dialog);
dialog.ShowDialog();
```