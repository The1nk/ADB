# Embed image templates inside the `.bot` file

**Date:** 2026-06-25
**Status:** Approved (design)
**Branch:** `feat/embed-templates`

## Problem

Image-matching actions (Find Image / Wait for Image / Assert Image Absent, for both Screen and Android) reference their template by a **file path** stored in the action config (`templatePath`). The match chain ends at `ITemplateMatcher.Match(haystack, templatePath, …)` doing `Cv2.ImRead(path)` from disk, and the editor previews the template via `PathToImageConverter` (also disk). The Capture button writes the SaveFileDialog's **absolute** path into config.

Consequence: a `.bot` is **not portable**. Move it to another machine or folder and every Find Image breaks, because it points at `C:\Users\…\btn.png`. Templates must travel with the bot.

## Goal

Embed each template's image bytes **inside** the `.bot` JSON (base64), so a `.bot` is self-contained and runs anywhere with no sidecar PNGs — while keeping existing path-based bots working.

## Decisions (locked)

- **Format:** base64-encoded image bytes in the action `config` (Approach #1 — single plain-text JSON file).
- **Back-compat:** opening a path-based `.bot` **upgrades it in memory only** (reads the source files into embedded bytes at load); the original file on disk is never rewritten until the user saves, at which point the saved copy is embedded.
- **Embed timing:** automatically on every save (no explicit "embed" action).
- **Source path on embed:** rewrite `templatePath` to **just the basename** (e.g. `btn.png`) — a human-readable label, not a machine-specific absolute path. The embedded bytes are authoritative; the basename is never used as a real path.
- **Out of scope (noted for later):** a dedicated `templateName` label field. The basename-in-`templatePath` seeds it; do not build it now.

## Data model (additive — schema stays `1.0`)

Per image action, `config` gains a companion key alongside `templatePath`:

| Key | Meaning |
|-----|---------|
| `templatePath` | Source label. Full path while authoring; rewritten to basename on save. |
| `templateImage` | Base64 of the original image file bytes (any format `Cv2.ImDecode` reads). **Authoritative** for matching + preview when present. |

`config` is already `Dictionary<string,object>` round-tripped by System.Text.Json, so a base64 string needs **no serializer change**. `templatePath` is retained (as a basename), so even an older build can still open the file; hence no schema-version bump. Constants live on `TemplateMatchCore` (`TemplatePathKey` exists; add `TemplateImageKey = "templateImage"`) so Screen and Android share one definition.

## Runtime (AdbCore)

1. **Matcher takes bytes, not a path.** `ITemplateMatcher.Match(Bitmap haystack, byte[] templatePng, double minConfidence)`; `OpenCvSharpTemplateMatcher` uses `Cv2.ImDecode(templatePng, ImreadModes.Color)` (replacing `Cv2.ImRead`). The empty / larger-than-haystack guards are unchanged.
2. **Resolver — the one place that knows embedded-vs-path.** `TemplateMatchCore.ResolveTemplateBytes(config)`:
   - `templateImage` present → `Convert.FromBase64String(...)`.
   - else `templatePath` present and the file exists → `File.ReadAllBytes(...)`.
   - else `templatePath` present but missing → throw `FileNotFoundException` (preserves today's behavior).
   - else (nothing configured) → return null, so the action emits its existing "a template image is required" failure.
3. **Actions** (`FindImageAction`, `WaitForImageAction`, `AssertImageAbsentAction` + the three Android equivalents) resolve bytes via the resolver and pass them through `CaptureAndMatch` → `TemplateMatchCore.MatchInRegion` (both signatures change `string templatePath` → `byte[] templatePng`).
4. **BotRunner needs no migration:** an old path-based bot resolves via the path exactly as today; an embedded bot resolves from bytes. Self-contained bots run with no external files.

## BotCapture call sites

`PreviewConfirmViewModel.TestMatch` and `SessionViewModel.Retest` currently call `matcher.Match(fresh, path, conf)`. They switch to bytes: `Retest` reads `File.ReadAllBytes(row.FilePath)`; `TestMatch` encodes its in-memory `Crop` to PNG bytes directly (and can drop its temp-PNG round-trip). The `FakeTemplateMatcher` records `LastTemplate` bytes instead of `LastTemplatePath`; affected AdbCore/BotCapture tests update to the bytes contract.

## Editor (BotBuilder)

A small `TemplateEmbedder` helper (`BotBuilder.Core`) with an injected file reader (`Func<string, byte[]?>` returning null when missing — keeps it unit-testable):

- **`Embed(Bot, read)`** — for each action with a `templatePath` whose file is readable and no `templateImage` yet, set `templateImage` from the bytes. Idempotent; leaves `templatePath` untouched (full path).
- **`PrepareForSave(Bot, read)`** — run `Embed`, then for any action that now has `templateImage`, rewrite `templatePath` to `Path.GetFileName(templatePath)`.

Wiring:

- **Load** (`BotEditorViewModel.Load`): run `Embed` on the deserialized `Bot` **before** `DocumentMapper.Populate`, so the in-memory model carries bytes (this is the "in-memory upgrade") — robust even if the source file is deleted before the user saves, and lets preview work when the source is already gone.
- **Save / Save As / Export** (the `DocumentMapper.ToBot` copy used for every write, incl. the Test Run temp export): run `PrepareForSave` on that copy before serialization, so every written `.bot` is embedded + basename-stripped without mutating live editor state mid-session.
- **Preview**: the `ImagePath` field's preview prefers `templateImage` bytes (decode → frozen `BitmapImage`) and falls back to the `templatePath` file. So an embedded bot previews correctly even when the original PNG is gone — the core portability win, visible in the editor. The `ConfigFieldViewModel` for the `ImagePath` field exposes the companion `templateImage` value (read from the node's config) so the preview can bind to bytes-or-path.
- **Capture / Browse**: unchanged — they still set `templatePath`. Preview falls back to the (present) file until the next save embeds it.

## Testing

- **AdbCore.Tests:** `ResolveTemplateBytes` (embedded / path-fallback / missing-file-throws / nothing-configured-null); `OpenCvSharpTemplateMatcher` decoding from bytes; the image-action tests updated to assert on bytes via the fake.
- **BotCapture.Core.Tests:** `TestMatch`/`Retest` matcher tests updated to the bytes contract.
- **BotBuilder.Core.Tests:** `TemplateEmbedder.Embed` (fills bytes, idempotent, skips missing files) and `PrepareForSave` (embeds + strips path to basename, leaves already-embedded bytes intact); a round-trip test that an embedded bot resolves at runtime with the source file absent.

## Delivery

Built via subagent-driven-dev as one branch/PR. **Parked for user visual verification + merge** (preview behavior is visual): capture a template → Save → move/delete the source PNG → reopen the `.bot` → the preview still shows and a Test Run still matches.

## Slices (for planning)

1. **Runtime core (AdbCore + BotCapture.Core):** matcher → bytes, `ResolveTemplateBytes`, actions, BotCapture call sites, fakes/tests.
2. **Editor logic (BotBuilder.Core):** `TemplateEmbedder` + Load/Save/Export wiring + field-VM preview bytes + tests.
3. **Editor visual (BotBuilder WPF):** preview converter prefers bytes; `ImagePath` template binding.
