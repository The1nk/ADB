# Nested Bot Library Manager Implementation Plan — Slice 5 of 5

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.
>
> **WORKTREE PATHS:** this runs in the worktree `C:\git\ADB\.claude\worktrees\library-manager`. For ALL Write/Edit ops use that ABSOLUTE prefix — the Write tool's default can otherwise land in the main checkout `C:\git\ADB`. After writing, `git -C C:/git/ADB/.claude/worktrees/library-manager status` to confirm.

**Goal:** Let the user see and remove nested-bot library entries — including orphans stranded after their cards were deleted — via a "Manage Nested Bot Library…" dialog with per-entry usage counts and a "Remove unused" action.

**Architecture:** A pure `NestedBotUsage` helper computes per-entry usage counts and the set of entries transitively reachable from the top-level graph's Nested Bot cards. A testable `NestedBotLibraryManagerViewModel` wraps the editor's `NestedBotLibrary` + top-level nodes, exposing rows (name · #actions · usage) and `Remove` / `RemoveUnused` commands (marking the doc dirty, refreshing card subtitles). A WPF `NestedBotLibraryDialog` (a ListView + buttons, following `SettingsDialog`) presents it, opened from an Edit-menu item. Removing a *referenced* entry warns first; "Remove unused" purges every entry not reachable from the top level.

**Tech Stack:** C# / .NET 10, WPF, xUnit. Independent of the frame-store/parallel slices (branches from `main`).

**Design doc:** `Docs/superpowers/specs/2026-07-14-fast-frame-reads-and-library-manager-design.md`

**Branch:** `worktree-library-manager` from `origin/main`. Tasks 1–2 are testable core; Task 3 is the WPF dialog (visual — the user validates before merge); Task 4 is docs.

**Key existing types:** `BotEditorViewModel` (`Nodes` : ObservableCollection<NodeViewModel>; `NestedBotLibrary`; `MarkDirty()`; `RefreshNestedBotSubtitles()`), `NestedBotLibrary` (`Entries`, `Get(id)`, `Remove(id)`, private `ReferencedIds(Bot)`), `NestedBotAction.NestedBotTypeKey` / `NestedBotAction.NestedBotIdKey`, `NodeViewModel` (`TypeKey`, `Config`).

---

### Task 1: `NestedBotUsage` helper (usage counts + reachability)

**Files:**
- Modify: `BotBuilder.Core/NestedBots/NestedBotLibrary.cs` (make `ReferencedIds` public static so the helper reuses it — one source of truth)
- Create: `BotBuilder.Core/NestedBots/NestedBotUsage.cs`
- Test: `BotBuilder.Core.Tests/NestedBots/NestedBotUsageTests.cs`

- [ ] **Step 1: Write the failing tests** — Create `BotBuilder.Core.Tests/NestedBots/NestedBotUsageTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using AdbCore.Actions.BuiltIn;
using AdbCore.Models;
using BotBuilder.Core.NestedBots;
using Xunit;

namespace BotBuilder.Core.Tests.NestedBots;

public class NestedBotUsageTests
{
    // Builds a library bot with the given id whose actions reference each of referTo via a Nested Bot card.
    private static Bot Entry(Guid id, string name, params Guid[] referTo)
    {
        var bot = new Bot { Id = id, Name = name };
        foreach (var r in referTo)
        {
            bot.Actions.Add(new BotAction
            {
                Id = Guid.NewGuid(),
                TypeKey = NestedBotAction.NestedBotTypeKey,
                Config = { [NestedBotAction.NestedBotIdKey] = r.ToString() },
            });
        }
        return bot;
    }

    private static NestedBotLibrary LibraryOf(params Bot[] entries)
    {
        var lib = new NestedBotLibrary();
        lib.Load(entries);
        return lib;
    }

    [Fact]
    public void UsageCount_CountsTopLevelAndNestedReferences()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var lib = LibraryOf(Entry(a, "A", b), Entry(b, "B"));   // A references B
        var topLevel = new List<Guid> { a, a };                  // two top-level cards reference A

        Assert.Equal(2, NestedBotUsage.UsageCount(lib, topLevel, a)); // 2 top-level, 0 nested
        Assert.Equal(1, NestedBotUsage.UsageCount(lib, topLevel, b)); // 0 top-level, 1 nested (from A)
    }

    [Fact]
    public void ReachableFromTopLevel_FollowsTransitiveReferences()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var lib = LibraryOf(Entry(a, "A", b), Entry(b, "B", c), Entry(c, "C"));

        var reachable = NestedBotUsage.ReachableFromTopLevel(lib, new[] { a });

        Assert.Equal(new HashSet<Guid> { a, b, c }, reachable);
    }

    [Fact]
    public void UnusedEntries_ReturnsThoseNotReachableFromTopLevel()
    {
        // top level references nothing; every entry is unused, even ones referenced by other (orphan) entries.
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var lib = LibraryOf(Entry(a, "A", b), Entry(b, "B")); // A->B, but neither reachable from top level

        var unused = NestedBotUsage.UnusedEntries(lib, Array.Empty<Guid>());

        Assert.Equal(new HashSet<Guid> { a, b }, unused.ToHashSet());
    }

    [Fact]
    public void UnusedEntries_KeepsReachableChainButDropsIslands()
    {
        var used = Guid.NewGuid();
        var usedChild = Guid.NewGuid();
        var island = Guid.NewGuid();
        var islandChild = Guid.NewGuid();
        var lib = LibraryOf(Entry(used, "Used", usedChild), Entry(usedChild, "UsedChild"),
                            Entry(island, "Island", islandChild), Entry(islandChild, "IslandChild"));

        var unused = NestedBotUsage.UnusedEntries(lib, new[] { used }).ToHashSet();

        Assert.Equal(new HashSet<Guid> { island, islandChild }, unused); // island + its child dropped; used chain kept
    }
}
```

- [ ] **Step 2: Run tests to verify they fail** — `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotUsageTests"` — expect FAIL (type missing).

- [ ] **Step 3: Implement.** First, in `BotBuilder.Core/NestedBots/NestedBotLibrary.cs`, change the existing `private static IEnumerable<Guid> ReferencedIds(Bot bot)` to `public static` (only the accessibility changes; keep the body identical). It is currently used internally by `WouldCreateCycle`/`ReachableFrom`/`Import` — those keep working.

Then create `BotBuilder.Core/NestedBots/NestedBotUsage.cs`:

```csharp
using AdbCore.Models;

namespace BotBuilder.Core.NestedBots;

/// <summary>Computes how nested-bot library entries are used: per-entry reference counts, and the set of
/// entries transitively reachable from the top-level graph's Nested Bot cards (everything else is an orphan
/// that can be purged).</summary>
public static class NestedBotUsage
{
    /// <summary>Total Nested Bot cards referencing <paramref name="entryId"/> — across the top-level graph
    /// (<paramref name="topLevelRefs"/>) and every library entry's own cards.</summary>
    public static int UsageCount(NestedBotLibrary library, IReadOnlyList<Guid> topLevelRefs, Guid entryId)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(topLevelRefs);

        var count = topLevelRefs.Count(r => r == entryId);
        foreach (var entry in library.Entries)
        {
            count += NestedBotLibrary.ReferencedIds(entry).Count(r => r == entryId);
        }
        return count;
    }

    /// <summary>The set of entry ids transitively reachable from the top-level roots through library
    /// references. (Ids in <paramref name="topLevelRefs"/> that don't match an entry are simply skipped.)</summary>
    public static HashSet<Guid> ReachableFromTopLevel(NestedBotLibrary library, IEnumerable<Guid> topLevelRefs)
    {
        ArgumentNullException.ThrowIfNull(library);

        var reachable = new HashSet<Guid>();
        var stack = new Stack<Guid>(topLevelRefs);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!reachable.Add(current)) { continue; }
            if (library.Get(current) is { } bot)
            {
                foreach (var referenced in NestedBotLibrary.ReferencedIds(bot)) { stack.Push(referenced); }
            }
        }
        return reachable;
    }

    /// <summary>Entry ids NOT reachable from the top-level graph — orphans safe to purge. Reachability, not raw
    /// usage count: an entry referenced only by another orphan is still unused.</summary>
    public static IReadOnlyList<Guid> UnusedEntries(NestedBotLibrary library, IEnumerable<Guid> topLevelRefs)
    {
        ArgumentNullException.ThrowIfNull(library);

        var reachable = ReachableFromTopLevel(library, topLevelRefs);
        return library.Entries.Where(e => !reachable.Contains(e.Id)).Select(e => e.Id).ToList();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass** — `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotUsageTests"` → PASS (4). Then `dotnet build ADB.slnx`.

- [ ] **Step 5: Commit**

```bash
git add BotBuilder.Core/NestedBots/NestedBotLibrary.cs BotBuilder.Core/NestedBots/NestedBotUsage.cs BotBuilder.Core.Tests/NestedBots/NestedBotUsageTests.cs
git commit -m "feat: NestedBotUsage (per-entry usage counts + top-level reachability)"
```

---

### Task 2: `NestedBotLibraryManagerViewModel`

**Files:**
- Create: `BotBuilder.Core/NestedBots/NestedBotLibraryManagerViewModel.cs`
- Test: `BotBuilder.Core.Tests/NestedBots/NestedBotLibraryManagerViewModelTests.cs`

- [ ] **Step 1: Write the failing tests** — Create `BotBuilder.Core.Tests/NestedBots/NestedBotLibraryManagerViewModelTests.cs`:

```csharp
using System;
using System.Linq;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using BotBuilder.Core.NestedBots;
using Xunit;

namespace BotBuilder.Core.Tests.NestedBots;

public class NestedBotLibraryManagerViewModelTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    // Adds a top-level Nested Bot card wired to reference entryId.
    private static void AddTopLevelCard(BotEditorViewModel editor, Guid entryId)
    {
        var node = editor.AddNode(NestedBotAction.NestedBotTypeKey, 0, 0);
        node.Config[NestedBotAction.NestedBotIdKey] = entryId.ToString();
    }

    [Fact]
    public void Rows_ReportNameActionCountAndUsage()
    {
        var editor = NewEditor();
        var used = editor.NestedBotLibrary.AddNew("Used");
        var orphan = editor.NestedBotLibrary.AddNew("Orphan");
        AddTopLevelCard(editor, used.Id);

        var vm = new NestedBotLibraryManagerViewModel(editor);

        var usedRow = vm.Entries.Single(r => r.Id == used.Id);
        var orphanRow = vm.Entries.Single(r => r.Id == orphan.Id);
        Assert.Equal("Used", usedRow.Name);
        Assert.Equal(1, usedRow.UsageCount);
        Assert.Equal(0, orphanRow.UsageCount);
    }

    [Fact]
    public void Remove_DropsEntry_MarksDirty_Refreshes()
    {
        var editor = NewEditor();
        var orphan = editor.NestedBotLibrary.AddNew("Orphan");
        editor.MarkSavedClean();
        var vm = new NestedBotLibraryManagerViewModel(editor);

        var removed = vm.Remove(orphan.Id);

        Assert.True(removed);
        Assert.Empty(editor.NestedBotLibrary.Entries);
        Assert.DoesNotContain(vm.Entries, r => r.Id == orphan.Id);
        Assert.True(editor.IsDirty);
    }

    [Fact]
    public void RemoveUnused_PurgesOnlyUnreachableEntries()
    {
        var editor = NewEditor();
        var used = editor.NestedBotLibrary.AddNew("Used");
        editor.NestedBotLibrary.AddNew("OrphanA");
        editor.NestedBotLibrary.AddNew("OrphanB");
        AddTopLevelCard(editor, used.Id);
        editor.MarkSavedClean();
        var vm = new NestedBotLibraryManagerViewModel(editor);

        var count = vm.RemoveUnused();

        Assert.Equal(2, count);
        Assert.Equal(new[] { used.Id }, editor.NestedBotLibrary.Entries.Select(e => e.Id).ToArray());
        Assert.True(editor.IsDirty);
    }

    [Fact]
    public void RemoveUnused_NothingUnused_NoDirty()
    {
        var editor = NewEditor();
        var used = editor.NestedBotLibrary.AddNew("Used");
        AddTopLevelCard(editor, used.Id);
        editor.MarkSavedClean();
        var vm = new NestedBotLibraryManagerViewModel(editor);

        var count = vm.RemoveUnused();

        Assert.Equal(0, count);
        Assert.False(editor.IsDirty);
    }
}
```

Note: verify `BuiltInActions.CreateRegistry()` is the real factory the other BotBuilder.Core tests use to build a `BotEditorViewModel` (grep the existing tests, e.g. `NestedBotPropertiesTests`, for how they construct an editor — match it exactly; if they use a different helper, use that). Verify `editor.IsDirty` is a settable/readable bool and `editor.AddNode(typeKey, x, y)` returns a `NodeViewModel` with a mutable `Config` — adapt if the real signatures differ.

- [ ] **Step 2: Run tests to verify they fail** — `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotLibraryManagerViewModelTests"` — expect FAIL.

- [ ] **Step 3: Implement** — Create `BotBuilder.Core/NestedBots/NestedBotLibraryManagerViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using AdbCore.Actions.BuiltIn;

namespace BotBuilder.Core.NestedBots;

/// <summary>A single row in the Nested Bot Library manager: an entry's id, name, action count, and how many
/// Nested Bot cards reference it (top-level + nested).</summary>
public sealed record NestedBotEntryRow(Guid Id, string Name, int ActionCount, int UsageCount);

/// <summary>View-model for the "Manage Nested Bot Library" dialog: lists library entries with usage counts and
/// removes entries (one, or all orphans unreachable from the top-level graph), keeping the editor in sync.</summary>
public sealed class NestedBotLibraryManagerViewModel
{
    private readonly BotEditorViewModel _editor;

    public ObservableCollection<NestedBotEntryRow> Entries { get; } = new();

    public NestedBotLibraryManagerViewModel(BotEditorViewModel editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        _editor = editor;
        Refresh();
    }

    /// <summary>Rebuilds the rows from the current library + top-level references.</summary>
    public void Refresh()
    {
        var topLevel = TopLevelRefs();
        Entries.Clear();
        foreach (var entry in _editor.NestedBotLibrary.Entries)
        {
            Entries.Add(new NestedBotEntryRow(
                entry.Id, entry.Name, entry.Actions.Count,
                NestedBotUsage.UsageCount(_editor.NestedBotLibrary, topLevel, entry.Id)));
        }
    }

    /// <summary>Removes one entry; marks the document dirty and refreshes card subtitles + rows. Returns false
    /// when the entry wasn't in the library.</summary>
    public bool Remove(Guid id)
    {
        if (!_editor.NestedBotLibrary.Remove(id)) { return false; }
        AfterMutation();
        return true;
    }

    /// <summary>Removes every entry not reachable from the top-level graph. Returns how many were removed.</summary>
    public int RemoveUnused()
    {
        var unused = NestedBotUsage.UnusedEntries(_editor.NestedBotLibrary, TopLevelRefs());
        foreach (var id in unused) { _editor.NestedBotLibrary.Remove(id); }
        if (unused.Count > 0) { AfterMutation(); }
        return unused.Count;
    }

    private void AfterMutation()
    {
        _editor.MarkDirty();
        _editor.RefreshNestedBotSubtitles();
        Refresh();
    }

    private IReadOnlyList<Guid> TopLevelRefs()
    {
        var refs = new List<Guid>();
        foreach (var node in _editor.Nodes)
        {
            if (node.TypeKey == NestedBotAction.NestedBotTypeKey
                && node.Config.TryGetValue(NestedBotAction.NestedBotIdKey, out var raw)
                && Guid.TryParse(raw?.ToString(), out var id))
            {
                refs.Add(id);
            }
        }
        return refs;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass** — `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotLibraryManagerViewModelTests"` → PASS (4). Then `dotnet build ADB.slnx`.

- [ ] **Step 5: Commit**

```bash
git add BotBuilder.Core/NestedBots/NestedBotLibraryManagerViewModel.cs BotBuilder.Core.Tests/NestedBots/NestedBotLibraryManagerViewModelTests.cs
git commit -m "feat: NestedBotLibraryManagerViewModel (list + remove + remove-unused)"
```

---

### Task 3: `NestedBotLibraryDialog` (WPF) + Edit-menu entry  [VISUAL — user validates]

**Files:**
- Create: `BotBuilder/NestedBotLibraryDialog.xaml`
- Create: `BotBuilder/NestedBotLibraryDialog.xaml.cs`
- Modify: `BotBuilder/MainWindow.xaml` (add an Edit-menu item)
- Modify: `BotBuilder/MainWindow.xaml.cs` (open the dialog)

- [ ] **Step 1: Create the dialog XAML** — Create `BotBuilder/NestedBotLibraryDialog.xaml` (mirrors `SettingsDialog` conventions: `DynamicResource` theme brushes, CenterOwner, AutomationProperties). A `ListView`/`GridView` of entries + Remove / Remove Unused / Close buttons:

```xml
<Window x:Class="BotBuilder.NestedBotLibraryDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Manage Nested Bot Library" Height="380" Width="560"
        WindowStartupLocation="CenterOwner"
        Background="{DynamicResource WindowBackgroundBrush}">
    <DockPanel Margin="12">
        <TextBlock DockPanel.Dock="Top" TextWrapping="Wrap" Margin="0,0,0,8"
                   Foreground="{DynamicResource SecondaryTextBrush}" FontSize="11"
                   Text="Reusable nested bots stored in this file. 'Usage' counts the Nested Bot cards referencing each entry. Removing an entry that's still in use leaves those cards showing '(missing bot)'. 'Remove Unused' drops every entry not reachable from the top-level graph." />
        <StackPanel DockPanel.Dock="Bottom" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,10,0,0">
            <Button Content="Remove Unused" Width="120" Click="RemoveUnused_Click"
                    AutomationProperties.Name="Remove unused nested bots" />
            <Button Content="Remove" Width="90" Margin="8,0,0,0" Click="Remove_Click"
                    AutomationProperties.Name="Remove selected nested bot" />
            <Button Content="Close" Width="90" Margin="8,0,0,0" Click="Close_Click" IsCancel="True"
                    AutomationProperties.Name="Close" />
        </StackPanel>
        <ListView x:Name="EntriesList" ItemsSource="{Binding Entries}"
                  Background="{DynamicResource WindowBackgroundBrush}"
                  AutomationProperties.Name="Nested bot library entries">
            <ListView.View>
                <GridView>
                    <GridViewColumn Header="Name" Width="260" DisplayMemberBinding="{Binding Name}" />
                    <GridViewColumn Header="Actions" Width="90" DisplayMemberBinding="{Binding ActionCount}" />
                    <GridViewColumn Header="Usage" Width="90" DisplayMemberBinding="{Binding UsageCount}" />
                </GridView>
            </ListView.View>
        </ListView>
    </DockPanel>
</Window>
```

- [ ] **Step 2: Create the code-behind** — Create `BotBuilder/NestedBotLibraryDialog.xaml.cs`:

```csharp
using System.Windows;
using BotBuilder.Core.NestedBots;

namespace BotBuilder;

public partial class NestedBotLibraryDialog : Window
{
    private readonly NestedBotLibraryManagerViewModel _vm;

    public NestedBotLibraryDialog(NestedBotLibraryManagerViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (EntriesList.SelectedItem is not NestedBotEntryRow row) { return; }
        if (row.UsageCount > 0)
        {
            var confirm = MessageBox.Show(this,
                $"'{row.Name}' is still referenced by {row.UsageCount} card(s). Remove it anyway? Those cards will show '(missing bot)'.",
                "Remove nested bot", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) { return; }
        }
        _vm.Remove(row.Id);
    }

    private void RemoveUnused_Click(object sender, RoutedEventArgs e)
    {
        var unused = _vm.Entries.Count(r => r.UsageCount == 0); // display hint only; VM computes reachability authoritatively
        var confirm = MessageBox.Show(this,
            "Remove every nested bot not reachable from the top-level graph? This cannot be undone (but isn't saved until you save the file).",
            "Remove unused nested bots", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) { return; }
        var removed = _vm.RemoveUnused();
        MessageBox.Show(this, removed == 0 ? "No unused nested bots to remove." : $"Removed {removed} unused nested bot(s).",
            "Remove unused nested bots", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
```

Add `using System.Linq;` if the analyzer flags the `Count(...)` call.

- [ ] **Step 3: Add the Edit-menu item.** In `BotBuilder/MainWindow.xaml`, inside the `<MenuItem Header="_Edit">` block (after the `_Tidy Up` item), add:

```xml
                <Separator />
                <MenuItem Header="Manage Nested Bot _Library…" Click="ManageNestedBotLibrary_Click" />
```

- [ ] **Step 4: Wire the click handler.** In `BotBuilder/MainWindow.xaml.cs`, add a handler (place it near the other menu handlers, e.g. after `Settings_Click`). Use the editor VM the window already holds — grep the file for how the editor VM field is named (likely `_editor` or `_viewModel`; match the actual field). Example (adapt the field name):

```csharp
    private void ManageNestedBotLibrary_Click(object sender, RoutedEventArgs e)
    {
        var vm = new BotBuilder.Core.NestedBots.NestedBotLibraryManagerViewModel(_editor);
        var dialog = new NestedBotLibraryDialog(vm) { Owner = this };
        dialog.ShowDialog();
    }
```

- [ ] **Step 5: Build + smoke-test compile.** `dotnet build ADB.slnx` — expect clean (0 warnings/errors). Then `dotnet test ADB.slnx` — all pass (no test regressions; the dialog itself has no unit test — it's validated visually by the user).

- [ ] **Step 6: Commit**

```bash
git add BotBuilder/NestedBotLibraryDialog.xaml BotBuilder/NestedBotLibraryDialog.xaml.cs BotBuilder/MainWindow.xaml BotBuilder/MainWindow.xaml.cs
git commit -m "feat: Manage Nested Bot Library dialog (Edit menu)"
```

---

### Task 4: Docs

**Files:**
- Modify: `CLAUDE.md`
- Modify: `README.md` (only if nested bots are mentioned in a user-facing feature list)
- Wiki: `C:/git/ADB.wiki` — update the nested-bots page (write only; do NOT commit/push)

- [ ] **Step 1: Full build + test.** `dotnet build ADB.slnx` (clean) and `dotnet test ADB.slnx` (all pass).

- [ ] **Step 2: CLAUDE.md.** Find the nested-bots prose (search "nestedBots" / "Nested Bot card" — near the `.bot` File Format Notes). Add a sentence documenting the manager: an Edit-menu **"Manage Nested Bot Library…"** dialog lists library entries with **usage counts** and removes entries — one at a time (warning when still referenced) or **Remove Unused** (every entry not transitively reachable from the top-level graph's Nested Bot cards). Note this is how orphaned entries — stranded when their cards are deleted — get purged (they persist otherwise, since the library is independent of the cards). Ground it in `NestedBotUsage` / `NestedBotLibraryManagerViewModel`.

- [ ] **Step 3: README.md.** If nested bots appear in a user-facing feature list, add a short mention of managing/pruning the library in the goblin voice. If not mentioned, make no change and say so in the report.

- [ ] **Step 4: Wiki (write only, no commit/push).** Grep `C:/git/ADB.wiki` for a nested-bots page; update it (or add a "Managing the library" section) describing the dialog, the usage column, per-entry Remove (warns if referenced), and Remove Unused (transitive-reachability). Do NOT run git in `C:/git/ADB.wiki`.

- [ ] **Step 5: Commit the worktree docs**

```bash
git add CLAUDE.md README.md
git commit -m "docs: Nested Bot Library manager"
```

---

## Self-Review

**Spec coverage (Slice 5):** usage counts + transitive-reachability purge → Task 1 (pure, tested incl. the "orphan-referenced-by-orphan is still unused" case). Manager VM (list, Remove, Remove Unused, dirty/subtitle sync) → Task 2. Dialog + Edit-menu entry + referenced-entry warning → Task 3 (visual). Docs → Task 4. Validation against a copy of the real `.bot` (all orphans identified) is a coordinator step, not a committed test (keeps tests self-contained; honors "don't touch working .bot files").

**Placeholder scan:** none — full code in every code step; the two spots that depend on exact existing API (`BuiltInActions.CreateRegistry()`/editor construction in tests; the MainWindow editor-field name) are called out with "grep and match the real name."

**Type consistency:** `NestedBotUsage.UsageCount/ReachableFromTopLevel/UnusedEntries`, `NestedBotLibrary.ReferencedIds` (now public static), `NestedBotEntryRow(Id, Name, ActionCount, UsageCount)`, `NestedBotLibraryManagerViewModel(BotEditorViewModel)` with `Entries`/`Refresh`/`Remove`/`RemoveUnused` — consistent across tasks, tests, and the dialog binding.

## Execution Handoff
Tasks 1–2 backend/VM (testable). Task 3 is WPF (the user validates the dialog visually before merge). Independent of the frame-store/parallel slices → PR to `main`.
