# M8c — BotCapture "Capture" Button on Image Fields Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a **Capture** button to image-path fields in the Properties panel that launches BotCapture's integrated `--output` mode (from M6c), reloads the field with the saved PNG, and pre-fills the action's confidence from the `.meta.json` sidecar — also pre-filling confidence on the existing **Browse** path. This closes the build → capture → run loop and completes M8.

**Architecture:** A testable `ConfidenceSidecarReader` (BotBuilder.Core.Integration) reads the `<image>.png.meta.json` confidence (read-only mirror of BotCapture's writer, so the Builder needn't reference BotCapture). A WPF `CaptureLauncher` spawns `BotCapture.exe --output <path>` and reports the save/cancel result on the UI thread. The image-field template gets a Capture button; both Capture and Browse set the field value and pre-fill the sibling confidence field. Reuses M8a's `ExeLocator`.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), WPF, System.Text.Json, xUnit. Reuses `ExeLocator` (BotBuilder.Core.Integration), `ConfigFieldViewModel`/`PropertiesViewModel`, and BotCapture's `--output` mode + sidecar format. Per `Docs/Specs/2026-06-03-m8-integration-design.md` §5.

---

## File Structure

**BotBuilder.Core (new, tested):**
- `Integration/ConfidenceSidecarReader.cs` — `double? Read(string imagePath)`; tolerant (null on missing/corrupt).

**BotBuilder (WPF, new):**
- `CaptureLauncher.cs` — spawns `BotCapture.exe --output <path>`, invokes a UI-thread callback with the saved/cancel result.

**BotBuilder (WPF, modified):**
- `MainWindow.xaml` — a **Capture** button in the `FieldImagePath` data template.
- `MainWindow.xaml.cs` — `CaptureField_Click` + `ResolveCapture`; pre-fill confidence on Capture and Browse.

**Tests:** `BotBuilder.Core.Tests/Integration/ConfidenceSidecarReaderTests.cs`.

---

## Task 1: ConfidenceSidecarReader

**Files:**
- Create: `BotBuilder.Core/Integration/ConfidenceSidecarReader.cs`
- Test: `BotBuilder.Core.Tests/Integration/ConfidenceSidecarReaderTests.cs`

- [ ] **Step 1: Write the failing tests** — `BotBuilder.Core.Tests/Integration/ConfidenceSidecarReaderTests.cs`:

```csharp
using System.IO;
using BotBuilder.Core.Integration;

namespace BotBuilder.Core.Tests.Integration;

public class ConfidenceSidecarReaderTests
{
    private static string TempImagePath() => Path.Combine(Path.GetTempPath(), $"adb_{Guid.NewGuid():N}.png");

    [Fact]
    public void Read_ReturnsConfidence_FromSidecar()
    {
        var image = TempImagePath();
        File.WriteAllText(image + ".meta.json", """{"confidence":0.83}""");
        try
        {
            Assert.Equal(0.83, ConfidenceSidecarReader.Read(image)!.Value, 3);
        }
        finally { File.Delete(image + ".meta.json"); }
    }

    [Fact]
    public void Read_MissingSidecar_ReturnsNull()
    {
        Assert.Null(ConfidenceSidecarReader.Read(TempImagePath()));
    }

    [Fact]
    public void Read_CorruptSidecar_ReturnsNull()
    {
        var image = TempImagePath();
        File.WriteAllText(image + ".meta.json", "{ not valid json");
        try
        {
            Assert.Null(ConfidenceSidecarReader.Read(image));
        }
        finally { File.Delete(image + ".meta.json"); }
    }

    [Fact]
    public void Read_SidecarWithoutConfidenceKey_ReturnsNull()
    {
        var image = TempImagePath();
        File.WriteAllText(image + ".meta.json", """{"other":1}""");
        try
        {
            Assert.Null(ConfidenceSidecarReader.Read(image));
        }
        finally { File.Delete(image + ".meta.json"); }
    }
}
```

- [ ] **Step 2: Run to verify they fail** — `dotnet test BotBuilder.Core.Tests/BotBuilder.Core.Tests.csproj --filter "FullyQualifiedName~ConfidenceSidecarReaderTests"` → FAIL (type missing).

