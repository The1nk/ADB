# Zoom & Pan for the Frame-Picker Tools

**Date:** 2026-07-15
**Status:** Approved — ready for implementation plan

## Problem

The four tools that let a user click / drag / sample on a captured window-or-device frame all
render that frame at a single fixed size and offer no way to magnify it:

- **CoordinatePickerDialog** (BotBuilder) — click to drop coordinate markers.
- **RegionPickerDialog** (BotBuilder) — drag a box to define an image-match ROI.
- **ColorDropperDialog** (BotBuilder) — click a pixel to sample its `#RRGGBB`.
- **RegionSelectView** (BotCapture) — drag a box to crop a template image.

On a high-resolution capture, small UI elements are only a handful of display pixels, so
precise clicking (a single pixel for the eye-dropper, a tight ROI for a template) is guesswork.
Users need to zoom in.

All four share near-identical structure: a `Grid` hosting a `Stretch="Uniform"` `Image` plus an
overlay `Canvas`, with clicks mapped to source pixels. The three BotBuilder dialogs route through
the pure `CoordinateMapping` geometry (`BotBuilder.Core`) and each apply a
`FrameImage.TranslatePoint(origin)` correction for the letterbox margins that `Stretch="Uniform"`
introduces. BotCapture's view uses a simpler centered variant with its own inline ratio math.

## Goal

Add zoom + pan to **all four** surfaces through **one reusable control**, so every tool gains
identical, consistent behavior and the tricky geometry is written and tested once.

Interaction model (chosen):

- **Mouse wheel = zoom toward the cursor** (the wheel is otherwise unused in these tools).
- **Middle-mouse drag = pan**; scrollbars also pan.
- A compact toolbar: **`[−] [zoom %] [+]  [Fit] [100%]`**.
- **Left-drag stays reserved** for the tool's own select/click behavior.
- Keyboard **`+` / `−` / `0`** (0 = Fit) when the control is focused.

## Non-Goals

- No change to what any tool ultimately produces (coordinate tuples, ROI rectangle, sampled hex,
  cropped template) or to the `.bot` schema.
- No change to capture or DPI behavior — source coordinates remain true device pixels.
- The node-graph canvas (`MainWindow`) already has its own zoom and is out of scope.
- `SourcePickerView` (a list, not a draggable frame) is out of scope.

## Architecture

One shared, `UseWPF` home already exists: **`AdbUi.Theme`**, referenced by both BotBuilder and
BotCapture, with its own test project `AdbUi.Theme.Tests`. Both the pure geometry and the WPF
control live there. Consumers speak **only in source-pixel space**; the control owns every
display/zoom/pan concern.

### The model shift (letterbox → exact-fit)

Today `Stretch="Uniform"` letterboxes the image, forcing the `CoordinateMapping` +
`TranslatePoint` margin correction. The zoomable control instead sizes the image **exactly** to
`source × scale` inside a `ScrollViewer`:

- Content smaller than the viewport → centered, nothing to scroll.
- Content larger than the viewport → scrollbars / pan.

Because content size **equals** image size, mapping collapses to `sourceX = pointOnImage.X / scale`
with **no margin term** — removing the whole letterbox class of bug rather than compounding it.

Pixel crispness matters for an eye-dropper, so the image uses **`NearestNeighbor`** scaling when
zoomed past 100 % (sharp pixel squares) and linear when shrunk below 100 %.

## Component 1 — `ViewportTransform` (pure geometry, `AdbUi.Theme`)

A pure class over plain `double`/`int` (no `System.Windows` types) so `AdbUi.Theme.Tests` can
cover it fully. It generalizes the old `CoordinateMapping` (which was only the zoom = Fit case):

- `FitScale(viewportW, viewportH, srcW, srcH)` → the Fit zoom (min of the width/height ratios).
- `ClampScale(scale)` → clamp to `[0.05, 32]`.
- `StepScale(scale, wheelTicks)` → geometric step (~1.2× per notch), then clamp.
- `ZoomToCursorOffset(oldOffset, viewportCursor, oldScale, newScale)` per axis → the new scroll
  offset that keeps the source pixel under the cursor fixed (the `ScrollViewer` clamps the result
  into `[0, scrollable]`).
- `PointToSource(pointOnImage, scale, srcW, srcH)` → `(int X, int Y)?`; null when outside
  `[0,srcW)×[0,srcH)`, otherwise clamped to `[0, src-1]`.
- `SourceToDisplay(sx, sy, scale)` → display coordinates for overlay placement.

`RegionSelection.FromCorners` (BotBuilder.Core) stays — still used to clamp drag corners into a
valid rectangle. `CoordinateMapping` and its tests are **removed once no caller remains**.

## Component 2 — `ZoomPanImageHost` (UserControl, `AdbUi.Theme`)

Structure (themed via `DynamicResource` brushes to match the rest of `AdbUi.Theme`):

```
DockPanel
├─ Border (Top): [−] [zoom %] [+]   [Fit] [100%]       ← toolbar, built in
└─ ScrollViewer (Horizontal/VerticalScrollBarVisibility=Auto)
   └─ Grid (Horizontal/VerticalAlignment=Center)        ← sized to src × scale
      ├─ Image  (explicit Width/Height; NearestNeighbor when scale ≥ 1)
      └─ Canvas (overlay; same size; IsHitTestVisible=False)
```

