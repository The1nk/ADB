# Patterns Reference

## Contents
- DO / DON'T Pairs
- Visual Anti-Patterns for Tooling UIs
- Theme Integration Checklist
- New Surface Checklist

---

## DO / DON'T Pairs

### Brush Binding

**DO:** `{DynamicResource WindowBackgroundBrush}` — reacts to theme switches at runtime.
**DON'T:** `{StaticResource WindowBackgroundBrush}` for theme brushes — freezes at load time, ignores subsequent theme changes.

### Control Templating

**DO:** Write full `ControlTemplate` for ComboBox, Menu, MenuItem, ListBox — these WPF controls have internal chrome that ignores setter-only styles.
**DON'T:** Assume `<Setter Property="Background" .../>` themes popup chrome. It does not.

### ViewModel ToString()

**DO:** Override `ToString()` on every VM class bound to a ComboBox's `ItemsSource`.
**DON'T:** Rely on `DisplayMemberPath` to fix the selection box — it only affects dropdown items.

### Canvas Positioning

**DO:** Bind `Canvas.Left`/`Canvas.Top` to VM coordinates for node graph items.
**DON'T:** Use `Margin` or `Grid` layout inside the infinite canvas — it breaks pan/zoom math.

### Disabled State

**DO:** Pair `IsEnabled=False` with `{DynamicResource DisabledForegroundBrush}` via a `DataTrigger`.
**DON'T:** Use opacity hacks (e.g., `Opacity="0.4"`) — the result is inconsistent across themes and ignores the semantic disabled brush.

---

## Visual Anti-Patterns for Tooling UIs

### WARNING: Generic "Modern App" Aesthetic

Rounded cards, hero images, large whitespace, gradient backgrounds — these signal consumer/marketing surfaces. ADB is a professional tool. Any new UI that looks like a settings page from a consumer app is wrong.

### WARNING: Color-Only State Signaling

**The Problem:** Using only color to distinguish states (e.g., red = error, green = success) fails HighContrast mode and color-blind users.
**The Fix:** Always pair color with an icon, label, or pattern change:
```xml
<!-- GOOD — icon + color, not color alone -->
<StackPanel Orientation="Horizontal">
    <Path Data="{StaticResource ErrorIcon}" Fill="{DynamicResource ErrorBrush}" />
    <TextBlock Text="Connection failed" Foreground="{DynamicResource ErrorBrush}" />
</StackPanel>
```

### WARNING: Emoji as Icons

WPF does not render color emoji. Using ✅, ❌, ⚠️ in `TextBlock` produces blank boxes or monochrome glyphs on most systems. Use `Path`-based vector icons or `Image` with bundled assets.

### WARNING: Hardcoded Window Size

```csharp
// BAD — wrong on high-DPI monitors, wrong for non-primary monitors
this.Width = 1200;
this.Height = 800;
```
Set `Width`/`Height` as defaults in XAML, then let the user resize. Persist window bounds to user settings if the dialog is frequently used.

---

## Theme Integration Checklist

Copy this checklist when adding a new control or dialog:

- [ ] All `Background`, `Foreground`, `BorderBrush` use `{DynamicResource ...Brush}`
- [ ] ComboBox and Menu controls have full `ControlTemplate` (not setter-only styles)
- [ ] `ThemeManager.Apply(...)` called before `Show()`/`ShowDialog()` on new windows
- [ ] No hardcoded hex color values anywhere in XAML or code-behind
- [ ] No emoji characters in any `TextBlock` or string resources
- [ ] Disabled state uses `DisabledForegroundBrush` via `DataTrigger`
- [ ] Verified visually in Dark, Light, and HighContrast themes

---

## New Surface Checklist

Copy this checklist when building a new dialog or panel from scratch:

- [ ] Layout uses `Grid`/`DockPanel`, not `StackPanel` for main structure
- [ ] Button row is bottom-aligned with standard OK/Cancel sizing (80px wide)
- [ ] Tall content wrapped in `ScrollViewer` with `MaxHeight` cap
- [ ] Per-Monitor V2 DPI: no hardcoded pixel sizes for hit targets
- [ ] No animation without `SystemParameters.ClientAreaAnimation` gate
- [ ] `dotnet build ADB.slnx` produces zero binding errors after change

## Related Skills

See the **wpf** skill for WPF XAML and data-binding depth.
See the **ux** skill for interaction patterns and accessibility guidance.
See the **dotnet** skill for build and project structure.