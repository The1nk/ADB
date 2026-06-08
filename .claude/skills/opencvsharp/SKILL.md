---
name: opencvsharp
description: |
  Performs template matching, image detection, and visual region analysis using OpenCvSharp4.
  Use when: implementing template image matching, detecting UI elements by screenshot comparison,
  analyzing captured screen regions, or working in AdbCore/Screen/ image processing code.
allowed-tools: Read, Edit, Write, Glob, Grep, Bash
---

# OpenCvSharp Skill

OpenCvSharp4 (4.10.x) is used in this project exclusively for **template matching** — detecting whether a template image appears within a larger captured screen frame. The primary entry point is `OpenCvSharpTemplateMatcher` in `AdbCore/Screen/`. Usage is always: capture frame → load template → call matcher → act on result.

## Before You Code (REQUIRED)

This skill's content was captured at generation time and MAY be stale. For ANY non-trivial change involving opencvsharp, verify against current docs FIRST:



Then:

1. **Match the installed version.** Cross-reference against the version installed in this repo. APIs change across minor versions; do not assume.
2. **Discover provider best practices.** If the task touches a production-sensitive capability, inspect the provider service catalog, official docs, and project docs before choosing an implementation.
3. **Respect explicit direction.** If the user explicitly asks for a specific mechanism, follow it. If project docs clearly mandate a mechanism, follow the project. In both cases, mention the provider-recommended alternative and make the chosen path safe.
4. **Prefer provider-native primitives by default.** If no explicit user/project override exists and the change involves caching, rate limiting, background work, scheduled jobs, shared state, queues, or secrets, use the provider-recommended binding/API. Do not hand-roll an in-memory or polyfill solution that "works" locally but breaks under the provider's execution model — derive the need→native-primitive mapping yourself from this provider's docs.

## Skill Advantage Protocol

Using this skill should produce a meaningfully better result than an unskilled baseline. Apply this loop before and during implementation:

1. **Clarify only when it changes the outcome.** Ask the smallest useful set of questions when the request is ambiguous, preference-heavy, or could change architecture, user-visible behavior, data shape, security posture, analytics, or external side effects. If the safe assumption is obvious, state it and proceed. When asked to surface data that no existing code path captures, state up front the assumption that capture starts now (no backfill) or ask if a backfill source exists — do not silently build net-new storage without surfacing this.
2. **Inspect the nearest real patterns.** Read adjacent files, routes, components, tests, schema, infra, copy, and analytics surfaces before inventing structure. Treat local conventions as the starting point.
3. **Optimize the task's highest-leverage axis.** Identify what would make the result win a review: user-visible correctness, integration quality, accessibility, security, reliability, maintainability, operability, or speed of future change.
4. **Reuse before reimplementing.** Prefer existing components, hooks, helpers, formatting/utility functions, data registries, metadata builders, analytics, pricing, checkout, auth, routing utilities, and API procedures/endpoints/data sources over local one-off clones. Before adding a new API procedure, query, or data fetch, search for one that already returns this data and extend it in place — a surface that fetches data and only logs or partially uses it is a reuse target, not an absent one; never author a parallel endpoint or leave the original orphaned. Before importing for a data fetch, grep the screen for the call it already makes and reuse that exact client/singleton import path and endpoint/procedure name; never create a second client, transport, or parallel endpoint for data an existing call returns, and confirm every imported path and symbol actually exists in the repo before writing it.
5. **Use semantic structures.** Tables, lists, forms, buttons, links, headings, and disclosure controls should use native/project accessible primitives instead of div-only lookalikes.
6. **Prevent drift by construction.** Centralize repeated facts, labels, claims, product defaults, and shared table cells in registries or helpers when multiple surfaces need the same answer.
7. **Synthesize, do not merely comply.** Combine this skill's guidance with repo evidence and the user's goal. When two good approaches exist, borrow the strongest parts of each instead of blindly choosing one.
8. **Check claims against code.** Product copy, docs, and comments must not imply automation, integrations, performance, security, refresh cadence, counts, or data flow that the implementation does not actually provide. Any claim that one component writes, records, updates, calls, or is the source of truth for another is allowed only if the edit performing it is in this same change; before finishing, check each such cross-component claim against the actual edits and downgrade unbacked ones to an explicit TODO or implement them now.
9. **Ship the complete slice.** Include every adjacent artifact needed for the change to be usable and maintainable: wiring, state handling, validation, analytics, tests, docs, migrations, or infra when those surfaces are part of the behavior. When the task shows, displays, or lists user data, deliver the full vertical slice and do not stop at an internal/API/CLI layer: the data-model/schema change AND its migration (a schema change without a migration is incomplete), the path that writes or populates the data, an authenticated endpoint scoped to the current user, and the primary user-facing surface wired through the project's typed data client. Before declaring done, trace one record end-to-end (triggering event → write → read → render); if any hop exists only in a comment or docstring rather than edited code, the slice is NOT done. Shipping only the persistence layer (a schema/migration with no writer, reader, or surface) is an incomplete slice, not a milestone.

