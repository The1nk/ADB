# Tesseract Workflows Reference

## Contents
- Adding a New OCR Action
- Debugging OCR Accuracy
- Testing OCR Actions
- Wiring tessdata to Build Output

---

## Adding a New OCR Action

Copy this checklist and track progress:

- [ ] Define action config fields in `AdbCore/Actions/BuiltIn/` (region selector, output variable name, page seg mode)
- [ ] Implement `IActionExecutor` injecting `IOcrEngine` — never `TesseractEngine` directly
- [ ] Register executor in `ActionExecutorRegistry`
- [ ] Write xUnit test using a fake `IOcrEngine` — see **xunit** skill for project fake pattern
- [ ] Validate: `dotnet test ADB.slnx`
- [ ] Manual smoke-test in BotBuilder: drag action onto canvas, configure region, press F5

**Iterate-until-pass:**
1. Make changes
2. Validate: `dotnet test ADB.slnx`
3. Fix any failures, repeat step 2
4. Only proceed to manual test when all tests pass

---

## Debugging OCR Accuracy

When OCR returns empty string, garbage characters, or partial text:

**Step 1 — Isolate the image.** Save the captured bitmap to disk before passing to OCR:

```csharp
// new code to add — debug save, remove before merge
bitmap.Save(Path.Combine(Path.GetTempPath(), "ocr_debug.png"));
```

Open `ocr_debug.png` and visually inspect: Is the region correct? Is text visible and legible?

**Step 2 — Try preprocessing.** Run grayscale + threshold (see `references/patterns.md` preprocessing section). Save the preprocessed image and inspect again.

**Step 3 — Try a different PageSegMode.** For single-line UI labels, switch to `PageSegMode.SingleLine`. For scattered digits, try `PageSegMode.SparseText`.

**Step 4 — Scale up.** Tesseract performs best at ~300 DPI equivalent. If the captured region is small (e.g., a 30px-tall label), scale 2x–3x before OCR:

```csharp
// new code to add — scale via OpenCvSharp before OCR; see opencvsharp skill
OpenCvSharp.Cv2.Resize(src, scaled, new OpenCvSharp.Size(), 2.0, 2.0, OpenCvSharp.InterpolationFlags.Cubic);
```

**Step 5 — Check DPI awareness.** ADB uses PerMonitorV2 DPI awareness. A region configured at 100% DPI on a 150% display will capture the wrong pixels. Coordinate mapping must account for DPI scale — verify via `BotBuilder.Core/Picker/CoordinateMapping.cs`.

---

## Testing OCR Actions

Project convention: hand-rolled fakes, no mock frameworks. See **xunit** skill.

```csharp
// new code to add — fake IOcrEngine for unit tests
internal sealed class FakeOcrEngine : IOcrEngine
{
    public string? NextResult { get; set; }
    public Bitmap? LastInput { get; private set; }

    public string Recognize(Bitmap bitmap, PageSegMode mode = PageSegMode.Auto)
    {
        LastInput = bitmap;
        return NextResult ?? string.Empty;
    }
}

// Usage in test
[Fact]
public async Task ReadText_StoresResultInVariable()
{
    var ocr = new FakeOcrEngine { NextResult = "42" };
    var executor = new ReadTextExecutor(ocr);
    var ctx = FakeBotExecutionContext.Create();

    await executor.ExecuteAsync(action, ctx);

    Assert.Equal("42", ctx.Variables["OutputVar"]);
}
```

**What NOT to test here:** Don't test Tesseract's recognition accuracy — that's the library's concern. Test that your executor correctly wires region capture → OCR → variable storage, using the fake.

---

## Wiring tessdata to Build Output

`eng.traineddata` lives in `assets/tessdata/`. It must be present next to the executable at runtime for both BotRunner (headless) and BotBuilder (editor).

In the consuming project's `.csproj`:

```xml
<!-- new code to add — ensures tessdata copies on build -->
<ItemGroup>
  <Content Include="..\..\assets\tessdata\eng.traineddata">
    <CopyToOutputDirectory>Always</CopyToOutputDirectory>
    <Link>assets\tessdata\eng.traineddata</Link>
  </Content>
</ItemGroup>
```

Validate after adding:

1. Build: `dotnet build ADB.slnx`
2. Check output: `ls BotRunner/bin/Debug/net10.0-windows/assets/tessdata/`
3. Confirm `eng.traineddata` is present
4. Run tests: `dotnet test ADB.slnx`

**If tests fail because tessdata is missing:** The `TesseractOcrEngine` constructor throws `TesseractException` with a path in the message — that path tells you exactly where it looked. Update `AppContext.BaseDirectory`-relative path accordingly.