- [ ] **Step 3: Implement** — `BotBuilder.Core/Integration/ConfidenceSidecarReader.cs`:

```csharp
using System.Text.Json;

namespace BotBuilder.Core.Integration;

/// <summary>Reads the confidence threshold from an image's <c>&lt;image&gt;.png.meta.json</c> sidecar (the
/// format BotCapture writes). Returns null when the sidecar is absent, unreadable, or has no numeric
/// <c>confidence</c>. A read-only mirror of BotCapture's sidecar writer, kept here so the Builder needn't
/// reference BotCapture.</summary>
public static class ConfidenceSidecarReader
{
    public static double? Read(string imagePath)
    {
        try
        {
            var path = imagePath + ".meta.json";
            if (!File.Exists(path))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number
                ? c.GetDouble()
                : null;
        }
        catch
        {
            return null; // missing/corrupt/locked sidecar
        }
    }
}
```

- [ ] **Step 4: Run to verify they pass** — same filter → PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add BotBuilder.Core/Integration/ConfidenceSidecarReader.cs BotBuilder.Core.Tests/Integration/ConfidenceSidecarReaderTests.cs
git commit -m "feat(builder): add ConfidenceSidecarReader (.meta.json confidence, tolerant)"
```

---

## Task 2: Capture button + CaptureLauncher + wiring (WPF, visual)

No unit tests — verified by building and running. Adds the Capture button, the process launcher, and the
confidence pre-fill on both Capture and Browse.

**Files:**
- Create: `BotBuilder/CaptureLauncher.cs`
- Modify: `BotBuilder/MainWindow.xaml`, `BotBuilder/MainWindow.xaml.cs`

- [ ] **Step 1: Create `CaptureLauncher.cs`** — `BotBuilder/CaptureLauncher.cs`:

```csharp
using System.Diagnostics;
using System.Threading;

namespace BotBuilder;

/// <summary>Launches BotCapture in integrated single-shot mode (<c>--output &lt;path&gt;</c>) and reports the
/// result on the captured UI <see cref="SynchronizationContext"/>: <c>true</c> when it saved (exit 0),
/// <c>false</c> on cancel/any non-zero exit.</summary>
public static class CaptureLauncher
{
    public static void Launch(string exePath, string outputPath, Action<bool> onCompleted)
    {
        var sync = SynchronizationContext.Current ?? new SynchronizationContext();

        var psi = new ProcessStartInfo(exePath) { UseShellExecute = false };
        psi.ArgumentList.Add("--output");
        psi.ArgumentList.Add(outputPath);

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.Exited += (_, _) =>
        {
            var saved = process.ExitCode == 0;
            sync.Post(_ =>
            {
                onCompleted(saved);
                process.Dispose();
            }, null);
        };

        process.Start();
    }
}
```

- [ ] **Step 2: Add a Capture button to the image-field template.** In `BotBuilder/MainWindow.xaml`, find
  the `<DataTemplate x:Key="FieldImagePath">`. Its inner `<DockPanel>` has a `Browse...` button docked
  right and a `TextBox`. Add a **Capture** button docked right, immediately before the existing Browse
  button:

```xml
                    <Button DockPanel.Dock="Right" Content="Capture" Margin="4,0,0,0" Padding="6,0"
                            Click="CaptureField_Click" />
```

  (Result: the row reads `[ TextBox ............ ] [ Capture ] [ Browse... ]`.)

- [ ] **Step 3: Add `CaptureField_Click` + `ResolveCapture` + confidence pre-fill to `MainWindow.xaml.cs`.**
  Add these members to the `MainWindow` class:

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

        // Output path: the field's current value, or a Save dialog if it's empty.
        var outputPath = field.Value as string;
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            var dialog = new SaveFileDialog { Filter = "PNG image|*.png", DefaultExt = ".png", AddExtension = true };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }
            outputPath = dialog.FileName;
        }

        // Capture the sibling confidence field now (the selection may change while BotCapture is open).
        var confidenceField = ConfidenceFieldOrNull();

        CaptureLauncher.Launch(exe, outputPath, saved =>
        {
            if (!saved)
            {
                return; // cancelled — leave the field unchanged
            }

            field.Value = outputPath;
            if (confidenceField is not null && BotBuilder.Core.Integration.ConfidenceSidecarReader.Read(outputPath) is double c)
            {
                confidenceField.Value = c;
            }
        });
    }

    private static string? ResolveCapture()
        => BotBuilder.Core.Integration.ExeLocator.Locate(
            BotBuilder.Core.Integration.ExeLocator.Candidates(System.AppContext.BaseDirectory, "BotCapture.exe"),
            System.IO.File.Exists);

    /// <summary>The selected action's "confidence" config field, or null when it has none.</summary>
    private ConfigFieldViewModel? ConfidenceFieldOrNull()
        => _editor.Properties.Fields.FirstOrDefault(f => f.Key == "confidence");
```

