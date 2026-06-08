# Tesseract Patterns Reference

## Contents
- Engine Lifetime
- Abstraction Layer
- Page Segmentation Modes
- Preprocessing Pipeline
- Anti-Patterns

---

## Engine Lifetime

`TesseractEngine` is expensive to construct (loads the `.traineddata` model). Constructing one per recognition call tanks throughput and causes memory spikes.

```csharp
// new code to add — register as singleton in DI
services.AddSingleton<IOcrEngine>(sp =>
    new TesseractOcrEngine(
        tessDataPath: Path.Combine(AppContext.BaseDirectory, "assets", "tessdata"),
        language: "eng",
        engineMode: EngineMode.LstmOnly));
```

The `IOcrEngine` abstraction (in `AdbCore/Ocr/`) decouples action executors from the concrete Tesseract type, enabling test fakes. See the **xunit** skill for the fake pattern used in this project.

---

## Abstraction Layer

Action executors in `AdbCore/Actions/BuiltIn/` must never reference `Tesseract.TesseractEngine` directly — always go through `IOcrEngine`.

```csharp
// GOOD — depends on abstraction
public sealed class ReadTextExecutor : IActionExecutor
{
    private readonly IOcrEngine _ocr;
    public ReadTextExecutor(IOcrEngine ocr) => _ocr = ocr;
}

// BAD — leaks Tesseract dependency into executor layer, breaks unit tests
public sealed class ReadTextExecutor : IActionExecutor
{
    private readonly TesseractEngine _engine = new("assets/tessdata", "eng");
}
```

**Why this matters:** Direct `TesseractEngine` usage requires the physical `tessdata/` directory to exist during tests, coupling test execution to the build output layout. The fake pattern (per project convention in `AdbCore.Tests/`) avoids this entirely.

---

## Page Segmentation Modes

Choosing the wrong `PageSegMode` is the most common cause of garbled output on UI screenshots.

| Scenario | Mode | Why |
|----------|------|-----|
| Single line label / button text | `PageSegMode.SingleLine` | Avoids multi-column layout analysis on short strings |
| Multi-line dialog / paragraph | `PageSegMode.Auto` | Full layout analysis |
| Single word (health value, counter) | `PageSegMode.SingleWord` | Fastest; skips line detection entirely |
| Sparse UI elements | `PageSegMode.SparseText` | Finds text islands without assuming layout |

```csharp
// new code to add — pass mode through IOcrEngine.Recognize overload
var text = _ocr.Recognize(bitmap, PageSegMode.SingleLine);
```

---

## Preprocessing Pipeline

Raw screenshots from Windows captures or Android ADB frames are often low-contrast, scaled, or have UI chrome. OCR accuracy on unprocessed images is poor.

**Recommended pipeline using OpenCvSharp4** (already a project dependency — see **opencvsharp** skill):

```csharp
// new code to add
using var src = OpenCvSharp.Mat.FromStream(bitmapStream, OpenCvSharp.ImreadModes.Color);
using var gray = new OpenCvSharp.Mat();
using var thresh = new OpenCvSharp.Mat();

OpenCvSharp.Cv2.CvtColor(src, gray, OpenCvSharp.ColorConversionCodes.BGR2GRAY);
OpenCvSharp.Cv2.Threshold(gray, thresh, 128, 255, OpenCvSharp.ThresholdTypes.Binary);

// Convert thresh Mat → Bitmap → pass to IOcrEngine
```

**When to skip preprocessing:** If the source region is already high-contrast black-on-white (e.g., a standard Windows dialog), preprocessing may degrade accuracy by introducing threshold artifacts. Test both paths.

---

## Anti-Patterns

### WARNING: Constructing TesseractEngine Per Recognition

**The Problem:**
```csharp
// BAD — new engine on every call
public string Recognize(Bitmap bmp)
{
    using var engine = new TesseractEngine("assets/tessdata", "eng", EngineMode.LstmOnly);
    // ...
}
```

**Why This Breaks:**
1. Model load time (~200-500ms) multiplies across every bot step that reads text.
2. Under concurrent execution, multiple engine constructions race for the same file handle on `eng.traineddata`.
3. Garbage collection pressure from repeated large allocations causes GC pauses visible as bot execution jitter.

**The Fix:** Singleton `IOcrEngine` registered in DI. See Engine Lifetime above.

---

### WARNING: Hardcoded `tessdata` Path

**The Problem:**
```csharp
// BAD — breaks when running from test output or different working directories
new TesseractEngine("assets/tessdata", "eng");
```

**Why This Breaks:** The working directory at runtime is not guaranteed to be the project root. Tests run from `bin/Debug/net10.0-windows/`; BotRunner may be invoked from any directory.

**The Fix:**
```csharp
// GOOD — always relative to the assembly
var tessdata = Path.Combine(AppContext.BaseDirectory, "assets", "tessdata");
new TesseractEngine(tessdata, "eng", EngineMode.LstmOnly);
```

Ensure `assets/tessdata/eng.traineddata` is marked `Copy to Output Directory: Always` in the project file.