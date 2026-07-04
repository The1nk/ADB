# Rip out file-based image templates — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Templates come only from Capture, embedded straight into the `.bot` (no working-dir files); the template field becomes a `templateName` label; matching is embedded-bytes only; old path-based bots migrate on load.

**Architecture:** `ITemplateMatcher` loses its path overload; `TemplateMatchCore` matches embedded bytes only and exposes a `templateName` label field (`ConfigFieldType.ImageTemplate`, renamed from `ImagePath`). `TemplateEmbedder.Migrate` embeds-from-path one last time on load, derives `templateName`, drops `templatePath`. The Capture button captures to a temp file, embeds bytes + confidence into the node, and deletes the temp files. Browse is removed.

**Tech Stack:** C# / .NET 10, WPF, OpenCvSharp4, xUnit.

**Spec:** `Docs/superpowers/specs/2026-07-04-rip-out-file-based-templates.md`

**Execution note:** Changes the Capture UI + template field rendering → **parked** slice (build, unit-test AdbCore/Core fully, open for the user's visual verification; do NOT self-merge). Wiki edit + pointer bump at merge time. **Each task must leave the whole solution building** (`dotnet build ADB.slnx`) — several changes are cross-project and atomic.

---

## File map

| File | Change |
| --- | --- |
| `AdbCore/Screen/ITemplateMatcher.cs` | Remove `Match(Bitmap, string, double)` |
| `AdbCore/Screen/OpenCvSharpTemplateMatcher.cs` | Remove path impl |
| `AdbCore.Tests/Screen/FakeScreenDependencies.cs`, `AdbCore.Tests/**` fakes | Drop path overload |
| `AdbCore/Actions/BuiltIn/TemplateMatchCore.cs` | `templateName` field + embedded-only `HasTemplate`/`MatchConfigured` |
| `AdbCore/Actions/ConfigFieldType.cs` | `ImagePath` → `ImageTemplate` |
| `AdbCore/Actions/BuiltIn/**` 6 image actions | (auto, via `TemplateNameField()`) |
| `BotBuilder.Core/TemplateEmbedder.cs` | `Migrate` (embed→name→drop path) |
| `BotBuilder.Core/Properties/ConfigFieldViewModel.cs` | `ImageTemplate` branch + `SetCapturedTemplate` |
| `BotBuilder/ConfigFieldTemplateSelector.cs`, `MainWindow.xaml` | Field-type rename, remove Browse |
| `BotBuilder/MainWindow.xaml.cs` | `CaptureField_Click` → temp + embed + cleanup |
| tests (AdbCore.Tests, BotBuilder.Core.Tests) | Migration, HasTemplate, VM, existing round-trips |
| `CLAUDE.md`, `README.md`, wiki | Docs |

---

## Task 1: Matcher — embedded-bytes only

**Files:** `AdbCore/Screen/ITemplateMatcher.cs`, `AdbCore/Screen/OpenCvSharpTemplateMatcher.cs`,
`AdbCore/Actions/BuiltIn/TemplateMatchCore.cs` (MatchConfigured), and every `ITemplateMatcher`
implementer/fake: `AdbCore.Tests/Screen/FakeScreenDependencies.cs`,
`AdbCore.Tests/Actions/BuiltIn/WaitForImageActionTests.cs`, `AdbCore.Tests/Execution/FindImageRetryTests.cs`,
`BotCapture.Core.Tests/Fakes.cs`.

- [ ] **Step 1: Update `TemplateMatchCore.MatchConfigured` (and `HasTemplate`) to embedded-only**

In `TemplateMatchCore.cs`, replace `HasTemplate`:
```csharp
    /// <summary>True when the config carries an embedded template image (base64). Templates are
    /// capture-embedded; there is no file-path fallback.</summary>
    public static bool HasTemplate(IReadOnlyDictionary<string, object> config)
        => !string.IsNullOrWhiteSpace(ConfigValues.GetString(config, TemplateImageKey));
```
and replace `MatchConfigured`:
```csharp
    // Matches the embedded base64 template. No template ⇒ null (the action's HasTemplate gate fails the
    // run early, so this is only defensive).
    private static MatchResult? MatchConfigured(Bitmap haystack, IReadOnlyDictionary<string, object> config, ITemplateMatcher matcher, double confidence)
    {
        var embedded = ConfigValues.GetString(config, TemplateImageKey);
        return string.IsNullOrWhiteSpace(embedded)
            ? null
            : matcher.Match(haystack, Convert.FromBase64String(embedded), confidence);
    }
```

- [ ] **Step 2: Remove the path overload from the interface**

`AdbCore/Screen/ITemplateMatcher.cs` — delete the line
`MatchResult? Match(Bitmap haystack, string templatePath, double minConfidence);` and update the `<summary>`
to say the template is supplied as in-memory PNG bytes only.

- [ ] **Step 3: Remove the path impl + fix all implementers/fakes**

- `OpenCvSharpTemplateMatcher.cs` — delete the `Match(Bitmap, string, double)` method (the one doing
  `Cv2.ImRead`/file read); keep the `byte[]` method.
- In each test fake listed above, delete the `Match(Bitmap, string, double)` member. If a fake's byte
  overload delegated to the path one, make it standalone. If a test *called* `Match(path,…)` on the fake to
  set up an expectation, change it to the byte overload (decode a small PNG byte[] the test already has, or
  assert via the byte overload).

- [ ] **Step 4: Build + test**

Run: `dotnet build ADB.slnx -clp:ErrorsOnly` → 0 errors.
Run: `dotnet test AdbCore.Tests` → 0 failures (update any path-based matcher/HasTemplate test to embedded).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Screen/ITemplateMatcher.cs AdbCore/Screen/OpenCvSharpTemplateMatcher.cs AdbCore/Actions/BuiltIn/TemplateMatchCore.cs AdbCore.Tests/
git commit -m "Templates: matcher is embedded-bytes only (drop the file-path overload + fallback)"
```
Trailer: `Claude-Session: https://claude.ai/code/session_01CmX3RvuoA9B7XJ8WCUoFgU`

---

## Task 2: `templateName` field + `ImageTemplate` field type (atomic cross-project rename)

**Files:** `AdbCore/Actions/ConfigFieldType.cs`, `AdbCore/Actions/BuiltIn/TemplateMatchCore.cs`,
`BotBuilder.Core/Properties/ConfigFieldViewModel.cs`, `BotBuilder/ConfigFieldTemplateSelector.cs`,
`BotBuilder/MainWindow.xaml`. All in ONE commit (the enum rename must land everywhere at once).

- [ ] **Step 1: Rename the enum member**

`AdbCore/Actions/ConfigFieldType.cs`: rename `ImagePath` → `ImageTemplate`.

- [ ] **Step 2: `TemplateMatchCore` — `templateName` field**

Add `public const string TemplateNameKey = "templateName";` (keep `TemplatePathKey` for migration reads).
Replace `TemplatePathField()` with:
```csharp
    public static ConfigField TemplateNameField() => new() { Key = TemplateNameKey, Label = "Template Name", Type = ConfigFieldType.ImageTemplate };
```
Update the six image actions / `ScreenActionBase` that call `TemplatePathField()` to call
`TemplateNameField()` (same call sites; grep `TemplatePathField`).

- [ ] **Step 3: `ConfigFieldViewModel`**

- In the `Value` setter, replace the `if (Type == ConfigFieldType.ImagePath) { …path supersede… }` block with:
```csharp
            if (Type == ConfigFieldType.ImageTemplate)
            {
                OnPropertyChanged(nameof(EmbeddedImageBase64));
            }
```
  (Remove the `File.Exists`/path-supersede logic — an `ImageTemplate` value is just the `templateName` label.)
- Add:
```csharp
    /// <summary>Stores a freshly captured template as the node's embedded base64 image and refreshes the
    /// preview. Called by the Capture button — templates are embedded, never written to a working file.</summary>
    public void SetCapturedTemplate(byte[] png)
    {
        _node.Config[TemplateMatchCore.TemplateImageKey] = Convert.ToBase64String(png);
        OnPropertyChanged(nameof(EmbeddedImageBase64));
        _onChanged();
    }
```
- Keep `EmbeddedImageBase64` as-is. (The `using System.IO;` for `File` may become unused — remove it if the
  build warns/needs it; the project builds 0-warn.)

- [ ] **Step 4: Template selector + XAML**

- `BotBuilder/ConfigFieldTemplateSelector.cs`: rename the `ImagePathTemplate` property → `ImageTemplateTemplate`
  and its `ConfigFieldType.ImagePath` case → `ConfigFieldType.ImageTemplate`.
- `BotBuilder/MainWindow.xaml`: rename the `FieldImagePath` `DataTemplate` key → `FieldImageTemplate`; in the
  `ConfigFieldTemplateSelector` element change `ImagePathTemplate="{StaticResource FieldImagePath}"` →
  `ImageTemplateTemplate="{StaticResource FieldImageTemplate}"`. In that template, **remove the Browse…
  Button** (the one with `Click="BrowseField_Click"`); keep the Template-Name `TextBox` (bound to `Value`),
  the **Capture** Button, and the preview `Image`/`MultiBinding`.

- [ ] **Step 5: Build**

Run: `dotnet build ADB.slnx -clp:ErrorsOnly` → 0 errors (this proves the rename is consistent across
AdbCore + BotBuilder.Core + WPF).

- [ ] **Step 6: Commit**

```bash
git add AdbCore/Actions/ConfigFieldType.cs AdbCore/Actions/BuiltIn/ BotBuilder.Core/Properties/ConfigFieldViewModel.cs BotBuilder/ConfigFieldTemplateSelector.cs BotBuilder/MainWindow.xaml
git commit -m "Templates: template field is a name label (ImageTemplate), not a file path; add SetCapturedTemplate; remove Browse"
```
Trailer: `Claude-Session: https://claude.ai/code/session_01CmX3RvuoA9B7XJ8WCUoFgU`

---

## Task 3: `TemplateEmbedder.Migrate` + tests

**Files:** `BotBuilder.Core/TemplateEmbedder.cs`, `BotBuilder.Core/BotEditorViewModel.cs` (Open/Save call
sites, if the method name changes), `BotBuilder.Core.Tests/` (migration tests + update existing embed tests).

- [ ] **Step 1: Write the failing migration tests**

In a new `BotBuilder.Core.Tests/TemplateMigrationTests.cs` (mirror the existing TemplateEmbedder test setup —
find it via grep for `TemplateEmbedder` in tests and copy its `Bot`/`BotAction` construction), assert:
`Migrate` with (a) `templatePath` + a readable file (injected `read` returns bytes) → sets `templateImage`
= base64, `templateName` = `Path.GetFileName(path)`, and **removes** `templatePath`; (b) already-embedded
`templateImage` + a `templatePath` → sets `templateName` from the basename, drops `templatePath`, keeps the
bytes; (c) `templatePath` + missing file (`read` returns null) → no `templateImage`, `templateName` =
basename, `templatePath` removed; (d) a nested bot's action is migrated too. Include the exact base64 /
`read` fake the existing embed tests use.

- [ ] **Step 2: Run to verify fail** — `dotnet test BotBuilder.Core.Tests --filter "FullyQualifiedName~TemplateMigrationTests"` → FAIL (`Migrate` undefined).

- [ ] **Step 3: Rewrite `TemplateEmbedder`**

```csharp
using System.IO;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Models;

namespace BotBuilder.Core;

/// <summary>Ensures every image action carries its template as embedded base64 (so a .bot is
/// self-contained) and migrates legacy file-path templates: embed a still-present source one last time,
/// derive the display <c>templateName</c> from its basename, and drop the obsolete <c>templatePath</c>.</summary>
public static class TemplateEmbedder
{
    /// <summary>Reads a file's bytes, or null when the path is empty or the file does not exist.</summary>
    public static byte[]? ReadFileIfExists(string? path)
        => !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? File.ReadAllBytes(path) : null;

    /// <summary>Embeds not-yet-embedded templates from a still-readable source path, sets a display
    /// <c>templateName</c> from the legacy basename when absent, and removes <c>templatePath</c>. Recurses
    /// into nested bots. Idempotent. Mutates and returns the bot.</summary>
    public static Bot Migrate(Bot bot, Func<string?, byte[]?> read)
    {
        foreach (var action in bot.Actions)
        {
            var path = Get(action, TemplateMatchCore.TemplatePathKey);
            var hasImage = !string.IsNullOrWhiteSpace(Get(action, TemplateMatchCore.TemplateImageKey));

            if (!hasImage && read(path) is byte[] bytes)
            {
                action.Config[TemplateMatchCore.TemplateImageKey] = Convert.ToBase64String(bytes);
            }

            if (!string.IsNullOrWhiteSpace(path))
            {
                if (string.IsNullOrWhiteSpace(Get(action, TemplateMatchCore.TemplateNameKey)))
                {
                    action.Config[TemplateMatchCore.TemplateNameKey] = Path.GetFileName(path);
                }
                action.Config.Remove(TemplateMatchCore.TemplatePathKey);
            }
        }

        foreach (var nested in bot.NestedBots)
        {
            Migrate(nested, read);
        }

        return bot;
    }

    /// <summary>Save-time normalization: same as <see cref="Migrate"/> (capture embeds directly, so there is
    /// nothing extra to do). Kept as a distinct entry point for the save path.</summary>
    public static Bot PrepareForSave(Bot bot, Func<string?, byte[]?> read) => Migrate(bot, read);

    private static string Get(BotAction action, string key)
        => action.Config.TryGetValue(key, out var v) ? ConfigValues.AsString(v) : string.Empty;
}
```

- [ ] **Step 4: Fix call sites** — in `BotEditorViewModel`, wherever `TemplateEmbedder.Embed(...)` was called
(Open path), call `TemplateEmbedder.Migrate(...)`; `PrepareForSave` callers are unchanged. Grep
`TemplateEmbedder.Embed` and update. Update/replace the existing `TemplateEmbedder` tests that asserted the
old `Embed`/basename-on-save behavior to the new `Migrate` semantics (embed + name + drop path).

- [ ] **Step 5: Run to verify pass** — `dotnet test BotBuilder.Core.Tests` → 0 failures.

- [ ] **Step 6: Commit**

```bash
git add BotBuilder.Core/TemplateEmbedder.cs BotBuilder.Core/BotEditorViewModel.cs BotBuilder.Core.Tests/
git commit -m "Templates: migrate legacy templatePath on load (embed + templateName + drop path)"
```
Trailer: `Claude-Session: https://claude.ai/code/session_01CmX3RvuoA9B7XJ8WCUoFgU`

---

## Task 4: Capture to temp → embed → cleanup

**Files:** `BotBuilder/MainWindow.xaml.cs` (`CaptureField_Click`).

- [ ] **Step 1: Rewrite `CaptureField_Click`**

Replace the body (keeping the `exe`/`ResolveCapture` guard and the `confidenceField` capture) so the output
goes to a temp file that is embedded and then deleted:

```csharp
    private void CaptureField_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ConfigFieldViewModel field })
        {
            return;
        }

        var exe = ResolveCapture();
        if (exe is null)
        {
            MessageBox.Show(
                "BotCapture couldn't be found. Try reinstalling ADB, and check whether your antivirus quarantined it.",
                "Capture", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Capture into a temp file we embed and then delete — templates live in the .bot, never on disk.
        var dir = Path.Combine(Path.GetTempPath(), "ADB", "Captures");
        Directory.CreateDirectory(dir);
        var tempPath = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".png");

        var confidenceField = ConfidenceFieldOrNull();

        CaptureLauncher.Launch(exe, tempPath, saved =>
        {
            try
            {
                if (!saved || !File.Exists(tempPath))
                {
                    return; // cancelled — leave the field unchanged
                }

                field.SetCapturedTemplate(File.ReadAllBytes(tempPath));
                if (confidenceField is not null &&
                    BotBuilder.Core.Integration.ConfidenceSidecarReader.Read(tempPath) is double c)
                {
                    confidenceField.Value = c;
                }
            }
            finally
            {
                TryDelete(tempPath);
                TryDelete(tempPath + ".meta.json");
            }
        });
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort temp cleanup */ }
    }
```

Ensure `using System.IO;` is present in the file (for `Path`/`File`/`Directory`).

- [ ] **Step 2: Build** — `dotnet build ADB.slnx -clp:ErrorsOnly` → 0 errors.

- [ ] **Step 3: Commit**

```bash
git add BotBuilder/MainWindow.xaml.cs
git commit -m "Templates: Capture writes to a temp file, embeds it, and cleans up (no working-dir files)"
```
Trailer: `Claude-Session: https://claude.ai/code/session_01CmX3RvuoA9B7XJ8WCUoFgU`

---

## Task 5: BotRunner check + full-suite verification

- [ ] **Step 1: Confirm BotRunner needs no change** — `grep -rn "templatePath\|ReadAllBytes\|ImRead" BotRunner/`.
  If nothing reads a template path from disk (expected — it runs the actions through `TemplateMatchCore`),
  no change. If something does, remove it and note it in the commit.

- [ ] **Step 2: Full solution build + test**

Run: `dotnet build ADB.slnx -clp:ErrorsOnly` → 0 errors, 0 warnings.
Run: `dotnet test ADB.slnx` → 0 failures across all projects.

- [ ] **Step 3: Commit (only if BotRunner changed; else skip)**

```bash
git add BotRunner/
git commit -m "Templates: BotRunner matches embedded-only (no disk template read)"
```

---

## Task 6: Documentation

**Files:** `CLAUDE.md`, `README.md`, `ADB.wiki/Actions-Reference.md` (+ Image-Matching page) — wiki deferred
to merge.

- [ ] **Step 1: CLAUDE.md** — in the `.bot` format / image-actions notes, state: image templates are
  **embedded-only** (`templateImage` base64 + a `templateName` display label); `templatePath` is a **legacy
  key migrated away on load** (`TemplateEmbedder.Migrate`); there is no disk-read fallback and no Browse —
  templates come from Capture, which embeds directly.

- [ ] **Step 2: README.md** — wherever template capture is described (the *arsenal* image-matching line
  and/or BotCapture mention), note templates are **captured straight into the bot — no loose PNGs, no
  working-directory clutter** (goblin voice, accurate).

- [ ] **Step 3: Wiki (deferred to merge)** — note in the PR/merge summary: Actions-Reference image rows
  `templatePath` → `templateName`; remove any Image-Matching page text about external PNG files or
  `.meta.json` sidecars in the working dir. Do not push the wiki from this unmerged parked branch.

- [ ] **Step 4: Commit main-repo docs**

```bash
git add CLAUDE.md README.md
git commit -m "Docs: image templates are embedded-only (templateName label, no file paths)"
```
Trailer: `Claude-Session: https://claude.ai/code/session_01CmX3RvuoA9B7XJ8WCUoFgU`

---

## Final verification

- [ ] `dotnet test ADB.slnx` → all green.
- [ ] `dotnet build ADB.slnx -clp:ErrorsOnly` → clean, 0 warnings.
- [ ] **Park for the user's visual check:** in BotBuilder, add a Find Image node, type a Template Name, click
  Capture, snip a region → the preview shows the template, **and no `File`/`.meta.json` appears in the
  working directory**. Open an OLD path-based `.bot` whose PNG still exists → it still matches (migrated on
  load); save it and confirm `templatePath` is gone and `templateName`/`templateImage` remain. Do NOT
  self-merge — this is a visual slice for the user to verify and merge.
