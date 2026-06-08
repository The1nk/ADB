# OpenCvSharp Patterns Reference

## Contents
- Mat Lifetime Management
- Match Mode Selection
- Coordinate Mapping
- Anti-Patterns
- Integration with Screen Capture

---

## Mat Lifetime Management

`Mat` wraps unmanaged OpenCV memory. EVERY `Mat` must be disposed.

```csharp
// GOOD — using declarations ensure disposal even on exception
using var template = Cv2.ImDecode(templateBytes, ImreadModes.Color);
using var frame = Cv2.ImDecode(frameBytes, ImreadModes.Color);
using var result = new Mat();
Cv2.MatchTemplate(frame, template, result, TemplateMatchModes.CCoeffNormed);
```

### WARNING: Undisposed Mat Causes Native Memory Leaks

**The Problem:**
```csharp
// BAD — no using, leaks unmanaged memory on every call
var template = Cv2.ImDecode(templateBytes, ImreadModes.Color);
var frame = Cv2.ImDecode(frameBytes, ImreadModes.Color);
```

**Why This Breaks:**
1. `Mat` allocates on the native heap; the GC does not collect it.
2. In a bot loop running hundreds of iterations, this silently exhausts memory.
3. Symptoms appear far from the cause — process crash or OOM, not here.

**The Fix:** Always `using var` or explicit `mat.Dispose()` in `finally`.

---

## Match Mode Selection

| Mode | Use Case | Notes |
|------|----------|-------|
| `CCoeffNormed` | General template detection | Best for UI screenshots; result in [-1, 1], match near 1.0 |
| `SqDiffNormed` | Pixel-perfect match | Match near 0.0 (inverse); less robust to lighting |
| `CorrNormed` | Brightness-tolerant | Less common; use CCoeffNormed unless you have reason |

**ALWAYS use `CCoeffNormed` as the default.** Its normalized output (0–1) makes threshold tuning predictable across different screens and templates.

---

## Coordinate Mapping

OpenCV match coordinates are relative to the frame passed to `MatchTemplate`. In this project, frames come from either Win32 window capture or Android ADB screenshot — the coordinate spaces differ.

```csharp
// new code to add — map OpenCV result back to screen/device coords
// maxLoc is top-left of the match rectangle in frame-space
var matchRect = new Rect(maxLoc, new Size(template.Width, template.Height));
var centerInFrame = new Point(
    matchRect.X + matchRect.Width / 2,
    matchRect.Y + matchRect.Height / 2);

// For Android: frame is the full device screenshot, centerInFrame IS device coords
// For Windows: frame is the captured window client area; add window origin for screen coords
```

See `BotBuilder.Core/Picker/CoordinateMapping.cs` for existing coordinate translation logic.

---

## Anti-Patterns

### WARNING: Passing RGB Byte Arrays to ImDecode

**The Problem:**
```csharp
// BAD — if bytes come from System.Drawing.Bitmap.LockBits (RGB), colors will mismatch
var mat = Cv2.ImDecode(rgbBytes, ImreadModes.Color); // OpenCV expects BGR
```

**Why This Breaks:**
1. OpenCV's `ImDecode` expects JPEG/PNG encoded bytes, not raw pixel data.
2. If you pass raw pixel bytes from `Bitmap`, red and blue channels swap silently.
3. Template and frame end up in different color spaces → match confidence plummets.

**The Fix:** Encode to PNG first, or convert color order explicitly:
```csharp
// new code to add — encode Bitmap to PNG bytes before ImDecode
using var ms = new MemoryStream();
bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
using var mat = Cv2.ImDecode(ms.ToArray(), ImreadModes.Color);
```

### WARNING: Threshold Too Low

A threshold below `0.80` will produce false positives on UI screenshots with repeated elements (buttons, icons, list items). Start at `0.90`, tune down only with evidence.

---

## Integration with Screen Capture

The matcher sits downstream of `AdbCore/Screen/` capture:

```
Win32WindowCapture / ADB screenshot
        ↓  (byte[])
OpenCvSharpTemplateMatcher.Match(frameBytes, templateBytes, threshold)
        ↓  (MatchResult: found, confidence, location)
Action executor (Click, Assert, etc.)
```

Template images are loaded from disk (`.png`) relative to the `.bot` file's asset path. See `AdbCore/Actions/BuiltIn/` image-matching actions for how template paths are resolved at execution time.

For OCR on matched regions, see the **tesseract** skill.