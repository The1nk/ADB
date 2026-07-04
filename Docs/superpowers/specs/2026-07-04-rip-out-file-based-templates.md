# Rip out file-based image templates

**Date:** 2026-07-04
**Status:** Approved — ready for implementation plan

## Problem

Image-match templates are already embedded in the `.bot` (base64 `templateImage`, from the embed-templates
work), but the authoring flow still treats a template as a **file on disk**:

- The template field is `ConfigFieldType.ImagePath` — a **file path** with a **Browse…** picker.
- The **Capture** button uses the field's current value **as the output path**. Typing a bare name like
  `File` makes BotCapture write `File` (the PNG) **and** `File.meta.json` (the confidence sidecar, via
  `ConfidenceSidecar`) into the app's **working directory** — then the image is embedded into the `.bot`
  on save anyway, leaving those two files orphaned and useless.
- `TemplateMatchCore.MatchConfigured` still falls back to reading the path from disk, and
  `ITemplateMatcher` still exposes a `Match(Bitmap, string path, …)` overload.

## Goal (user decision: **full rip-out**)

- Templates come **only** from Capture, embedded directly — **no file ever lands in the working directory**.
- The template field becomes a pure **name/label** (`templateName`), not a path. **Browse… is removed.**
- The runtime **disk-read fallback is deleted** (`ITemplateMatcher.Match(string)` and the path branch in
  `MatchConfigured`); matching is embedded-bytes only.
- Old `.bot` files **migrate on load**: embed from a still-present path one last time, derive `templateName`
  from the old basename, drop `templatePath`. A bot referencing a *missing* external PNG loses that image
  (accepted).

## Non-goals

- No change to BotCapture itself — it still writes a PNG (+ sidecar) to the `--output` path it's given; we
  just give it a **temp** path and delete both files after reading them.
- No `.bot` schema version bump — the config bag is open; `templateName`/`templateImage` are the live keys,
  a stray `templatePath` in an old file is migrated away on load and simply ignored if it lingers.
- The confidence **sidecar** mechanism stays (BotCapture writes it to temp; the Builder reads then deletes
  it). We are not moving confidence into a new store.

## Design

### 1. Config model — `AdbCore/Actions/BuiltIn/TemplateMatchCore.cs`

- Add `public const string TemplateNameKey = "templateName";`. Keep `TemplatePathKey = "templatePath"` **for
  migration reads only** (no longer a field).
- Replace `TemplatePathField()` with:
  `TemplateNameField() => new() { Key = TemplateNameKey, Label = "Template Name", Type = ConfigFieldType.ImageTemplate };`
- `HasTemplate(config)` → embedded only:
  `!string.IsNullOrWhiteSpace(ConfigValues.GetString(config, TemplateImageKey))`.
