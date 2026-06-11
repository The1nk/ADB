# Save / Save As + First-Save Naming (A2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `Ctrl+S` saves in place when the document already has a file (no prompt); on a first save it prompts and sets the bot's `Name` from the chosen filename (persisted in that same save). A new **Save As** (`Ctrl+Shift+S`) always prompts and does NOT rename. Test Run no longer pollutes the document's file path.

**Architecture:** Three small `BotEditorViewModel` methods carry the policy (testable, no WPF): `Save()` (rewrite to the current file), `SaveAsNew(path)` (derive `Name` from the filename, then save), and `ExportTo(path)` (serialize without mutating `FilePath`/`IsDirty` — for Test Run). `MainWindow` wires Save / Save As / shortcuts and switches Test Run to `ExportTo`.

**Tech Stack:** .NET 10 WPF, BotBuilder.Core, xUnit.

This refines the merged Feature A. Reference: `Docs/superpowers/specs/2026-06-10-title-bar-and-nested-bots-design.md` (Feature A) + user follow-up (first-save naming; Save vs Save As).

Work in worktree `C:\git\ADB-save-as` (branch `worktree-save-as`). Build/test from the worktree root.

---

### Task 1: `BotEditorViewModel` — `Save()`, `SaveAsNew(path)`, `ExportTo(path)`

**Files:**
- Modify: `BotBuilder.Core/BotEditorViewModel.cs`
- Test: `BotBuilder.Core.Tests/BotEditorViewModelSaveTests.cs` (create)

The existing `Save(string path)` (writes via the serializer, sets `FilePath`, clears `IsDirty`) stays as-is and is the Save As path.

- [ ] **Step 1: Write the failing tests**

Create `BotBuilder.Core.Tests/BotEditorViewModelSaveTests.cs`:

```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Serialization;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class BotEditorViewModelSaveTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    private static string TempBotPath() => Path.Combine(Path.GetTempPath(), $"adb-save-{Guid.NewGuid():N}.bot");

    [Fact]
    public void SaveAsNew_SetsBotNameFromFileName_AndPersistsThatSave()
    {
        var editor = NewEditor();
        var path = Path.Combine(Path.GetTempPath(), $"GoToPlayerMenu-{Guid.NewGuid():N}.bot");
        try
        {
            editor.SaveAsNew(path);

            Assert.Equal(Path.GetFileNameWithoutExtension(path), editor.BotName);
            Assert.Equal(path, editor.FilePath);
            Assert.False(editor.IsDirty);

            // The name was persisted in THIS save, not just held in memory.
            var loaded = new BotSerializer().Load(path);
            Assert.Equal(Path.GetFileNameWithoutExtension(path), loaded.Name);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Save_NoArg_RewritesExistingFile()
    {
        var editor = NewEditor();
        var path = TempBotPath();
        try
        {
            editor.SaveAsNew(path);
            editor.AddNode("control.start", 0, 0); // dirty
            Assert.True(editor.IsDirty);

            editor.Save(); // no prompt, same path

            Assert.Equal(path, editor.FilePath);
            Assert.False(editor.IsDirty);
            Assert.Single(new BotSerializer().Load(path).Actions); // the added node was written
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Save_NoArg_WithoutFilePath_Throws()
    {
        var editor = NewEditor();
        Assert.Throws<InvalidOperationException>(() => editor.Save());
    }

    [Fact]
    public void Save_WithPath_DoesNotChangeName()
    {
        var editor = NewEditor();
        editor.BotName = "KeepMe";
        var path = Path.Combine(Path.GetTempPath(), $"Different-{Guid.NewGuid():N}.bot");
        try
        {
            editor.Save(path); // Save As semantics — no rename
            Assert.Equal("KeepMe", editor.BotName);
            Assert.Equal(path, editor.FilePath);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExportTo_WritesFile_ButDoesNotTouchDocumentState()
    {
        var editor = NewEditor();
        editor.AddNode("control.start", 0, 0); // dirty, FilePath still null
        var path = TempBotPath();
        try
        {
            editor.ExportTo(path);

            Assert.Null(editor.FilePath);     // unchanged
            Assert.True(editor.IsDirty);      // unchanged
            Assert.True(File.Exists(path));   // but the file was written
            Assert.Single(new BotSerializer().Load(path).Actions);
        }
        finally { File.Delete(path); }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~BotEditorViewModelSaveTests"`