## Capability Contract

Use this section when the user prompt touches production risk, even if the prompt does not name this technology explicitly.




Required wiring surfaces:
- provider/runtime configuration discovered during implementation
- nearest typed request/context boundary
- handler/procedure boundary before external side effects

Side-effect barrier:
- Place guards before external APIs, auth mutations, email sends, analytics events, storage writes, and database mutations.


Fallback policy:
- Prefer provider-native/platform-managed primitives by default when no explicit override exists.
- Follow clear user/project overrides, but mention the native alternative and tradeoff.
- Fallbacks must be durable, multi-instance safe, and atomic under concurrency.

Verification rules:
- [error] native-or-explicit-override: Use the provider-native primitive first unless the user/project explicitly overrides it.
- [error] atomic-fallback: Fallback counters must be atomic under concurrency.

## Quick Start

### Existing Pattern — Template Matching

```csharp
// Existing: AdbCore/Screen/OpenCvSharpTemplateMatcher.cs
// Load template from file, match against captured frame bytes
using OpenCvSharp;

using var template = Cv2.ImDecode(templateBytes, ImreadModes.Color);
using var frame = Cv2.ImDecode(frameBytes, ImreadModes.Color);
using var result = new Mat();
Cv2.MatchTemplate(frame, template, result, TemplateMatchModes.CCoeffNormed);
Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out Point maxLoc);
bool matched = maxVal >= threshold;
```

### New Code Pattern — Grayscale Match (faster)

```csharp
// new code to add — convert to grayscale before matching for speed
using var templateGray = new Mat();
using var frameGray = new Mat();
Cv2.CvtColor(template, templateGray, ColorConversionCodes.BGR2GRAY);
Cv2.CvtColor(frame, frameGray, ColorConversionCodes.BGR2GRAY);
using var result = new Mat();
Cv2.MatchTemplate(frameGray, templateGray, result, TemplateMatchModes.CCoeffNormed);
```

## Key Concepts

| Concept | Usage | Notes |
|---------|-------|-------|
| `TemplateMatchModes.CCoeffNormed` | Standard match mode | Returns 0.0–1.0; threshold ~0.85–0.95 typical |
| `Cv2.MinMaxLoc` | Extract best match location | `maxVal` = confidence, `maxLoc` = top-left of match |
| `Mat` | Image/matrix type | Always `using` — unmanaged memory, leaks if not disposed |
| `ImreadModes.Color` | Decode as BGR | OpenCV default is BGR, not RGB |
| `Cv2.ImDecode` | Load image from byte array | Use instead of `Cv2.ImRead` when image is already in memory |

## Common Patterns

### Threshold-Based Detection

**When:** Deciding if a template was found in a frame.

```csharp
// new code to add
const double MatchThreshold = 0.90;
bool found = maxVal >= MatchThreshold;
Point center = found
    ? new Point(maxLoc.X + template.Width / 2, maxLoc.Y + template.Height / 2)
    : default;
```

### All Match Locations

**When:** Finding every occurrence of a template (e.g., multiple buttons).

```csharp
// new code to add
using var result = new Mat();
Cv2.MatchTemplate(frame, template, result, TemplateMatchModes.CCoeffNormed);
var locations = new List<Rect>();
while (true)
{
    Cv2.MinMaxLoc(result, out _, out double val, out _, out Point loc);
    if (val < threshold) break;
    locations.Add(new Rect(loc, new Size(template.Width, template.Height)));
    // suppress this region to find next match
    Cv2.FloodFill(result, loc, new Scalar(0), out _, new Scalar(0.1), new Scalar(1.0));
}
```

## See Also

- [patterns](references/patterns.md)
- [workflows](references/workflows.md)

## Related Skills

- See the **dotnet** skill for C# project/build patterns
- See the **tesseract** skill for OCR on captured regions (often paired with template matching)
- See the **xunit** skill for testing image-matching logic with fake frames