- [ ] **Step 4: Pre-fill confidence on the Browse path too.** In the existing `BrowseField_Click`, after
  the line `field.Value = dialog.FileName;` (inside the `if (dialog.ShowDialog(this) == true)` block), add a
  confidence pre-fill for image fields:

```csharp
            field.Value = dialog.FileName;
            if (isImage && ConfidenceFieldOrNull() is { } confField
                && BotBuilder.Core.Integration.ConfidenceSidecarReader.Read(dialog.FileName) is double conf)
            {
                confField.Value = conf;
            }
```

- [ ] **Step 5: Build** — `dotnet build ADB.slnx -c Debug --nologo` → 0 warnings, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add BotBuilder/CaptureLauncher.cs BotBuilder/MainWindow.xaml BotBuilder/MainWindow.xaml.cs
git commit -m "feat(builder): image-field Capture button (BotCapture --output) + confidence pre-fill"
```

## Context for Task 2
- `ConfigFieldViewModel` (in `BotBuilder.Core.Properties`) has `.Value` (object?, settable — Number fields
  coerce a `double`), `.Key` (the config key), `.Type`. The Capture/Browse button's `DataContext` is the
  image field's `ConfigFieldViewModel`.
- `_editor.Properties.Fields` (`ObservableCollection<ConfigFieldViewModel>`) is the selected node's fields;
  the confidence field has `Key == "confidence"`. Setting `confidenceField.Value = c` updates that field's
  node config (and the Number text box on screen if it's the selected node).
- `ExeLocator` (BotBuilder.Core.Integration, from M8a) finds the sibling exe; here for `BotCapture.exe`.
- BotCapture's `--output <path>` mode (M6c) saves the cropped template to exactly that path + a
  `<path>.meta.json` sidecar, then exits 0 (cancel/close exits non-zero).
- `MainWindow` already has `using` for `Microsoft.Win32` (`SaveFileDialog`/`OpenFileDialog`),
  `System.Windows` (`MessageBox`), and `System.Linq` (`FirstOrDefault`).

---

## Task 3: Full verification

**Files:** none (verification only).

- [ ] **Step 1: Build the whole solution** — `dotnet build ADB.slnx -c Debug --nologo` → `0 Warning(s) 0 Error(s)`.

- [ ] **Step 2: Run the whole test suite** — `dotnet test ADB.slnx -c Debug --nologo --no-build`. New
  BotBuilder.Core tests this slice: ConfidenceSidecarReader 4. BotBuilder.Core.Tests should total 137
  (was 133). AdbCore 226 / BotCapture.Core 41 / BotRunner 19 unchanged.

- [ ] **Step 3: Manual run (user visual verification).** `dotnet run --project BotBuilder/BotBuilder.csproj -c Debug`:
  - Add a **Find Image** action (it has an Image path + a Confidence field), select it.
  - Click **Capture** on the image field → choose a save path → BotCapture opens; capture a template and
    Save. BotCapture exits; the image field fills with the path, the **preview thumbnail** appears, and the
    **Confidence** field pre-fills from the `.meta.json` sidecar.
  - Cancel/close BotCapture without saving → the field is unchanged.
  - **Browse** to an existing image that has a `.meta.json` sidecar → confidence pre-fills too.
  - Temporarily rename `BotCapture.exe` and click Capture → the friendly "couldn't be found… reinstall /
    antivirus" message appears (no crash).

> Hand off to the user for visual confirmation before opening the PR. After M8c merges, **M8 (Integration)
> is complete** — the build → capture → run loop works end-to-end.
