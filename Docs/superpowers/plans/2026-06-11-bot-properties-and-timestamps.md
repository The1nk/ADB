# Bot Properties Dialog + Populated Timestamps Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Populate `createdAt`/`updatedAt` (no longer `0001-01-01`), track the bot's `Description`, and add a File ▸ Properties dialog to edit this bot's Name + Description (with Created/Updated shown read-only).

**Architecture:** Backend first (BotBuilder.Core, testable): the editor tracks `CreatedAt`/`UpdatedAt`/`BotDescription`, set on New/Save and round-tripped through `DocumentMapper`; new library entries get timestamps; a nested entry's UpdatedAt bumps on sync-back. Then a small testable `BotPropertiesViewModel` + a themed `PropertiesDialog` wired to File ▸ Properties (works in both the root and nested/child editors — it edits whichever bot the window is editing).

**Tech Stack:** .NET 10 WPF, BotBuilder.Core, CommunityToolkit.Mvvm, xUnit.

Reference: user request (2026-06-11). Builds on B1/B2/B3a (merged) and B3b/B3c (this branch — stacks on the parked child-editor PR #60). THEME RULE: the dialog uses `DynamicResource` theme brushes and mirrors an existing dialog's chrome.

Work in worktree `C:\git\ADB-properties` (branch `worktree-bot-properties`). Build/test from the worktree root.

---

### Task 1: Populate timestamps + track Description (backend)

**Files:**
- Modify: `BotBuilder.Core/BotEditorViewModel.cs`
- Modify: `BotBuilder.Core/DocumentMapper.cs`
- Modify: `BotBuilder.Core/NestedBots/NestedBotLibrary.cs`
- Modify: `BotBuilder.Core/NestedBots/NestedBotEditorSession.cs`
- Test: `BotBuilder.Core.Tests/BotMetadataTests.cs` (create)

- [ ] **Step 1: Write the failing tests**

Create `BotBuilder.Core.Tests/BotMetadataTests.cs`:

```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Serialization;
using BotBuilder.Core;
using BotBuilder.Core.NestedBots;
using Xunit;

namespace BotBuilder.Core.Tests;

public class BotMetadataTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    [Fact]
    public void New_SetsCreatedAndUpdatedTimestamps()
    {
        var editor = NewEditor();
        Assert.NotEqual(default, editor.CreatedAt);
        Assert.NotEqual(default, editor.UpdatedAt);
    }

    [Fact]
    public void Save_BumpsUpdatedAt_PreservesCreatedAt_AndPersists()
    {
        var editor = NewEditor();
        var created = editor.CreatedAt;
        var path = Path.Combine(Path.GetTempPath(), $"adb-meta-{Guid.NewGuid():N}.bot");
        try
        {
            editor.Save(path);
            Assert.Equal(created, editor.CreatedAt);          // preserved
            Assert.True(editor.UpdatedAt >= created);          // bumped (>= since fast)

            var loaded = new BotSerializer().Load(path);
            Assert.NotEqual(default, loaded.CreatedAt);
            Assert.NotEqual(default, loaded.UpdatedAt);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Description_RoundTrips()
    {
        var editor = NewEditor();
        editor.BotDescription = "Grinds the player menu.";
        var bot = DocumentMapper.ToBot(editor);
        Assert.Equal("Grinds the player menu.", bot.Description);

        var editor2 = NewEditor();
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        DocumentMapper.Populate(editor2, bot, defs);
        Assert.Equal("Grinds the player menu.", editor2.BotDescription);
    }

    [Fact]
    public void Populate_LoadsTimestamps()
    {
        var editor = NewEditor();
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        var when = new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var bot = new Bot { Id = Guid.NewGuid(), Name = "X", CreatedAt = when, UpdatedAt = when };

        DocumentMapper.Populate(editor, bot, defs);

        Assert.Equal(when, editor.CreatedAt);
        Assert.Equal(when, editor.UpdatedAt);
    }

    [Fact]
    public void AddNew_LibraryEntry_HasTimestamps()
    {
        var lib = new NestedBotLibrary();
        var bot = lib.AddNew("Sub");
        Assert.NotEqual(default, bot.CreatedAt);
        Assert.NotEqual(default, bot.UpdatedAt);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~BotMetadataTests"`
Expected: FAIL — `BotEditorViewModel.CreatedAt`/`UpdatedAt`/`BotDescription` don't exist.

- [ ] **Step 3a: Editor fields + New + Save**

In `BotBuilder.Core/BotEditorViewModel.cs`:
- Add three observable fields alongside the existing ones (`_botName` etc.):
```csharp
    [ObservableProperty] private DateTime _createdAt;
    [ObservableProperty] private DateTime _updatedAt;
    [ObservableProperty] private string _botDescription = string.Empty;
```
- In `New()`, set the metadata (place near `BotName = "Untitled Bot";`):
```csharp
        BotDescription = string.Empty;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
```
- In `Save(string path)`, BEFORE the `_serializer.Save(...)` line, bump the timestamps:
```csharp
        UpdatedAt = DateTime.UtcNow;
        if (CreatedAt == default) { CreatedAt = UpdatedAt; } // safety for bots loaded from old files
```
(Do NOT touch `ExportTo` — a Test Run temp export must not bump the document's UpdatedAt.)

- [ ] **Step 3b: DocumentMapper round-trip**

In `BotBuilder.Core/DocumentMapper.cs`:
- In `ToBot`, right after `var bot = new Bot { Id = editor.BotId, Name = editor.BotName };`, add:
```csharp
        bot.Description = editor.BotDescription;
        bot.CreatedAt = editor.CreatedAt;
        bot.UpdatedAt = editor.UpdatedAt;
```
- In `Populate`, after `editor.LoadFrom(...)` (which sets BotId/BotName) and before the targets loop, add:
```csharp
        editor.BotDescription = bot.Description;
        editor.CreatedAt = bot.CreatedAt;
        editor.UpdatedAt = bot.UpdatedAt;
```

- [ ] **Step 3c: New library entries get timestamps**

In `BotBuilder.Core/NestedBots/NestedBotLibrary.cs` `AddNew`, set timestamps on the new bot:
```csharp
    public Bot AddNew(string name = "Untitled Bot")
    {
        var now = DateTime.UtcNow;
        var bot = new Bot { Id = Guid.NewGuid(), Name = name, CreatedAt = now, UpdatedAt = now };
        _entries.Add(bot);
        return bot;
    }
```

- [ ] **Step 3d: Sync-back bumps a nested entry's UpdatedAt**

In `BotBuilder.Core/NestedBots/NestedBotEditorSession.cs` `SyncBack`, bump the child editor's UpdatedAt before mapping so an edited nested entry reflects the change:
```csharp
    public void SyncBack()
    {
        Editor.UpdatedAt = DateTime.UtcNow;
        _library.Replace(DocumentMapper.ToBot(Editor, includeLibrary: false));
    }
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~BotMetadataTests"`
Expected: PASS (5).

- [ ] **Step 5: Commit**

```bash
git add BotBuilder.Core/BotEditorViewModel.cs BotBuilder.Core/DocumentMapper.cs BotBuilder.Core/NestedBots/NestedBotLibrary.cs BotBuilder.Core/NestedBots/NestedBotEditorSession.cs BotBuilder.Core.Tests/BotMetadataTests.cs
git commit -m "Populate bot createdAt/updatedAt + track Description"
```

---

### Task 2: `BotPropertiesViewModel` + File ▸ Properties dialog

**Files:**
- Create: `BotBuilder.Core/BotPropertiesViewModel.cs`
- Create: `BotBuilder/PropertiesDialog.xaml` + `BotBuilder/PropertiesDialog.xaml.cs`
- Modify: `BotBuilder/MainWindow.xaml` (File menu)
- Modify: `BotBuilder/MainWindow.xaml.cs` (handler)
- Test: `BotBuilder.Core.Tests/BotPropertiesViewModelTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `BotBuilder.Core.Tests/BotPropertiesViewModelTests.cs`:

```csharp
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class BotPropertiesViewModelTests
{
    [Fact]
    public void Exposes_EditableNameAndDescription()
    {
        var vm = new BotPropertiesViewModel("Bot", "Desc", DateTime.UtcNow, DateTime.UtcNow);
        Assert.Equal("Bot", vm.Name);
        Assert.Equal("Desc", vm.Description);
        vm.Name = "Renamed";
        Assert.Equal("Renamed", vm.Name);
    }

    [Fact]
    public void FormatsTimestamps_AndShowsDashForDefault()
    {
        var when = new DateTime(2031, 5, 6, 7, 8, 0, DateTimeKind.Utc);
        var vm = new BotPropertiesViewModel("B", "", when, default);
        Assert.Contains("2031", vm.CreatedDisplay);
        Assert.Equal("—", vm.UpdatedDisplay); // em dash for an unset timestamp
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~BotPropertiesViewModelTests"`
Expected: FAIL — type does not exist.

- [ ] **Step 3a: Create the view-model**

Create `BotBuilder.Core/BotPropertiesViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotBuilder.Core;

/// <summary>Backs the File ▸ Properties dialog: editable Name/Description plus read-only Created/Updated
/// (formatted for display). The caller seeds it from the editor and applies Name/Description back on OK.</summary>
public partial class BotPropertiesViewModel : ObservableObject
{
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _description;

    public BotPropertiesViewModel(string name, string description, DateTime createdAt, DateTime updatedAt)
    {
        _name = name;
        _description = description;
        CreatedDisplay = Format(createdAt);
        UpdatedDisplay = Format(updatedAt);
    }

    public string CreatedDisplay { get; }
    public string UpdatedDisplay { get; }

    private static string Format(DateTime dt) =>
        dt == default ? "—" : dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}
```

- [ ] **Step 3b: Create the dialog (mirror an existing dialog's chrome/theming)**

FIRST read an existing simple modal — e.g. `BotBuilder/SelectorPickerDialog.xaml`/`.xaml.cs` — to match window chrome, theme brushes (`WindowBackgroundBrush` on the Window, `SecondaryTextBrush` for labels, themed buttons), `WindowStartupLocation`, and the OK/Cancel + `DialogResult` pattern.

Create `BotBuilder/PropertiesDialog.xaml` — a `Window` (Title "Bot Properties", e.g. 420x360, `Background="{DynamicResource WindowBackgroundBrush}"`, `WindowStartupLocation="CenterOwner"`) with a `StackPanel` (Margin 12):
- "Name" label (`SecondaryTextBrush`) + `TextBox Text="{Binding Name, UpdateSourceTrigger=PropertyChanged}"`.
- "Description" label + multiline `TextBox Text="{Binding Description, UpdateSourceTrigger=PropertyChanged}" AcceptsReturn="True" TextWrapping="Wrap" MinHeight="120" VerticalScrollBarVisibility="Auto"`.
- "Created" label + `TextBlock Text="{Binding CreatedDisplay}"`; "Updated" label + `TextBlock Text="{Binding UpdatedDisplay}"` (both read-only, `SecondaryTextBrush`).
- A right-aligned button row: `OK` (`Click="Ok_Click"`, IsDefault) and `Cancel` (`Click="Cancel_Click"`, IsCancel).

Create `BotBuilder/PropertiesDialog.xaml.cs`:
```csharp
using System.Windows;
using BotBuilder.Core;

namespace BotBuilder;

public partial class PropertiesDialog : Window
{
    public PropertiesDialog(BotPropertiesViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
```

- [ ] **Step 3c: File menu item + handler**

In `BotBuilder/MainWindow.xaml`, add a Properties item to the File menu (after Save As; a separator before it is nice). It is available in both root and child editors (it edits whichever bot the window holds):
```xml
                <Separator />
                <MenuItem Header="P_roperties…" Click="Properties_Click" />
```
In `BotBuilder/MainWindow.xaml.cs`, add:
```csharp
    private void Properties_Click(object sender, RoutedEventArgs e)
    {
        var vm = new BotBuilder.Core.BotPropertiesViewModel(
            _editor.BotName, _editor.BotDescription, _editor.CreatedAt, _editor.UpdatedAt);
        var dialog = new PropertiesDialog(vm) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _editor.BotName = vm.Name;
            _editor.BotDescription = vm.Description;
            _editor.MarkDirty();
            // Root title is bound to WindowTitle; child breadcrumb updates via its BotName subscription.
        }
    }
```

- [ ] **Step 4: Build + manual verification**

Run: `dotnet build ADB.slnx` — Build succeeded.

Launch `dotnet run --project BotBuilder`. File ▸ Properties opens the dialog: Name + Description editable, Created/Updated shown. Edit Name → OK → the title bar updates and the doc is dirty; Description persists on save (reopen Properties to confirm). Open a nested bot's child editor and use File ▸ Properties there → it edits that nested bot's Name/Description (the breadcrumb title updates). Verify Light/Dark/HighContrast.

- [ ] **Step 5: Commit**

```bash
git add BotBuilder.Core/BotPropertiesViewModel.cs BotBuilder/PropertiesDialog.xaml BotBuilder/PropertiesDialog.xaml.cs BotBuilder/MainWindow.xaml BotBuilder/MainWindow.xaml.cs BotBuilder.Core.Tests/BotPropertiesViewModelTests.cs
git commit -m "Add File > Properties dialog (Name/Description + read-only timestamps)"
```

---

### Task 3: Full suite green

- [ ] **Step 1: Run the whole suite**

Run: `dotnet test ADB.slnx`
Expected: PASS, no regressions.

---

## Self-Review

- **Coverage:** timestamps populated on New/Save (Task 1) — no more `0001-01-01`; Description tracked + round-tripped (Task 1); new library entries timestamped + nested edits bump UpdatedAt (Task 1); Properties dialog edits Name+Description with read-only Created/Updated, in root and child editors (Task 2). ✓
- **No regressions:** `ExportTo` deliberately not bumped (Test Run temp export); root title binding + child breadcrumb both update from `BotName` change. ✓
- **Placeholders:** none in Task 1; Task 2 dialog mirrors an existing dialog (read it first) with concrete VM + handlers. ✓
- **Type consistency:** `BotEditorViewModel.CreatedAt/UpdatedAt/BotDescription` and `BotPropertiesViewModel.Name/Description/CreatedDisplay/UpdatedDisplay` used identically across tasks. ✓
- **Note for executor:** read `SelectorPickerDialog`/`RegionPickerDialog` for the dialog chrome + theming pattern; the File ▸ Properties item is intentionally enabled in child mode too (unlike New/Open which B3c disabled).
