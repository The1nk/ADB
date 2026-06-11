# Nested Bots — Modeless Child Editor Slice (B3c) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Edit nested bots in-app. Double-clicking a Nested Bot card (or "New nested bot") opens a modeless editor window scoped to that library entry, reusing the full editor. One window per `nestedBotId` (re-open focuses the existing one). The child window has a breadcrumb title, New/Open disabled, and Save that persists the whole parent stack. Assigning a card that would create a reference cycle is blocked.

**Architecture:** Two layers. (1) Testable BotBuilder.Core logic: `DocumentMapper` gains an `includeLibrary` flag (a nested entry must NOT round-trip the shared flat library into its own `NestedBots`), `NestedBotLibrary.Replace`, a `NestedBotEditorSession` that builds a child `BotEditorViewModel` from a library entry and syncs it back, and a cycle guard in `PropertiesViewModel`. (2) WPF: `MainWindow` gains a child mode (injected VM, breadcrumb title, File-menu disable, save-parent callback), and a `NestedEditorManager` dedupes/owns child windows. Because the library is flat at the root, one manager keyed by `nestedBotId` serves every depth.

**Tech Stack:** .NET 10 WPF, BotBuilder.Core (testable VMs), CommunityToolkit.Mvvm, xUnit.

Reference spec: `Docs/superpowers/specs/2026-06-10-title-bar-and-nested-bots-design.md` (Feature B, section B5). Builds on B1/B2/B3a (merged) and B3b (this branch — parked PR #59). THEME RULE: any new control uses `DynamicResource` brushes; reuse existing templates.

Work in worktree `C:\git\ADB-nested-child` (branch `worktree-nested-bots-child`, stacked on `worktree-nested-bots-ui`). Build/test from the worktree root.

---

### Task 1: `DocumentMapper.includeLibrary` + `NestedBotLibrary.Replace`

**Files:**
- Modify: `BotBuilder.Core/DocumentMapper.cs`
- Modify: `BotBuilder.Core/NestedBots/NestedBotLibrary.cs`
- Test: `BotBuilder.Core.Tests/NestedBots/NestedBotEntryMappingTests.cs` (create)

A nested entry edited in a child editor must NOT write the shared root library into its own `Bot.NestedBots`, nor clear the shared library on load. `includeLibrary` (default true = root behavior) controls this.

- [ ] **Step 1: Write the failing test**

Create `BotBuilder.Core.Tests/NestedBots/NestedBotEntryMappingTests.cs`:

```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using BotBuilder.Core;
using BotBuilder.Core.NestedBots;
using Xunit;

namespace BotBuilder.Core.Tests.NestedBots;

public class NestedBotEntryMappingTests
{
    private static (BotEditorViewModel editor, ActionRegistry reg) NewEditor(NestedBotLibrary? lib = null)
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return (new BotEditorViewModel(defs, lib), defs);
    }

    [Fact]
    public void ToBot_WithoutLibrary_LeavesNestedBotsEmpty()
    {
        var lib = new NestedBotLibrary();
        var (editor, _) = NewEditor(lib);
        lib.AddNew("ShouldNotLeak"); // editor shares this library

        var bot = DocumentMapper.ToBot(editor, includeLibrary: false);

        Assert.Empty(bot.NestedBots); // the shared library was NOT written into this (nested) bot
    }

    [Fact]
    public void Populate_WithoutLibrary_DoesNotClearSharedLibrary()
    {
        var lib = new NestedBotLibrary();
        lib.AddNew("KeepMe");
        var (editor, reg) = NewEditor(lib);
        var entry = new Bot { Id = Guid.NewGuid(), Name = "Entry" }; // an entry with empty NestedBots

        DocumentMapper.Populate(editor, entry, reg, includeLibrary: false);

        Assert.Single(lib.Entries);           // shared library untouched
        Assert.Equal("Entry", editor.BotName); // graph/name still populated
        Assert.Equal(entry.Id, editor.BotId);
    }

    [Fact]
    public void Replace_SwapsEntryByIdPreservingPosition()
    {
        var lib = new NestedBotLibrary();
        var a = lib.AddNew("A");
        var b = lib.AddNew("B");
        var updated = new Bot { Id = b.Id, Name = "B-edited" };

        lib.Replace(updated);

        Assert.Equal(2, lib.Entries.Count);
        Assert.Same(updated, lib.Get(b.Id));
        Assert.Equal("B-edited", lib.Entries[1].Name); // position preserved (index 1)
        Assert.Same(a, lib.Entries[0]);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotEntryMappingTests"`
Expected: FAIL — overloads / `Replace` don't exist.

- [ ] **Step 3a: `includeLibrary` on `DocumentMapper`**

In `BotBuilder.Core/DocumentMapper.cs`:
- Change `public static Bot ToBot(BotEditorViewModel editor)` to `public static Bot ToBot(BotEditorViewModel editor, bool includeLibrary = true)`, and guard the library line added in B3a:
```csharp
        if (includeLibrary)
        {
            bot.NestedBots = editor.NestedBotLibrary.Entries.ToList();
        }
```
- Change `public static void Populate(BotEditorViewModel editor, Bot bot, ActionRegistry registry)` to add `, bool includeLibrary = true`, and guard the load line added in B3a:
```csharp
        if (includeLibrary)
        {
            editor.NestedBotLibrary.Load(bot.NestedBots);
        }
```
(Leave `editor.RefreshNestedBotSubtitles();` and `editor.RefreshTargetBadges();` running unconditionally.)

- [ ] **Step 3b: `Replace` on `NestedBotLibrary`**

In `BotBuilder.Core/NestedBots/NestedBotLibrary.cs`, add:
```csharp
    /// <summary>Replaces the entry whose id matches <paramref name="updated"/>, preserving its position. No-op
    /// if no entry has that id.</summary>
    public void Replace(Bot updated)
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Id == updated.Id)
            {
                _entries[i] = updated;
                return;
            }
        }
    }
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotEntryMappingTests"`
Expected: PASS (3).

- [ ] **Step 5: Commit**

```bash
git add BotBuilder.Core/DocumentMapper.cs BotBuilder.Core/NestedBots/NestedBotLibrary.cs BotBuilder.Core.Tests/NestedBots/NestedBotEntryMappingTests.cs
git commit -m "DocumentMapper includeLibrary flag + NestedBotLibrary.Replace"
```

---

### Task 2: `NestedBotEditorSession` + cycle guard + `NewNestedBot`

**Files:**
- Create: `BotBuilder.Core/NestedBots/NestedBotEditorSession.cs`
- Modify: `BotBuilder.Core/Properties/PropertiesViewModel.cs`
- Test: `BotBuilder.Core.Tests/NestedBots/NestedBotEditorSessionTests.cs` (create)
- Test: `BotBuilder.Core.Tests/Properties/NestedBotCycleGuardTests.cs` (create)

- [ ] **Step 1: Write the failing tests**

Create `BotBuilder.Core.Tests/NestedBots/NestedBotEditorSessionTests.cs`:

```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using BotBuilder.Core.NestedBots;
using Xunit;

namespace BotBuilder.Core.Tests.NestedBots;

public class NestedBotEditorSessionTests
{
    private static ActionRegistry Registry()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return defs;
    }

    [Fact]
    public void Open_BuildsChildEditorPopulatedFromEntry_SharingLibrary()
    {
        var reg = Registry();
        var lib = new NestedBotLibrary();
        var entry = lib.AddNew("Sub");

        var session = NestedBotEditorSession.Open(entry.Id, reg, lib);

        Assert.Equal(entry.Id, session.Editor.BotId);
        Assert.Equal("Sub", session.Editor.BotName);
        Assert.Same(lib, session.Editor.NestedBotLibrary); // shares the root library
    }

    [Fact]
    public void SyncBack_WritesChildGraphIntoLibraryEntry()
    {
        var reg = Registry();
        var lib = new NestedBotLibrary();
        var entry = lib.AddNew("Sub");
        var session = NestedBotEditorSession.Open(entry.Id, reg, lib);

        session.Editor.AddNode("control.start", 10, 10); // edit the child

        session.SyncBack();

        Assert.Single(lib.Get(entry.Id)!.Actions); // the edit landed in the library entry
        Assert.Empty(lib.Get(entry.Id)!.NestedBots); // entry didn't absorb the shared library
    }

    [Fact]
    public void Open_UnknownId_Throws()
    {
        var reg = Registry();
        var lib = new NestedBotLibrary();
        Assert.Throws<InvalidOperationException>(() => NestedBotEditorSession.Open(Guid.NewGuid(), reg, lib));
    }
}
```

Create `BotBuilder.Core.Tests/Properties/NestedBotCycleGuardTests.cs`:

```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using BotBuilder.Core;
using BotBuilder.Core.NestedBots;
using Xunit;

namespace BotBuilder.Core.Tests.Properties;

public class NestedBotCycleGuardTests
{
    private static BotEditorViewModel NestedEditorFor(NestedBotLibrary lib, Guid entryId, out ActionRegistry reg)
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        reg = defs;
        var editor = new BotEditorViewModel(defs, lib);
        DocumentMapper.Populate(editor, lib.Get(entryId)!, defs, includeLibrary: false);
        return editor;
    }

    [Fact]
    public void AssigningACyclicReference_IsBlocked()
    {
        // Library: A and B. B already references A. Editing A, try to point a card at B -> would cycle A->B->A.
        var lib = new NestedBotLibrary();
        var a = lib.AddNew("A");
        var b = lib.AddNew("B");
        b.Actions.Add(new BotAction { Id = Guid.NewGuid(), TypeKey = NestedBotAction.NestedBotTypeKey,
            Config = { [NestedBotAction.NestedBotIdKey] = a.Id.ToString() } });

        var editorA = NestedEditorFor(lib, a.Id, out _);
        var card = editorA.AddNode(NestedBotAction.NestedBotTypeKey, 0, 0);
        editorA.Select(card);

        editorA.Properties.SelectedNestedBotId = b.Id; // would create A->B->A

        Assert.Null(editorA.Properties.SelectedNestedBotId);          // assignment rejected
        Assert.False(card.Config.ContainsKey(NestedBotAction.NestedBotIdKey));
        Assert.NotNull(editorA.Properties.CycleWarning);              // warning surfaced
    }

    [Fact]
    public void AssigningANonCyclicReference_Succeeds_AndClearsWarning()
    {
        var lib = new NestedBotLibrary();
        var a = lib.AddNew("A");
        var b = lib.AddNew("B"); // B does NOT reference A

        var editorA = NestedEditorFor(lib, a.Id, out _);
        var card = editorA.AddNode(NestedBotAction.NestedBotTypeKey, 0, 0);
        editorA.Select(card);

        editorA.Properties.SelectedNestedBotId = b.Id;

        Assert.Equal(b.Id, editorA.Properties.SelectedNestedBotId);
        Assert.Null(editorA.Properties.CycleWarning);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotEditorSessionTests|FullyQualifiedName~NestedBotCycleGuardTests"`
Expected: FAIL — `NestedBotEditorSession` / `CycleWarning` / cycle guard don't exist.

- [ ] **Step 3a: Create `NestedBotEditorSession`**

Create `BotBuilder.Core/NestedBots/NestedBotEditorSession.cs`:

```csharp
using AdbCore.Actions;

namespace BotBuilder.Core.NestedBots;

/// <summary>A child editing session for one library entry: builds a <see cref="BotEditorViewModel"/> populated
/// from the entry (sharing the registry + root library) and syncs edits back into the entry. The WPF layer hosts
/// <see cref="Editor"/> in a child window and calls <see cref="SyncBack"/> on save/close.</summary>
public sealed class NestedBotEditorSession
{
    private readonly NestedBotLibrary _library;

    private NestedBotEditorSession(Guid nestedBotId, BotEditorViewModel editor, NestedBotLibrary library)
    {
        NestedBotId = nestedBotId;
        Editor = editor;
        _library = library;
    }

    public Guid NestedBotId { get; }
    public BotEditorViewModel Editor { get; }

    /// <summary>Builds a child editor for the library entry <paramref name="nestedBotId"/>.</summary>
    public static NestedBotEditorSession Open(Guid nestedBotId, ActionRegistry registry, NestedBotLibrary library)
    {
        var entry = library.Get(nestedBotId)
            ?? throw new InvalidOperationException($"Nested bot '{nestedBotId}' is not in the library.");
        var editor = new BotEditorViewModel(registry, library);
        DocumentMapper.Populate(editor, entry, registry, includeLibrary: false);
        editor.MarkSavedClean(); // a freshly-opened entry isn't an unsaved edit
        return new NestedBotEditorSession(nestedBotId, editor, library);
    }

    /// <summary>Writes the child editor's current graph back into its library entry (in place).</summary>
    public void SyncBack() => _library.Replace(DocumentMapper.ToBot(Editor, includeLibrary: false));
}
```

Add a small helper to `BotEditorViewModel` (used above so a freshly populated child isn't marked dirty): in `BotBuilder.Core/BotEditorViewModel.cs` add:
```csharp
    /// <summary>Clears the dirty flag (e.g. just after populating a child editor from a library entry).</summary>
    public void MarkSavedClean() => IsDirty = false;
```

- [ ] **Step 3b: Cycle guard + `CycleWarning` + `NewNestedBot` in `PropertiesViewModel`**

In `BotBuilder.Core/Properties/PropertiesViewModel.cs`:
- Add an observable field with the others at the top of the class: `[ObservableProperty] private string? _cycleWarning;`
- Replace the `SelectedNestedBotId` setter body so a cyclic assignment is rejected:
```csharp
        set
        {
            if (Node is null) { return; }
            if (value is Guid id)
            {
                if (_editor.NestedBotLibrary.WouldCreateCycle(_editor.BotId, id))
                {
                    CycleWarning = "That would make this bot run itself (a nested-bot cycle).";
                    OnPropertyChanged(nameof(SelectedNestedBotId)); // snap the picker back
                    return;
                }
                Node.Config[NestedBotAction.NestedBotIdKey] = id.ToString();
            }
            else
            {
                Node.Config.Remove(NestedBotAction.NestedBotIdKey);
            }
            CycleWarning = null;
            _editor.MarkDirty();
            _editor.RefreshNestedBotSubtitles();
            OnPropertyChanged(nameof(SelectedNestedBotName));
            OnPropertyChanged(nameof(SelectedNestedBotEditableName));
        }
```
- Add a `NewNestedBot` method near `ImportNestedBot`:
```csharp
    /// <summary>Creates a new empty library entry and assigns it to the selected card. Returns the entry so the
    /// caller can open a child editor for it.</summary>
    public Bot NewNestedBot()
    {
        var entry = _editor.NestedBotLibrary.AddNew();
        SelectedNestedBotId = entry.Id;
        OnPropertyChanged(nameof(NestedBotEntries));
        return entry;
    }
```
- In `Rebuild()`, also clear/raise the warning: add `CycleWarning = null;` near the top of `Rebuild()` and `OnPropertyChanged(nameof(CycleWarning));` is automatic from the observable property — no manual raise needed.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotEditorSessionTests|FullyQualifiedName~NestedBotCycleGuardTests"`
Expected: PASS (5).

- [ ] **Step 5: Commit**

```bash
git add BotBuilder.Core/NestedBots/NestedBotEditorSession.cs BotBuilder.Core/BotEditorViewModel.cs BotBuilder.Core/Properties/PropertiesViewModel.cs BotBuilder.Core.Tests/NestedBots/NestedBotEditorSessionTests.cs BotBuilder.Core.Tests/Properties/NestedBotCycleGuardTests.cs
git commit -m "NestedBotEditorSession + cycle-guarded assignment + NewNestedBot"
```

---

### Task 3: WPF — child-mode `MainWindow` + `NestedEditorManager` + wiring

**Files:**
- Modify: `BotBuilder/MainWindow.xaml.cs`
- Modify: `BotBuilder/MainWindow.xaml`
- Create: `BotBuilder/NestedEditorManager.cs`

READ the full `BotBuilder/MainWindow.xaml.cs` (constructor, `New_Click`/`Open_Click`/`Save_Click`/`SaveAs_Click`, the menu item markup, `Node_MouseLeftButtonDown`) and `App.xaml.cs` before editing. Implement to these REQUIREMENTS (adapt to the actual code; keep existing behavior for the root window):

**3a. `MainWindow` child mode.**
- Add fields: `private NestedEditorManager? _nestedEditors;` and child-mode state: `private bool _isChild;`, `private BotBuilder.Core.NestedBots.NestedBotEditorSession? _childSession;`, `private string? _rootName;`, `private Action? _saveParent;`, `private Action? _onChildClosed;`.
- Refactor the existing parameterless constructor so the registry + editor it builds are stored in fields the manager can reuse (the registry and the root `BotEditorViewModel` — its `NestedBotLibrary` is the shared one). Keep current root behavior identical. After building `_editor`, construct the manager: `_nestedEditors = new NestedEditorManager(registry, _editor, this, () => Save_Click(this, new RoutedEventArgs()));`
- Add a second constructor for child windows:
```csharp
public MainWindow(BotBuilder.Core.NestedBots.NestedBotEditorSession session, string rootName,
                  NestedEditorManager manager, Action saveParent, Action onClosed)
    : this(session.Editor, manager) // see shared init below
{
    _isChild = true;
    _childSession = session;
    _rootName = rootName;
    _saveParent = saveParent;
    _onClosed = onClosed;
    ApplyChildMode();
}
```
Introduce a private shared-init path so both constructors set `DataContext`, theme checks, and the manager without duplicating the registry build. Simplest: keep the parameterless ctor as the root (builds registry+editor+manager, calls a `private void Wire()`), and have the child ctor receive an already-built editor + the shared manager and call `Wire()`. The child does NOT build its own registry/manager — it reuses the root's (passed in).
- `ApplyChildMode()`:
  - Disable New/Open menu items and set explanatory tooltips: give those `MenuItem`s `x:Name` (`NewMenuItem`, `OpenMenuItem`) in XAML, then `NewMenuItem.IsEnabled = false; NewMenuItem.ToolTip = "Available in the main bot window — nested bots live inside the parent file."` (same for Open). Also disable/hide the Save As item (`SaveAsMenuItem`) in child mode (a nested entry has no independent file).
  - Replace the title binding with a breadcrumb: `BindingOperations.ClearBinding(this, TitleProperty);` then set the title via a helper `UpdateChildTitle()` and subscribe to the child editor's `PropertyChanged` for `IsDirty`/`BotName`:
    ```csharp
    void UpdateChildTitle() =>
        Title = $"ADB Bot Builder — {_rootName} ▸ {(_editor.IsDirty ? "*" : "")}{_editor.BotName}";
    ```
    (`▸` is ▸.)
  - On `Closed`: `_childSession!.SyncBack(); _onClosed?.Invoke();` — but note the manager's `OnChildClosed` already calls SyncBack; to avoid double-sync, have the window invoke ONLY `_onClosed` (which is the manager's `OnChildClosed`, which does SyncBack + dirty + untrack). So the window's Closed handler just calls `_onClosed?.Invoke();`.
- In `Save_Click`, branch for child mode FIRST: if `_isChild`, do `_childSession!.SyncBack(); _saveParent!.Invoke(); return;` (sync the entry, then run the root's save). The root behavior (prompt/in-place) is unchanged.
- In `New_Click` / `Open_Click`, no-op when `_isChild` (the menu items are disabled, but guard anyway).

**3b. `NestedEditorManager`.**
Create `BotBuilder/NestedEditorManager.cs`:
```csharp
using System.Collections.Generic;
using System.Windows;
using AdbCore.Actions;
using BotBuilder.Core;
using BotBuilder.Core.NestedBots;

namespace BotBuilder;

/// <summary>Owns the modeless child editor windows: one window per nested-bot id (re-open focuses the existing
/// one). Because the library is flat at the root, a single manager serves every nesting depth.</summary>
public sealed class NestedEditorManager
{
    private readonly Dictionary<Guid, MainWindow> _open = new();
    private readonly ActionRegistry _registry;
    private readonly BotEditorViewModel _rootEditor;
    private readonly Window _rootWindow;
    private readonly Action _saveRoot;

    public NestedEditorManager(ActionRegistry registry, BotEditorViewModel rootEditor, Window rootWindow, Action saveRoot)
    {
        _registry = registry;
        _rootEditor = rootEditor;
        _rootWindow = rootWindow;
        _saveRoot = saveRoot;
    }

    public void OpenOrFocus(Guid nestedBotId)
    {
        if (_open.TryGetValue(nestedBotId, out var existing))
        {
            existing.Activate();
            return;
        }

        var session = NestedBotEditorSession.Open(nestedBotId, _registry, _rootEditor.NestedBotLibrary);
        var child = new MainWindow(session, _rootEditor.BotName, this, _saveRoot, () => OnChildClosed(nestedBotId, session));
        _open[nestedBotId] = child;
        child.Show();
    }

    private void OnChildClosed(Guid id, NestedBotEditorSession session)
    {
        session.SyncBack();                 // persist edits into the in-memory library
        _rootEditor.MarkDirty();            // the parent document now has unsaved changes
        _rootEditor.RefreshNestedBotSubtitles();
        _open.Remove(id);
    }
}
```
(The child `MainWindow` receives `this` manager so its own double-click / New opens further nested editors through the same dedup map.)

**3c. Wiring: double-click + New button + cycle warning display.**
- Double-click a Nested Bot card opens its editor. In `MainWindow.xaml.cs`, add a `Node_MouseDoubleClick` handler (or extend the node mouse handling) that, for a node with `TypeKey == NestedBotAction.NestedBotTypeKey` and an assigned id, calls `_nestedEditors!.OpenOrFocus(id)`. Wire it on the node `Border` in the node `DataTemplate` (`MouseDoubleClick="Node_MouseDoubleClick"`). Resolve the id from `node.Config[nestedBotId]`; if unassigned, do nothing (or focus the picker).
- Add a **"New"** button to the nested-bot properties section (next to Import/Remove) with `Click="NewNestedBot_Click"`:
```csharp
private void NewNestedBot_Click(object sender, RoutedEventArgs e)
{
    var entry = _editor.Properties.NewNestedBot();
    _nestedEditors!.OpenOrFocus(entry.Id);
}
```
- Show the cycle warning: add a `TextBlock` in the nested-bot properties section bound to `CycleWarning` (visible when non-null), using `DynamicResource ErrorBrush` (or the project's error brush — check `AdbUi.Theme` brushes; if none named ErrorBrush, use the same brush other error text uses):
```xml
<TextBlock Text="{Binding CycleWarning}" FontSize="11" TextWrapping="Wrap" Margin="0,2,0,0"
           Foreground="{DynamicResource ErrorBrush}"
           Visibility="{Binding CycleWarning, Converter={x:Static local:NullToCollapsedConverter.Instance}}" />
```

**3d. Build + manual verification.**
- `dotnet build ADB.slnx` — Build succeeded.
- Launch, drop a Nested Bot card → "New" opens a child window titled `ADB Bot Builder — <Root> ▸ Untitled Bot`; New/Open are greyed (tooltip), Save in the child saves the whole document. Build a small graph in the child, close it → the parent is dirty and the card subtitle reflects the entry. Re-open via double-click → same window focuses (no duplicate). In a child editor, try to point a card back at an ancestor → blocked with the cycle warning. Verify Light/Dark/HC.

- [ ] **Step (commit after each logical part, but at minimum):**

```bash
git add BotBuilder/MainWindow.xaml BotBuilder/MainWindow.xaml.cs BotBuilder/NestedEditorManager.cs
git commit -m "Modeless child editor: child-mode MainWindow + NestedEditorManager + wiring"
```

---

### Task 4: Full suite green

- [ ] **Step 1: Run the whole suite**

Run: `dotnet test ADB.slnx`
Expected: PASS, no regressions.

---

## Self-Review

- **Spec coverage (B5):** modeless child window reusing the editor with injected VM + shared library (Tasks 2/3); one window per id with re-open focus (Task 3 manager); breadcrumb title (Task 3); New/Open disabled + Save-saves-parent-stack (Task 3); double-click opens, New-empty opens (Task 3); edit-time cycle guard on assign (Task 2). ✓
- **Library integrity:** the `includeLibrary:false` flag prevents a nested entry from absorbing or clearing the shared flat library (Task 1) — the most important correctness invariant; explicitly tested. ✓
- **Recursion:** the single manager + flat library means a grandchild nested editor (a nested bot containing a nested card) opens through the same dedup map — the child window receives the same manager. ✓
- **Placeholders:** none in the testable tasks. Task 3 is requirement-driven WPF (read MainWindow first) with concrete code for the manager, child-mode branch, title, and handlers.
- **Notes for executor:** give the New/Open/Save-As menu items `x:Name`s; avoid double-SyncBack (window Closed delegates to the manager's `OnChildClosed`, which is the single SyncBack point); confirm an `ErrorBrush`-equivalent exists in `AdbUi.Theme` (use the project's actual error brush key).
