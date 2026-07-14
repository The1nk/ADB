# Design: Fast frame reads (capture-once, Measure Bar, Get Pixel Color), true-parallel Run Parallel, and a Nested Bot Library manager

**Date:** 2026-07-14
**Status:** Approved (brainstorming) — pending implementation plans

## Motivation

Two problems surfaced from a real bot (`PokeGo IV Reader.bot`, a Pokémon GO IV reader that reads three
0–15 stat bars — Attack / Def / HP):

1. **The bot is very slow.** Each stat is read by template-matching up to 16 candidate images per bar
   (≈45 matches worst case), and it manages only 2–3/sec. Root cause: **every image action re-captures the
   entire target** (`ScreenActionBase.CaptureAndMatch` → `IWindowCapture.Capture`; the Android equivalent
   calls `device.Screenshot()`), and the ROI only narrows the *match*, never the *capture*. So the dominant
   cost is N full-frame captures, not the matching.

2. **`Run Parallel` does not run in parallel.** The branch executors are synchronous
   (`FindImageAction.ExecuteAsync` returns `Task.FromResult`), so `ParallelControlFlowExecutor`'s
   `Task.WhenAll` over async methods that never yield runs each branch to completion before the next starts.
   Observable symptom: branch 1, then branch 2, then branch 3.

Separately, the file carried **7 orphaned nested-bot library definitions** with no UI to see or remove them:
deleting a Nested Bot *card* removes only the reference, never the library entry, and the only Remove button
is gated on a card being selected. Once all cards are deleted, the entries are stranded and re-serialized
(with embedded template images) on every save.

## Cross-cutting requirement: Windows **and** Android

Every runtime capability here must work for both **Window** (HWND) and **Android device** targets, mirroring
the existing split: `screen.findImage` / `android.findImage` share a capture-source-independent
`TemplateMatchCore`; `ScreenActionBase` captures via `Win32WindowCapture`, `AndroidImageActionBase` via
`device.Screenshot()`. New capabilities follow the same shape: a **capture-source-independent core** plus a
thin Windows wrapper and a thin Android wrapper that differ *only* in how a fresh frame is obtained. The
authoring-time pickers already capture from both target types via `BotBuilder/FrameCapturer.cs`.

## Architecture backbone: the frame store

A per-run **frame store** on `BotExecutionContext`, kept separate from `Variables` (which remains
scalars-only per the variable model). It maps a **frame name** → an **immutable frame snapshot**.

- **Snapshot form:** width, height, stride, pixel format, and a locked pixel `byte[]` — **not** a live GDI
  `Bitmap`. This is deliberate: `Bitmap`/`GetPixel` is not thread-safe, and true-parallel branches must be
  able to read one shared frame concurrently. A snapshot exposes thread-safe pixel reads and a
  `ToBitmap()` / `ToMat()` conversion for consumers that need one (e.g. the template matcher).
- **Source-agnostic:** a snapshot captured from an Android device is indistinguishable from an HWND capture
  to every downstream reader.
- **Lifetime:** `BotExecutor` disposes the entire store in a `finally` after the walk. Overwriting a slot
  disposes the previous snapshot.

## Slice 1 — Frame store + Capture Frame + Source selector

**Capture Frame** action, in two target variants writing the same snapshot type into the store:
- `screen.captureFrame` — HWND capture (`Win32WindowCapture`).
- `android.captureFrame` — device framebuffer (`device.Screenshot()`).
- Config: **frame name** (default `"frame"`), capture method (Windows only, mirrors existing capture-method
  config). Requires a resolved target of the matching type.