Expected: FAIL — `Save()` / `SaveAsNew` / `ExportTo` don't exist.

- [ ] **Step 3: Add the methods**

In `BotBuilder.Core/BotEditorViewModel.cs`, find the existing `Save(string path)` method:
```csharp
    public void Save(string path)
    {
        _serializer.Save(DocumentMapper.ToBot(this), path);
        FilePath = path;
        IsDirty = false;
    }
```
Add these three methods right after it:
```csharp
    /// <summary>Saves to the current file. Requires a prior save/open (<see cref="FilePath"/> set).</summary>
    public void Save()
        => Save(FilePath ?? throw new InvalidOperationException("No file path set; use Save(path) or SaveAsNew(path)."));

    /// <summary>First-time save: derives the bot <see cref="BotName"/> from the file name so it persists in this
    /// very save, then writes to <paramref name="path"/>.</summary>
    public void SaveAsNew(string path)
    {
        BotName = Path.GetFileNameWithoutExtension(path);
        Save(path);
    }

    /// <summary>Serializes the document to <paramref name="path"/> WITHOUT changing <see cref="FilePath"/> or
    /// <see cref="IsDirty"/>. Used to write a throwaway copy (e.g. a Test Run temp file) without making the editor
    /// believe it has been saved there.</summary>
    public void ExportTo(string path)
        => _serializer.Save(DocumentMapper.ToBot(this), path);
```
Add `using System.IO;` to the file's usings if not already present (for `Path`).

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~BotEditorViewModelSaveTests"`
Expected: PASS (5).

- [ ] **Step 5: Commit**

```bash
git add BotBuilder.Core/BotEditorViewModel.cs BotBuilder.Core.Tests/BotEditorViewModelSaveTests.cs
git commit -m "BotEditorViewModel: add Save(), SaveAsNew(path), ExportTo(path)"
```

---

### Task 2: MainWindow — Save in place, Save As, Ctrl+Shift+S, Test Run uses ExportTo

**Files:**
- Modify: `BotBuilder/MainWindow.xaml.cs`
- Modify: `BotBuilder/MainWindow.xaml`

- [ ] **Step 1: Rewrite `Save_Click` and add `SaveAs_Click` + a prompt helper**

In `BotBuilder/MainWindow.xaml.cs`, replace the existing `Save_Click` method:
```csharp
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = BotFilter,
            DefaultExt = ".bot",
            AddExtension = true,        // append ".bot" when the user types a bare name
            FileName = _editor.BotName, // pre-fill with the current bot name (e.g. "Untitled Bot")
        };
        if (dialog.ShowDialog(this) == true)
        {
            _editor.Save(dialog.FileName);
        }
    }
```
with:
```csharp
    // Ctrl+S / File>Save: rewrite the current file in place once the document has one; on a first save, prompt
    // and adopt the chosen filename as the bot's Name (persisted in that save).
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_editor.FilePath is not null)
        {
            _editor.Save();
            return;
        }

        if (PromptForBotPath() is string path)
        {
            _editor.SaveAsNew(path);
        }
    }

    // Ctrl+Shift+S / File>Save As: always prompt; does NOT change the bot's Name.
    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        if (PromptForBotPath() is string path)
        {
            _editor.Save(path);
        }
    }

    private string? PromptForBotPath()
    {
        var dialog = new SaveFileDialog
        {
            Filter = BotFilter,
            DefaultExt = ".bot",
            AddExtension = true,        // append ".bot" when the user types a bare name
            FileName = _editor.BotName, // pre-fill with the current bot name (e.g. "Untitled Bot")
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }
```

