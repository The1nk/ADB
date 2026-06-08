# OpenCvSharp Workflows Reference

## Contents
- Adding a New Image-Matching Action
- Debugging a Failed Match
- Testing Template Matching Logic
- Capture → Match → Click End-to-End

---

## Adding a New Image-Matching Action

Copy this checklist:
- [ ] 1. Define action in `AdbCore/Actions/BuiltIn/` implementing `IActionDefinition` + `IActionExecutor`
- [ ] 2. Add a `TemplatePathField` config field pointing to the `.png` asset
- [ ] 3. Inject or resolve `OpenCvSharpTemplateMatcher` from the execution context
- [ ] 4. In `ExecuteAsync`, capture the frame, call `matcher.Match(...)`, return success/failure
- [ ] 5. Register the action in the action registry
- [ ] 6. Write an xUnit test with a known frame + template pair (see **xunit** skill)

```csharp
// new code to add — skeleton executor
public async Task<ActionResult> ExecuteAsync(BotExecutionContext ctx, BotAction action)
{
    var templatePath = action.Config["TemplatePath"];
    var templateBytes = await File.ReadAllBytesAsync(templatePath);
    var frameBytes = await ctx.Target.CaptureScreenAsync();   // existing abstraction

    var result = _matcher.Match(frameBytes, templateBytes, threshold: 0.90);
    if (!result.Found)
        return ActionResult.Failure($"Template not found (best: {result.Confidence:F2})");

    await ctx.Input.ClickAsync(result.Center);
    return ActionResult.Success();
}
```

---

## Debugging a Failed Match

When `maxVal` is below threshold, work through this checklist:

- [ ] **Log confidence.** Always emit `result.Confidence` (the `maxVal`). Silent failure hides whether you're at 0.60 or 0.89.
- [ ] **Save debug frames.** Temporarily write `frame` and `result` mat to disk with `Cv2.ImWrite` to see what the matcher saw.
- [ ] **Check template size.** Template larger than frame → `MatchTemplate` throws. Assert `template.Width <= frame.Width && template.Height <= frame.Height`.
- [ ] **Check DPI.** Win32 capture must match DPI at template-capture time. A 150% DPI frame matched against a 100% template → wrong scale, low confidence.
- [ ] **Check color channels.** Both must be same mode (both color or both grayscale). Mixing causes silent mismatch.

```csharp
// new code to add — debug dump helper
#if DEBUG
Cv2.ImWrite("debug_frame.png", frame);
Cv2.ImWrite("debug_template.png", template);
Cv2.ImWrite("debug_result.png", result * 255); // scale 0-1 to 0-255 for visibility
#endif
```

Validate: `dotnet test ADB.slnx` after any matcher changes.

---

## Testing Template Matching Logic

See the **xunit** skill for test patterns. For image matching, use real small PNG fixtures — do NOT mock `Mat` or `Cv2`.

```csharp
// new code to add — xUnit test with embedded fixture
[Fact]
public void Match_ReturnsFound_WhenTemplateExistsInFrame()
{
    var frameBytes = File.ReadAllBytes("fixtures/frame.png");
    var templateBytes = File.ReadAllBytes("fixtures/template.png");
    var matcher = new OpenCvSharpTemplateMatcher();

    var result = matcher.Match(frameBytes, templateBytes, threshold: 0.90);

    Assert.True(result.Found);
    Assert.InRange(result.Confidence, 0.90, 1.0);
}

[Fact]
public void Match_ReturnsNotFound_WhenTemplateAbsent()
{
    var frameBytes = File.ReadAllBytes("fixtures/frame.png");
    var templateBytes = File.ReadAllBytes("fixtures/different_template.png");
    var matcher = new OpenCvSharpTemplateMatcher();

    var result = matcher.Match(frameBytes, templateBytes, threshold: 0.90);

    Assert.False(result.Found);
}
```

Keep fixture PNGs small (< 200×200 px) — they're checked into the repo alongside the test class.

---

## Capture → Match → Click End-to-End

```
1. Resolve target (window handle / ADB serial / browser page)
2. Capture frame bytes  →  AdbCore/Screen/ (Win32WindowCapture or ADB screenshot)
3. Load template bytes  →  from .bot file's asset directory
4. Match               →  OpenCvSharpTemplateMatcher.Match(frame, template, threshold)
5. Map coordinates     →  frame-space Point → target input space (see CoordinateMapping)
6. Send input          →  IInputSender.Click(point) or IAdbDevice.Tap(point)
```

Iterate-until-pass for reliability:
1. Run the action in a test bot
2. Check logs for `Confidence` value
3. If < 0.85 and visually correct: lower threshold to 0.80 or recapture template at correct DPI
4. If < 0.70: wrong template, wrong capture mode, or DPI mismatch — fix root cause
5. Only ship threshold < 0.90 when you have evidence the lower value doesn't produce false positives

For OCR on the matched region rather than click, crop the frame mat to `matchRect` and pass to Tesseract. See the **tesseract** skill.