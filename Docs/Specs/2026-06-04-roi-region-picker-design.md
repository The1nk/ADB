# ROI Region Picker — Design

**Status:** Approved
**Date:** 2026-06-04
**Type:** Focused QOL slice (the region analog of the M10b coordinate picker)

---

## 1. Problem

The region-of-interest fields (`regionX`, `regionY`, `regionWidth`, `regionHeight`) on the image- and text-matching actions (Find Image / Wait for Image / Assert Image Absent / Screenshot, and the OCR Find/Wait/Assert/Read Text — Screen and Android) must be hand-typed. There's no way to drag a box on the target to set them, even though the in-process capture + display→source mapping already exist (M10b's `FrameCapturer`, `CoordinateMapping`; BotCapture's drag-rectangle `RegionSelectView`).

## 2. Goal

A **"Pick region…"** button in the Properties panel that captures the selected action's bound target, lets the author drag a rectangle on it, and writes `regionX/Y/Width/Height` back into the action's config — the region analog of the coordinate picker.

## 3. Approach

Reuse the M10b machinery wholesale. The button appears via **generic detection** (any action whose config fields include the region keys), not a hardcoded action map — so it auto-covers every current and future region-bearing action, Screen and Android, including Find Text once M11a merges.

### 3.1 Components

**BotBuilder.Core (testable):**
- `PropertiesViewModel.SupportsRegionPicking` — true when the selected action's definition exposes the region fields (checks for `TemplateMatchCore.RegionWidthKey` among its `ConfigFields`). Computed in `Rebuild()`, mirroring `SupportsCoordinatePicking`.
- `RegionSelection.FromCorners(int x1, int y1, int x2, int y2, int imageWidth, int imageHeight) → (int X, int Y, int Width, int Height)` — a pure helper: normalizes the two corners to a top-left origin with positive width/height and clamps the rectangle to `[0, imageWidth] × [0, imageHeight]`.

**BotBuilder (WPF, user-verified):**
- `RegionPickerDialog(System.Drawing.Bitmap frame)` — sibling of `CoordinatePickerDialog`. Shows the frame (`Stretch=Uniform`, frozen `BitmapImage` so the source bitmap can be disposed). The user drags a rubber-band rectangle; on release, both endpoints are mapped to source pixels via the existing `CoordinateMapping.ToSourcePixel`, and `RegionSelection.FromCorners` produces the result. Exposes a `Region` value (X/Y/Width/Height); `DialogResult=true` on a valid (non-degenerate) drag, with a Cancel button.

**MainWindow:**
- A **"Pick region…"** button in the Properties panel (next to "Pick coordinates…"), `Visibility` bound to `SupportsRegionPicking`.
- `PickRegion_Click`: resolve the selected node's bound target (explicit `TargetId`, else first target — identical to the coordinate picker), `FrameCapturer.TryCapture(type, selector)` → friendly message on failure, open `RegionPickerDialog`, and on confirm write the four region `ConfigFieldViewModel`s (`FieldByKey(regionX/Y/Width/Height).Value = (double)…`).

### 3.2 Behaviour decisions

- **Generic detection** (not an action map) — Screenshot gets the button too, which is genuinely useful (screenshot a sub-region). No downside; no per-action wiring.
- **Degenerate drag** (a tiny/zero box, or a drag that starts/ends in the letterbox margin so a corner doesn't map) is **ignored** — the dialog does not confirm and the fields are left untouched. Clearing a region back to "whole frame" stays a manual zero of the fields (YAGNI; `ResolveRegion` already treats zero width/height as "no ROI").
- Coordinate space: `FrameCapturer` yields a client-relative (Window) / device-pixel (Android) frame, which is exactly the space `ResolveRegion` reads the region fields in — so the written source-pixel rectangle is correct at runtime, with the same ratio-based mapping (DPI-robust; BotBuilder is already Per-Monitor-V2 from M10b).

## 4. Testing

- **BotBuilder.Core unit tests:** `RegionSelection.FromCorners` (corner normalization, clamping to image bounds, degenerate sizes); `SupportsRegionPicking` (true for an action with region fields e.g. `screen.findImage`, false for one without e.g. `data.log`).
- **WPF (user visual verify):** the dialog drag-to-box, the four fields populating correctly, and a picked region actually constraining the match at run time (drag a box around a button, confirm Find Image only matches inside it). Target-not-connected → friendly message.

## 5. Out of scope

- A dedicated "clear region" affordance (manual zeroing suffices).
- Resizing/adjusting an existing region by handles (drag a fresh box instead).
- Sharing a common base class between `RegionPickerDialog` and `CoordinatePickerDialog` (they share the mapping helper + image-source conversion; a shared base isn't worth it for two dialogs).

## 6. Merge handling

Has a WPF dialog → **not** self-merged; built to compile-clean + unit-green, opened as a PR, user visually verifies + merges.