- [ ] **Step 2: Point Test Run at `ExportTo`**

In `BotBuilder/MainWindow.xaml.cs`, in `TestRun_Click`, find:
```csharp
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(botPath)!);
        _editor.Save(botPath);
```
and change the `_editor.Save(botPath);` line to:
```csharp
        _editor.ExportTo(botPath);
```
(The temp Test Run file must NOT become the document's saved path — otherwise a later Ctrl+S would silently overwrite the temp file instead of prompting / saving the real document.)

- [ ] **Step 3: Add the Ctrl+Shift+S shortcut**

In `BotBuilder/MainWindow.xaml.cs` `Window_KeyDown`, find the existing `Ctrl+S` branch:
```csharp
        else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.OriginalSource is TextBox) return;
            Save_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
```
and add this branch immediately BEFORE it (Ctrl+Shift+S has distinct modifiers, so it won't be caught by the exact-match Ctrl+S branch; placing it first keeps the intent clear):
```csharp
        else if (e.Key == Key.S && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (e.OriginalSource is TextBox) return;
            SaveAs_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
```

- [ ] **Step 4: Update the File menu (Save loses the ellipsis; add Save As)**

In `BotBuilder/MainWindow.xaml`, replace:
```xml
                <MenuItem Header="_Save..." Click="Save_Click" InputGestureText="Ctrl+S" />
```
with:
```xml
                <MenuItem Header="_Save" Click="Save_Click" InputGestureText="Ctrl+S" />
                <MenuItem Header="Save _As..." Click="SaveAs_Click" InputGestureText="Ctrl+Shift+S" />
```
(Save loses its `...` because it no longer always prompts; Save As keeps the ellipsis since it always does.)

- [ ] **Step 5: Build + manual verification**

Run: `dotnet build ADB.slnx` — Build succeeded.

Launch `dotnet run --project BotBuilder` and verify:
- New bot → Ctrl+S prompts; type "Foo" → saves; title shows `ADB Bot Builder: Foo.bot`; the bot's Name is "Foo" (reopen to confirm).
- Edit again → Ctrl+S writes silently to the same file (no prompt); title's `*` clears.
- Ctrl+Shift+S (Save As) → prompts; save as "Bar.bot" → file written; the in-editor title/name still reflects the new path but the Name is unchanged from before (Save As doesn't rename).
- Run a Test Run, then Ctrl+S → it still saves to the real document path (or prompts if never saved), NOT the temp Test Run file.

- [ ] **Step 6: Commit**

```bash
git add BotBuilder/MainWindow.xaml.cs BotBuilder/MainWindow.xaml
git commit -m "Save in place + Save As (Ctrl+Shift+S); Test Run uses ExportTo"
```

---

### Task 3: Full suite green

- [ ] **Step 1: Run the whole suite**

Run: `dotnet test ADB.slnx`
Expected: PASS, no regressions.

---

## Self-Review

- **Coverage:** first-save naming persisted in the same save (Task 1 `SaveAsNew` + test); Save in place without prompt once a file exists (Task 1 `Save()` + Task 2 `Save_Click`); Save As always prompts and doesn't rename (Task 2 `SaveAs_Click` + Task 1 `Save(path)` test); Ctrl+Shift+S (Task 2); Test Run no longer hijacks the document path (Task 1 `ExportTo` + Task 2). ✓
- **Regression guard:** the `ExportTo` switch is the key fix — without it, the new save-in-place behavior would make Ctrl+S overwrite the Test Run temp file. Explicitly tested that `ExportTo` leaves `FilePath`/`IsDirty` untouched. ✓
- **Placeholders:** none. ✓
- **Type consistency:** `Save()`/`SaveAsNew`/`ExportTo`/`Save(path)` used identically across VM and MainWindow. ✓