- `MatchConfigured` → embedded only. If `templateImage` present, `matcher.Match(haystack, Convert.FromBase64String(embedded), confidence)`; else **return null** (no template — the action's `HasTemplate` gate already fails the run early with a clear message, so this is just defensive).
- The six image actions reference `TemplateMatchCore.TemplateNameField()` where they used `TemplatePathField()`; no other action change (they already read `templateImage` via the core).

### 2. Field type — `AdbCore/Actions/ConfigFieldType.cs`

Rename the enum member `ImagePath` → `ImageTemplate` (compile-checked; ~4 references: the field factory,
`ConfigFieldViewModel`, the template selector, and the XAML resource wiring). It no longer means "a path."

### 3. Matcher — embedded-only

- `AdbCore/Screen/ITemplateMatcher.cs` — **remove** `MatchResult? Match(Bitmap, string templatePath, double)`.
  Keep `Match(Bitmap, byte[] templatePng, double)`.
- `AdbCore/Screen/OpenCvSharpTemplateMatcher.cs` — remove the path-based implementation (the one that reads
  the file); keep the bytes-based one (`Cv2.ImDecode`).
- Any test doubles implementing `ITemplateMatcher` (e.g. in `AdbCore.Tests`) drop the path method.

### 4. Migration + save — `BotBuilder.Core/TemplateEmbedder.cs`

Rework so load **migrates** and save no longer touches paths:

- `Migrate(Bot bot, Func<string?, byte[]?> read)` (rename/replace `Embed`): for each action **and** nested
  bot, if it carries `templatePath` or `templateImage` (an image action):
  1. If no `templateImage` and `read(templatePath)` returns bytes → set `templateImage` = base64 (last-time
     embed of a still-present source).
  2. If `templateName` is empty and `templatePath` is non-empty → set `templateName = Path.GetFileName(templatePath)`.
  3. Remove `templatePath`.
  Recurse into `bot.NestedBots`.
- `PrepareForSave(Bot bot, Func<string?, byte[]?> read)` → just `Migrate(bot, read)` (idempotent). No path
  rewrite; capture embeds directly, so save has nothing extra to do. Keep `ReadFileIfExists` as the injected
  reader.
- `BotEditorViewModel.Open` already calls the embed step before mapping — it now calls `Migrate`; `Save`/
  `ExportTo` route through `PrepareForSave` on the `ToBot` copy (unchanged wiring, method body simplified).

### 5. Properties VM — `BotBuilder.Core/Properties/ConfigFieldViewModel.cs`

- The `Value` setter's `ImagePath` branch (which cleared `templateImage` when a new **path** existed) is
  removed — an `ImageTemplate` field's `Value` is just the `templateName` label; setting it stores the name
  and raises change, nothing else. Keep `EmbeddedImageBase64` (drives the preview).
- Add `public void SetCapturedTemplate(byte[] png)`: stores `_node.Config[TemplateImageKey] =
  Convert.ToBase64String(png)`, raises `OnPropertyChanged(nameof(EmbeddedImageBase64))` and `_onChanged()`.
  This is how Capture injects the embedded image (no path round-trip).

### 6. WPF — capture-to-temp + name-only field

- `BotBuilder/MainWindow.xaml`: rename the `FieldImagePath` `DataTemplate` to `FieldImageTemplate`; **remove
  the Browse… button**; keep the **Template Name** TextBox (`Value`), the **Capture** button, and the
  embedded preview (`EmbeddedImageBase64`). Update `ConfigFieldTemplateSelector`'s `ImagePathTemplate`
  property → `ImageTemplateTemplate` and its `ImagePath` case → `ImageTemplate`.
- `BotBuilder/MainWindow.xaml.cs` `CaptureField_Click`:
  - Build a **temp** path: `Path.Combine(Path.GetTempPath(), "ADB", "Captures", Guid.NewGuid().ToString("N") + ".png")`;
    ensure the directory exists.
  - `CaptureLauncher.Launch(exe, tempPath, saved => …)`.
  - On `saved`: read `File.ReadAllBytes(tempPath)` → `field.SetCapturedTemplate(bytes)`; read
    `ConfidenceSidecarReader.Read(tempPath)` → set the sibling confidence field; then **delete** `tempPath`
    and `tempPath + ".meta.json"` (best-effort try/catch). On cancel / not-saved: best-effort delete any temp
    and leave the field unchanged.
  - Remove the old "output path = field.Value / SaveFileDialog" logic entirely.
- `BrowseField_Click` stays (still used by `FieldFilePath` for APK/output paths) — just no longer wired to
  templates.

### 7. BotRunner

Runs the actions through `TemplateMatchCore` (now embedded-only) — **no code change**. Confirm no BotRunner
code reads `templatePath` from disk (grep); if any does, delete it.

## Tests

**AdbCore.Tests**
- `TemplateMatchCore`: `HasTemplate` is true only with `templateImage` (a bare `templatePath` → **false**);
  `MatchConfigured`/`MatchInRegion` matches embedded bytes and returns null with no embedded image.
- Matcher: keep the bytes-based tests; delete any path-based `ITemplateMatcher` test; update the fake matcher
  to the single byte overload.
- The six image-action tests already drive a fake matcher with embedded/ROI config — update any that set
  only `templatePath` to set `templateImage` instead.

**BotBuilder.Core.Tests**
- `TemplateEmbedder.Migrate`: (a) path + readable file, no embed → embeds, sets `templateName` = basename,
  drops `templatePath`; (b) already-embedded + path → sets `templateName` from basename, drops path, keeps
  bytes; (c) path + **missing** file → no embed, `templateName` = basename, path dropped; (d) recurses into
  nested bots. Update the existing embed/save round-trip tests to the new key (`templateName`, no path).
- `ConfigFieldViewModel.SetCapturedTemplate` stores base64 under `templateImage` and surfaces it via
  `EmbeddedImageBase64`.

## Documentation

- **Wiki `Actions-Reference.md`** — the Screen/Android image-action rows: `templatePath` → `templateName`
  (label), note the image is captured+embedded (no external file, no Browse). Any "Image Matching" page
  mention of file paths / `.meta.json` sidecars in the working dir is removed.
- **README** — wherever capture/templates are described, note templates are **captured straight into the
  bot** (no loose PNGs, no working-dir clutter), goblin voice.
- **CLAUDE.md** — the `.bot` format / image-actions notes: templates are embedded-only (`templateImage` +
  `templateName` label); `templatePath` is a **legacy key migrated away on load**; no disk-read fallback.

## Execution

Subagent-driven development after the plan. This spans AdbCore + BotBuilder.Core + BotBuilder WPF + tests,
and changes the **Capture UI + template field rendering**, so it is a **parked** slice: build it, unit-test
the AdbCore/Core parts fully, and open it for the user's visual verification (capture a template, confirm
no files appear in the working dir, confirm an old path-based `.bot` still matches after load). Do **not**
self-merge. Wiki edit + pointer bump happen at merge time.
