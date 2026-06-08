# Aesthetics Reference

## Contents
- Visual Identity
- Color System / Theme Brushes
- Typography
- Dark Mode Defaults
- Anti-Patterns

---

## Visual Identity

ADB is a **developer tool**, not a consumer app. The aesthetic target is:
- Dense, functional, scannable — every pixel earns its space
- No decorative gradients, drop shadows, or hero imagery
- Dark theme is the default (anti-flash baseline set in PR #48)
- HighContrast mode must remain fully accessible — never rely on color alone to convey state

If a new surface looks like a generic "modern Windows app" with rounded cards and hero typography, it is wrong for this project.

---

## Color System / Theme Brushes

All colors are defined as named brushes in `AdbUi.Theme`. **Never hardcode hex values in XAML or code-behind.**

```xml
<!-- GOOD — reacts to theme switches -->
<Border Background="{DynamicResource WindowBackgroundBrush}" />

<!-- BAD — breaks Dark/HighContrast, causes flash on theme change -->
<Border Background="#1E1E1E" />
```

Semantic brush categories (verify names against `AdbUi.Theme/Brushes/`):
- **WindowBackgroundBrush** — app chrome / window background
- **PanelBackgroundBrush** — secondary panels, palette, properties
- **PrimaryTextBrush** — body text
- **BorderBrush** — control outlines
- **AccentBrush** — selection highlight, active node port
- **DisabledForegroundBrush** — soft-grey for unavailable palette items

---

## Typography

WPF uses system fonts; do not specify custom font families unless they ship with the installer.

```xml
<!-- GOOD — inherits system UI font, respects user accessibility settings -->
<TextBlock FontSize="12" Foreground="{DynamicResource PrimaryTextBrush}" Text="Action Name" />

<!-- BAD — hardcoded font may be missing, ignores theme foreground -->
<TextBlock FontFamily="Segoe UI Variable" FontSize="13" Foreground="White" Text="Action Name" />
```

- Body / label text: 12px
- Section headers (palette categories, panel titles): 11px Bold or SmallCaps
- Node titles on canvas: 12px, clipped to node width
- No emoji in WPF text — the WPF text pipeline does not render color emoji reliably

---

## Dark Mode Defaults

Dark theme is the default (PR #48 anti-flash baseline). New windows and dialogs must:
1. Initialize theme before `Show()` / `ShowDialog()` via `ThemeManager`
2. Use `DynamicResource` on ALL brush/color properties
3. Never set a `Background` on `Window` in XAML without a theme brush — this causes a white flash before theme applies

---

## Anti-Patterns

### WARNING: Hardcoded Colors

**The Problem:**
```xml
<!-- BAD — hardcoded color ignores theme -->
<TextBlock Foreground="#555555" />
```
**Why This Breaks:** HighContrast mode requires system-defined colors; hardcoded values fail accessibility and look wrong in Light theme.
**The Fix:** Use `{DynamicResource PrimaryTextBrush}` or the appropriate semantic brush.

### WARNING: No Emoji / Unicode Decorations

**The Problem:**
```xml
<!-- BAD — color emoji renders as blank or box in WPF -->
<TextBlock Text="✅ Connected" />
```
**Why This Breaks:** WPF's text stack does not support color emoji (Segoe UI Emoji color glyphs). Use text labels or Path/Image icons instead.