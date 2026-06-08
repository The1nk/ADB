# Motion Reference

## Contents
- Motion Philosophy for Tooling UIs
- WPF Animation Primitives
- Acceptable Transitions
- Anti-Patterns
- HighContrast / Accessibility

---

## Motion Philosophy for Tooling UIs

ADB is a **developer tool**. Motion should be functional, not decorative:
- Use animation only to communicate state changes (loading, success, error)
- Never animate for aesthetics alone — it slows down power users
- Respect `SystemParameters.ClientAreaAnimation` — if the user has disabled animations in Windows, honor it

---

## WPF Animation Primitives

WPF provides `Storyboard`, `DoubleAnimation`, `ColorAnimation`, and `ObjectAnimationUsingKeyFrames`. For ADB, limit use to:

```xml
<!-- GOOD — subtle opacity fade for transient status messages -->
<Storyboard x:Key="FadeOutStatus">
    <DoubleAnimation Storyboard.TargetProperty="Opacity"
                     From="1" To="0" Duration="0:0:0.3"
                     BeginTime="0:0:2" />
</Storyboard>
```

```csharp
// new code to add — respect system animation preference
if (SystemParameters.ClientAreaAnimation)
{
    var sb = (Storyboard)FindResource("FadeOutStatus");
    sb.Begin(StatusText);
}
else
{
    StatusText.Visibility = Visibility.Collapsed;
}
```

---

## Acceptable Transitions

| Use Case | Animation | Duration |
|----------|-----------|----------|
| Status message fade-out | `DoubleAnimation` on Opacity | 300ms, 2s delay |
| Panel expand/collapse | `DoubleAnimation` on Height/Width | 150ms |
| Node selection highlight | `ColorAnimation` on BorderBrush | 80ms |
| Execution progress | `ProgressBar` indeterminate mode | N/A (native) |

For everything else: **no animation**. Instant state changes are correct for tooling.

---

## Anti-Patterns

### WARNING: Decorative Entrance Animations

**The Problem:**
```xml
<!-- BAD — bouncing palette items feel like a consumer app -->
<Storyboard x:Key="PaletteItemEntrance">
    <DoubleAnimationUsingKeyFrames Storyboard.TargetProperty="(TranslateTransform.Y)">
        <SplineDoubleKeyFrame KeyTime="0:0:0.4" Value="0" KeySpline="0.175,0.885,0.32,1.275" />
    </DoubleAnimationUsingKeyFrames>
</Storyboard>
```
**Why This Breaks:** Plays on every palette rebuild (e.g., after filter). Distracts during real work. Conflicts with professional tool aesthetic.
**The Fix:** Remove. Instant layout is correct here.

### WARNING: Animation Without System Preference Check

**The Problem:**
```csharp
// BAD — plays even when user disabled animations in Windows Accessibility settings
fadeStoryboard.Begin(element);
```
**Why This Breaks:** Violates Windows accessibility contract (`SystemParameters.ClientAreaAnimation`).
**The Fix:** Gate all animations behind `SystemParameters.ClientAreaAnimation`.

---

## HighContrast / Accessibility

In HighContrast mode, `ColorAnimation` targeting custom brush properties will override system colors and break accessibility. Only animate `Opacity` and `Transform` properties in HighContrast-safe scenarios.

```csharp
// new code to add — skip color animations in HighContrast
if (!SystemParameters.HighContrast)
{
    colorAnimation.Begin(element);
}
```