**Source selector** — a shared config field added to every reader:
- Options: **Capture fresh** (default — today's exact behavior, zero migration risk) or **Stored frame**
  (by name, default `"frame"`).
- Applied to the existing image families on **both** platforms: `screen.findImage` / `screen.waitForImage`
  / `screen.assertImageAbsent` and their `android.*` counterparts. When set to Stored frame, the reader
  pulls the named snapshot from the store instead of capturing; a missing/empty slot is a clear action
  failure.

This slice is backend-only (AdbCore) and unit-testable end to end; it does not by itself change any existing
bot's behavior (default remains Capture fresh).

## Slice 2 — Measure Bar

**Purpose:** read a solid-fill bar's value directly (one cheap pixel scan) instead of matching up to 16
templates.

Two variants — `screen.measureBar` and `android.measureBar` — sharing a new **`BarMeasureCore`** (snapshot +
config → value; capture-source-independent).

**Config:** Source (fresh/stored) · ROI (`regionX/Y/W/H`) · **Fill color** (optional, hex) · **Empty color**
(optional, hex) · **tolerance** · **direction** (LeftToRight / RightToLeft / TopToBottom / BottomToTop) ·
**minValue** (default 0) · **maxValue** (default 15) · **result variable**.

**Validation:** at least one of Fill / Empty must be set.

**Algorithm:** scan the ROI's centerline along the chosen direction from the start edge; find the leading
contiguous run of "filled" pixels; `fraction = runLength / axisLength`;
`value = round(minValue + fraction · (maxValue − minValue))`. Write the integer to the result variable and
`<var>Fraction` (0–1). Pixel classification:
- **Both colors set** → nearest-color: a pixel is *filled* when it is closer to Fill than to Empty; the
  tolerance guards ambiguous/anti-aliased boundary pixels. Most robust.
- **Only Fill set** → *filled* = within tolerance of Fill (all else empty).
- **Only Empty set** → *filled* = **NOT** within tolerance of Empty (fill color need not be known; handles
  animated/gradient fills over a constant empty track).

## Slice 3 — Get Pixel Color

Two variants — `screen.getPixelColor` and `android.getPixelColor` — sharing a **`PixelReadCore`**.

- **Config:** Source (fresh/stored) · point (x, y) · result-var **prefix** (default `pixel`).
- **Behavior:** read one pixel from the source frame; write `pixelHex` (`#RRGGBB`), `pixelR`, `pixelG`,
  `pixelB`. Single `out` port — the author branches on the variables. (Pure read; no built-in compare.)

## Slice 4 — Run Parallel: true concurrency

- **Fix:** offload each branch walk with `Task.Run` in `ParallelControlFlowExecutor` so the synchronous
  executors actually run on separate thread-pool threads and branches overlap.
- **Thread-safety (already largely satisfied):** `Variables` is a `ConcurrentDictionary`; capture and
  matcher types are stateless; `ActionsExecuted` uses `Interlocked`. Frame snapshots are immutable and safe
  for concurrent reads.
- **To harden in this slice:** the **log** and **progress** sinks must be safe under concurrent calls (lock
  around file writes / marshal correctly). 
- **Documented constraints:** capturing *into the same frame slot* concurrently, and *fresh-capture inside
  parallel branches* (esp. concurrent `device.Screenshot()` on one Android connection), are unsupported —
  the intended pattern is Capture Frame **before** the split, branches read the shared snapshot.
- **Test:** a barrier-based test that passes only if two branches genuinely run concurrently (each branch
  must reach the barrier before either is released).

## Slice 5 — Nested Bot Library manager

A dedicated **"Manage Nested Bot Library…"** dialog (the properties panel is fixed-width and cannot host a
table).

- **Lists** every library entry: **name · #actions · usage count** (count of `control.nestedBot` cards whose
  `nestedBotId` == entry.Id across the top-level bot and all nested bots).
- **Per-row actions:** Open (edit in child editor), Rename, Export .bot…, Remove.
- **Remove unused:** purge every entry **not transitively reachable** from the top-level graph's Nested Bot
  cards. (For the motivating file, the top level references none → all 7 removed.) Note this differs from the
  per-row usage count: an entry can have usage count > 0 (referenced by *another* nested bot) yet still be
  removed, if that whole referrer chain is itself unreachable from the top level — e.g. *Rename Mons*
  references *Dismiss Popups*, but neither is reachable from the top-level graph, so both are unused.
- **Guard:** removing a *referenced* entry warns first (it will dangle those cards' `nestedBotId`).
- **Undo/dirty:** removals mark the document dirty (consistent with today's card-level Remove); they are not
  placed on the undo stack. The save-gate / close-without-saving is the safety net.
- **Access point:** a menu item (exact menu confirmed during planning).
- **File-safety constraint:** validation runs against a **copy** of `PokeGo IV Reader.bot`. The original is
  never written by any dev/test step.

## Reusable primitive: color dropper picker

Both Measure Bar color fields (Fill, Empty) get a **Pick…** dropper: capture a frame off the bound target and
click a pixel to sample its hex. Built once as a reusable color-sampling picker, extending the existing
capture/region picker plumbing and reusing `FrameCapturer` (already Window **and** Android capable). Get
Pixel Color's point-pick reuses the existing coordinate picker.

## Serialization / .bot format

- New action `typeKey`s: `screen.captureFrame`, `android.captureFrame`, `screen.measureBar`,
  `android.measureBar`, `screen.getPixelColor`, `android.getPixelColor`. New config keys per the slices
  above (`frameName`, `source` = `fresh` | `stored`, `fillColor`, `emptyColor`, `tolerance`, `direction`,
  `minValue`, `maxValue`, plus reuse of the existing `region*`/`resultVar` keys). The Source selector uses a
  single `source` key on every reader, paired with `frameName` when `source` = `stored`.
- No schema-version bump: these are additive config bags on new/existing actions; old bots deserialize
  unchanged (Source defaults to Capture fresh).
- The frame store is **runtime-only** — never serialized.

## Documentation (per the Docs Sync Contract)

Every slice updates all three surfaces in the same unit of work: **CLAUDE.md** (action set, frame-store
model, Run Parallel semantics), **README.md** (goblin voice preserved), and the **`../ADB.wiki`** sibling
repo (detailed reference for the new actions, Source selector, capture-once pattern, and library manager).

## Sequencing

Five independent slices, each its own plan → subagent-driven implementation → PR:

1. Frame store + Capture Frame + Source selector on image families (backend-only).
2. Measure Bar (+ color dropper picker).
3. Get Pixel Color.
4. Run Parallel true concurrency + thread-safe sinks (backend-only).
5. Nested Bot Library manager (WPF).

Slices 2–3 build on slice 1's frame store but also work standalone via Capture fresh. Slices 4 and 5 are
independent of 1–3.

## Out of scope (YAGNI)

- Implicit/auto frame caching (explicit Capture Frame node chosen instead).
- A compare/conditional variant of Get Pixel Color (pure read chosen).
- Undo for library-entry removal.
- Non-horizontal-only bars beyond the four cardinal scan directions already included.