### Public API (source-pixel space only)

- `void SetImage(BitmapSource img)` — reads `PixelWidth`/`PixelHeight`; calls `Fit()` on load.
- Interaction events `ImagePointerDown` / `ImagePointerMove` / `ImagePointerUp` — args carry
  `int? SourceX, SourceY` and `bool InsideImage` (already mapped). **Left-button only**; the
  middle button is consumed by pan and never surfaced. Mouse is captured on left-down so a region
  drag keeps tracking even when the cursor leaves the image.
- Overlay API (the control reprojects overlay items on **zoom**; **pan needs no reprojection**
  because image + canvas scroll together inside the `ScrollViewer`):
  - `void ClearOverlay()`
  - `void AddDot(double sx, double sy, Color stroke, Color fill)` — a **constant 14 px** ring at a
    source pixel (stays the same screen size at any zoom).
  - `void SetPreviewRect(int x, int y, int w, int h)` / `void ClearPreviewRect()` — a single
    transient rectangle in source coordinates that **scales with zoom** (the live rubber-band).
- `void Fit()`, `void SetScale(double scale)` (100 % = `SetScale(1.0)`).

### Baked-in interaction

- **Wheel = zoom-to-cursor:** `PreviewMouseWheel` → `StepScale` → apply, then set offsets via
  `ZoomToCursorOffset` using the cursor position within the `ScrollViewer` viewport; `e.Handled`
  so the `ScrollViewer` does not also scroll.
- **Middle-drag = pan:** capture on middle-down, adjust horizontal/vertical offsets by the drag
  delta, show a grab cursor, release on middle-up.
- **Scrollbars** pan directly.
- **Keyboard** `+` / `−` / `0` (Fit) when focused.
- The single `ToImageSource(Bitmap)` helper duplicated across the three dialogs is consolidated
  (co-located with the control / `BitmapInterop`).

## Component 3 — Wiring the four surfaces

Each surface loses its `Grid > Image + Canvas` block and its mapping code, keeping **only** its
own behavior:

- **CoordinatePickerDialog** — `ImagePointerDown` (InsideImage) → `_vm.RecordClick(sx, sy)`,
  `AddDot(..., Lime, semi-green)`, refresh prompt, close when `_vm.IsComplete`.
- **ColorDropperDialog** — keeps the `Bitmap` for `GetPixel`; `ImagePointerDown` → sample,
  `AddDot(..., White, sampledColor)`, set `PickedHex`, close.
- **RegionPickerDialog** — down → record start (source); move (pressed) →
  `SetPreviewRect(RegionSelection.FromCorners(start, current, …))`; up → finalize `Region`, close.
  The rubber-band is the control's preview rect, so it stays glued through zoom.
- **BotCapture `RegionSelectView`** — same region-drag wiring; sets `Vm.Selection` (source rect);
  `Vm.Crop()` unchanged. Its inline `scaleX`/`scaleY` mapping is deleted. Confirm / Back / prompt
  stay in the view's own top bar; the zoom toolbar comes from the control.

## Settled Behaviors & States

- **DPI unchanged.** Zoom lives purely in image-pixel space; captured frames stay true device
  pixels (PerMonitorV2), so the coordinate contract is untouched.
- Image larger than the viewport → Fit, then zoom in for precision.
- Tiny image → Fit upscales (crisp via NearestNeighbor).
- Click in the centered margin outside the image → `InsideImage == false`, ignored.
- Degenerate / zero-size region → ignored (existing behavior preserved).
- Pan with nothing to scroll → no-op.

## Testing

`ViewportTransformTests` (xUnit, `AdbUi.Theme.Tests`):

- `FitScale` for a wide source and a tall source (picks the correct limiting ratio).
- `ClampScale` honors the `[0.05, 32]` bounds.
- `StepScale` applies the geometric factor and clamps at the ends.
- `ZoomToCursorOffset` keeps the source pixel under the cursor fixed across a zoom change
  (the anchor invariant).
- `PointToSource` inside → mapped, outside → null, edge → clamped to `[0, src-1]`.
- `SourceToDisplay` round-trips with `PointToSource` at representative scales.

The WPF control's interaction (wheel/middle-drag/scroll/toolbar) is verified visually, per the
project's usual visual-slice workflow.

## Documentation (Sync Contract)

This is not in the hard-trigger list (no action / selector / `.bot` schema / CLI / Lua / target /
DPI-capture change), but the pickers are user-facing, so add a short "zoom & pan (wheel to zoom,
middle-drag to pan)" note wherever the three surfaces describe these picker/capture tools:

- `CLAUDE.md` — where the coordinate picker / ColorDropper / capture tools are described.
- `README.md` — keep the goblin voice; describe the interaction accurately.
- `../ADB.wiki` — the picker / BotCapture reference page(s).

## Suggested Slicing (for the implementation plan)

1. `ViewportTransform` + tests, and the `ZoomPanImageHost` control (with the built-in toolbar and
   interaction), in `AdbUi.Theme` — backend/library slice.
2. Adopt in the three BotBuilder dialogs (CoordinatePicker, RegionPicker, ColorDropper); remove
   dead `CoordinateMapping` once no caller remains — visual slice.
3. Adopt in BotCapture `RegionSelectView` — visual slice.
4. Docs sync across the three surfaces.
