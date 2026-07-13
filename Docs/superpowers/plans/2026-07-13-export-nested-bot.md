# Export a Nested Bot to a Standalone `.bot` — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user export a nested bot from the library into its own self-contained, runnable `.bot` file, via a Properties-panel button and a child-editor menu item.

**Architecture:** A pure, non-mutating `NestedBotLibrary.Export(Guid)` (the inverse of the existing `Import`) builds a standalone `Bot`: the entry's own graph as the top-level bot, every transitively-referenced nested bot hoisted into `NestedBots`, ids preserved. `PropertiesViewModel` exposes it for the selected card; two thin WPF handlers own the `SaveFileDialog` + `BotSerializer.Save`.

**Tech Stack:** C# / .NET 10, WPF (BotBuilder), xUnit. `System.Text.Json` serialization via `BotSerializer`.

Spec: `Docs/superpowers/specs/2026-07-13-export-nested-bot-design.md`

---

## File Structure

- **Modify** `BotBuilder.Core/NestedBots/NestedBotLibrary.cs` — add `Export` + a private `ReachableFrom` closure helper (reuses existing `ReferencedIds` and `CloneBot`).
- **Create** `BotBuilder.Core.Tests/NestedBots/NestedBotExportTests.cs` — unit tests for `Export` incl. Import↔Export round-trip.
- **Modify** `BotBuilder.Core/Properties/PropertiesViewModel.cs` — add `ExportSelectedNestedBot()`.
- **Modify** `BotBuilder.Core.Tests/Properties/NestedBotPropertiesTests.cs` — add a VM-level export test.
- **Modify** `BotBuilder/MainWindow.xaml` — Properties-panel "Export .bot…" button; child File-menu "Export standalone .bot…" item.
- **Modify** `BotBuilder/MainWindow.xaml.cs` — two click handlers + a shared `SaveExportedBot` helper; make the child menu item visible in `ApplyChildMode`.
- **Modify** `CLAUDE.md`, `README.md`, `../ADB.wiki` — document the export path.

---

## Task 1: `NestedBotLibrary.Export` core routine

**Files:**
- Modify: `BotBuilder.Core/NestedBots/NestedBotLibrary.cs`
- Test: `BotBuilder.Core.Tests/NestedBots/NestedBotExportTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `BotBuilder.Core.Tests/NestedBots/NestedBotExportTests.cs`:

```csharp
using AdbCore.Actions.BuiltIn;
using AdbCore.Models;
using BotBuilder.Core.NestedBots;
using Xunit;

namespace BotBuilder.Core.Tests.NestedBots;

public class NestedBotExportTests
{
    // Builds a library entry that references another entry by a Nested Bot card.
    private static (NestedBotLibrary lib, Bot outer, Bot inner, Bot unrelated) BuildLibrary()
    {
        var lib = new NestedBotLibrary();
        var outer = lib.AddNew("Outer");
        var inner = lib.AddNew("Inner");
        var unrelated = lib.AddNew("Unrelated");

        outer.Actions.Add(new BotAction
        {
            Id = Guid.NewGuid(),
            TypeKey = NestedBotAction.NestedBotTypeKey,
            Config = { [NestedBotAction.NestedBotIdKey] = inner.Id.ToString() },
        });
        return (lib, outer, inner, unrelated);
    }

    [Fact]
    public void Export_BundlesTransitivelyReferencedNestedBots()
    {
        var (lib, outer, inner, _) = BuildLibrary();

        var exported = lib.Export(outer.Id);

        Assert.Equal("Outer", exported.Name);
        Assert.Single(exported.NestedBots);
        Assert.Equal(inner.Id, exported.NestedBots[0].Id);
    }

    [Fact]
    public void Export_ExcludesUnreferencedEntries()
    {
        var (lib, outer, _, unrelated) = BuildLibrary();

        var exported = lib.Export(outer.Id);

        Assert.DoesNotContain(exported.NestedBots, b => b.Id == unrelated.Id);
    }

    [Fact]
    public void Export_PreservesIdsAndReferences()
    {
        var (lib, outer, inner, _) = BuildLibrary();

        var exported = lib.Export(outer.Id);

        Assert.Equal(outer.Id, exported.Id);
        var card = exported.Actions.Single(a => a.TypeKey == NestedBotAction.NestedBotTypeKey);
        Assert.Equal(inner.Id.ToString(), card.Config[NestedBotAction.NestedBotIdKey]);
    }

    [Fact]
    public void Export_IsDeepCopy_DoesNotMutateLibrary()
    {
        var (lib, outer, _, _) = BuildLibrary();
        var countBefore = lib.Entries.Count;

        var exported = lib.Export(outer.Id);
        exported.Name = "Renamed after export";
        exported.NestedBots.Clear();

        Assert.Equal(countBefore, lib.Entries.Count);
        Assert.Equal("Outer", lib.Get(outer.Id)!.Name);   // source untouched
        Assert.NotSame(outer, exported);
    }

    [Fact]
    public void Export_UnknownId_Throws()
    {
        var lib = new NestedBotLibrary();
        Assert.Throws<InvalidOperationException>(() => lib.Export(Guid.NewGuid()));
    }

    [Fact]
    public void Import_OfExport_RoundTripsToEquivalentGraph()
    {
        var (lib, outer, _, _) = BuildLibrary();
        var exported = lib.Export(outer.Id);

        var target = new NestedBotLibrary();
        var reimported = target.Import(exported);

        // Two flat entries (Outer + Inner), fresh ids, card reference remapped and still resolvable.
        Assert.Equal(2, target.Entries.Count);
        Assert.Equal("Outer", reimported.Name);
        var card = reimported.Actions.Single(a => a.TypeKey == NestedBotAction.NestedBotTypeKey);
        var newInnerId = Guid.Parse(card.Config[NestedBotAction.NestedBotIdKey].ToString()!);
        Assert.Equal("Inner", target.Get(newInnerId)!.Name);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotExportTests"`
Expected: FAIL — `NestedBotLibrary` has no `Export` method (compile error).

- [ ] **Step 3: Implement `Export` + `ReachableFrom`**

In `BotBuilder.Core/NestedBots/NestedBotLibrary.cs`, add these two methods (place `Export` right after `Import`, and `ReachableFrom` next to the existing `ReferencedIds`):

```csharp
    /// <summary>Builds a self-contained standalone bot from the library entry <paramref name="id"/>: its own graph
    /// as the top-level bot, with every transitively-referenced nested bot hoisted into
    /// <see cref="Bot.NestedBots"/>. Ids are preserved so the file is internally consistent; the source library is
    /// not modified. The inverse of <see cref="Import"/>.</summary>
    public Bot Export(Guid id)
    {
        var entry = Get(id)
            ?? throw new InvalidOperationException($"Nested bot '{id}' is not in the library.");

        var reachable = ReachableFrom(id);                       // includes id itself
        var identity = reachable.ToDictionary(g => g, g => g);   // preserve ids through CloneBot

        var top = CloneBot(entry, identity);
        foreach (var candidate in _entries)
        {
            if (candidate.Id != id && reachable.Contains(candidate.Id))
            {
                top.NestedBots.Add(CloneBot(candidate, identity));
            }
        }
        return top;
    }

    /// <summary>The transitive closure of nested-bot ids reachable from <paramref name="rootId"/> (inclusive),
    /// following Nested Bot card references. Dangling references (no matching entry) are still included so the
    /// identity id-map stays complete.</summary>
    private HashSet<Guid> ReachableFrom(Guid rootId)
    {
        var reachable = new HashSet<Guid>();
        var stack = new Stack<Guid>();
        stack.Push(rootId);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!reachable.Add(current)) { continue; }
            if (Get(current) is { } bot)
            {
                foreach (var referenced in ReferencedIds(bot)) { stack.Push(referenced); }
            }
        }
        return reachable;
    }
```

Note: `CloneBot` already leaves `NestedBots` empty, so `top.NestedBots.Add(...)` starts from an empty list. `identity` maps every reachable id to itself, so `CloneBot`/`CloneAction` preserve ids and `nestedBotId` references.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotExportTests"`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add BotBuilder.Core/NestedBots/NestedBotLibrary.cs BotBuilder.Core.Tests/NestedBots/NestedBotExportTests.cs
git commit -m "feat: NestedBotLibrary.Export builds a self-contained standalone bot

Claude-Session: https://claude.ai/code/session_01LzMA3zD4rSCmwRhR1u6D2F"
```

---

## Task 2: `PropertiesViewModel.ExportSelectedNestedBot`

**Files:**
- Modify: `BotBuilder.Core/Properties/PropertiesViewModel.cs`
- Test: `BotBuilder.Core.Tests/Properties/NestedBotPropertiesTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `BotBuilder.Core.Tests/Properties/NestedBotPropertiesTests.cs` (inside the class):

```csharp
    [Fact]
    public void ExportSelectedNestedBot_ReturnsStandaloneBotForSelection()
    {
        var editor = NewEditor();
        var node = editor.AddNode(NestedBotAction.NestedBotTypeKey, 0, 0);
        editor.Select(node);
        var bot = editor.NestedBotLibrary.AddNew("Sub");
        editor.Properties.SelectedNestedBotId = bot.Id;

        var exported = editor.Properties.ExportSelectedNestedBot();

        Assert.NotNull(exported);
        Assert.Equal(bot.Id, exported!.Id);
        Assert.Equal("Sub", exported.Name);
    }

    [Fact]
    public void ExportSelectedNestedBot_ReturnsNullWhenNothingSelected()
    {
        var editor = NewEditor();
        var node = editor.AddNode(NestedBotAction.NestedBotTypeKey, 0, 0);
        editor.Select(node);   // card selected, but no entry assigned

        Assert.Null(editor.Properties.ExportSelectedNestedBot());
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~ExportSelectedNestedBot"`
Expected: FAIL — `PropertiesViewModel` has no `ExportSelectedNestedBot` (compile error).

- [ ] **Step 3: Implement the method**

In `BotBuilder.Core/Properties/PropertiesViewModel.cs`, add next to `ImportNestedBot`:

```csharp
    /// <summary>Builds a standalone, self-contained bot for the currently-selected nested-bot card's entry, ready
    /// to serialize to its own .bot file (bundles transitively-referenced nested bots). Returns null when no entry
    /// is selected. Non-destructive — the library is untouched.</summary>
    public Bot? ExportSelectedNestedBot()
        => SelectedNestedBotId is Guid id ? _editor.NestedBotLibrary.Export(id) : null;
```

(`Bot` is already imported in this file via `using AdbCore.Models;` — confirm at the top; the type is used by `ImportNestedBot`/`NestedBotEntries`.)

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~ExportSelectedNestedBot"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add BotBuilder.Core/Properties/PropertiesViewModel.cs BotBuilder.Core.Tests/Properties/NestedBotPropertiesTests.cs
git commit -m "feat: PropertiesViewModel.ExportSelectedNestedBot exposes the selected entry for export

Claude-Session: https://claude.ai/code/session_01LzMA3zD4rSCmwRhR1u6D2F"
```

---

## Task 3: Properties-panel "Export .bot…" button (WPF)

**Files:**
- Modify: `BotBuilder/MainWindow.xaml:454-459` (the New/Import/Remove button row)
- Modify: `BotBuilder/MainWindow.xaml.cs` (new handler + shared save helper)

No unit test — WPF surface, verified visually at the end.

- [ ] **Step 1: Add the button to the XAML**

In `BotBuilder/MainWindow.xaml`, the horizontal button row currently reads:

```xml
                                <StackPanel Orientation="Horizontal">
                                    <Button Content="New" Click="NewNestedBot_Click" Padding="6,2" Margin="0,0,4,0"
                                            ToolTip="Create a new empty nested bot and open its editor" />
                                    <Button Content="Import .bot…" Click="ImportNestedBot_Click" Padding="6,2" Margin="0,0,4,0" />
                                    <Button Content="Remove" Click="RemoveNestedBot_Click" Padding="6,2" />
                                </StackPanel>
```

Insert an Export button between Import and Remove:

```xml
                                <StackPanel Orientation="Horizontal">
                                    <Button Content="New" Click="NewNestedBot_Click" Padding="6,2" Margin="0,0,4,0"
                                            ToolTip="Create a new empty nested bot and open its editor" />
                                    <Button Content="Import .bot…" Click="ImportNestedBot_Click" Padding="6,2" Margin="0,0,4,0" />
                                    <Button Content="Export .bot…" Click="ExportNestedBot_Click" Padding="6,2" Margin="0,0,4,0"
                                            ToolTip="Save this nested bot as its own standalone .bot file" />
                                    <Button Content="Remove" Click="RemoveNestedBot_Click" Padding="6,2" />
                                </StackPanel>
```

- [ ] **Step 2: Add the handler + shared save helper**

In `BotBuilder/MainWindow.xaml.cs`, add next to `ImportNestedBot_Click` (near line 1088):

```csharp
    private void ExportNestedBot_Click(object sender, RoutedEventArgs e)
    {
        // No-op when no library entry is assigned, mirroring Remove.
        if (_editor.Properties.ExportSelectedNestedBot() is { } bot)
        {
            SaveExportedBot(bot);
        }
    }

    // Shared by the Properties-panel export button and the child editor's Export-standalone menu item:
    // prompts for a path (pre-filled with a sanitized bot name) and writes a self-contained .bot.
    private void SaveExportedBot(AdbCore.Models.Bot bot)
    {
        var suggested = string.Join("_", bot.Name.Split(System.IO.Path.GetInvalidFileNameChars()));
        var dialog = new SaveFileDialog
        {
            Filter = BotFilter,
            DefaultExt = ".bot",
            AddExtension = true,
            FileName = string.IsNullOrWhiteSpace(suggested) ? "nested" : suggested,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            new AdbCore.Serialization.BotSerializer().Save(bot, dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't export that bot: {ex.Message}", "Export nested bot",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build ADB.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add BotBuilder/MainWindow.xaml BotBuilder/MainWindow.xaml.cs
git commit -m "feat: Export .bot button in the nested-bot properties panel

Claude-Session: https://claude.ai/code/session_01LzMA3zD4rSCmwRhR1u6D2F"
```

---

## Task 4: Child-editor "Export standalone .bot…" menu item (WPF)

**Files:**
- Modify: `BotBuilder/MainWindow.xaml:116-117` (File menu, after Save As)
- Modify: `BotBuilder/MainWindow.xaml.cs` (`ApplyChildMode` + new handler)

No unit test — WPF surface, verified visually at the end.

- [ ] **Step 1: Add the menu item (collapsed by default)**

In `BotBuilder/MainWindow.xaml`, the File menu currently has:

```xml
                <MenuItem x:Name="SaveAsMenuItem" Header="Save _As..." Click="SaveAs_Click" InputGestureText="Ctrl+Shift+S" />
                <Separator />
```

Insert a child-only export item after Save As:

```xml
                <MenuItem x:Name="SaveAsMenuItem" Header="Save _As..." Click="SaveAs_Click" InputGestureText="Ctrl+Shift+S" />
                <MenuItem x:Name="ExportStandaloneMenuItem" Header="_Export standalone .bot..." Click="ExportStandalone_Click"
                          Visibility="Collapsed"
                          ToolTip="Save this nested bot as its own standalone .bot file" />
                <Separator />
```

- [ ] **Step 2: Reveal the item in child mode**

In `BotBuilder/MainWindow.xaml.cs`, `ApplyChildMode` hides Save As with `SaveAsMenuItem.Visibility = Visibility.Collapsed;`. Immediately after that line, reveal the export item:

```csharp
        // Hide Save As — a nested entry has no independent file
        SaveAsMenuItem.Visibility = Visibility.Collapsed;

        // Reveal Export standalone — a nested entry CAN be pulled out into its own file
        ExportStandaloneMenuItem.Visibility = Visibility.Visible;
```

- [ ] **Step 3: Add the handler**

In `BotBuilder/MainWindow.xaml.cs`, add near `ExportNestedBot_Click`:

```csharp
    private void ExportStandalone_Click(object sender, RoutedEventArgs e)
    {
        if (!_isChild || _childSession is null)
        {
            return;
        }

        _childSession.SyncBack(); // capture in-flight edits into the library entry first
        SaveExportedBot(_editor.NestedBotLibrary.Export(_childSession.NestedBotId));
    }
```

(`_editor.NestedBotLibrary` on a child window is the shared root library — the child editor was built with it — so `Export` sees the just-synced entry.)

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build ADB.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add BotBuilder/MainWindow.xaml BotBuilder/MainWindow.xaml.cs
git commit -m "feat: Export standalone .bot menu item in the nested-bot child editor

Claude-Session: https://claude.ai/code/session_01LzMA3zD4rSCmwRhR1u6D2F"
```

---

## Task 5: Documentation sync

**Files:**
- Modify: `CLAUDE.md` (the `.bot` File Format → `nestedBots` bullet)
- Modify: `README.md` (nested-bots mention, if present — keep the goblin voice)
- Modify: `../ADB.wiki` (nested-bots / `.bot` reference page)

- [ ] **Step 1: Update `CLAUDE.md`**

In `CLAUDE.md`, find the `nestedBots` bullet under **.bot File Format** (begins "`nestedBots` is the flat reusable sub-bot library…"). Append a sentence describing export:

```
A library entry can be pulled back out into its own standalone `.bot` via **Export**
(`NestedBotLibrary.Export`, the inverse of `Import`): the entry's graph becomes the top-level bot and every
transitively-referenced nested bot is hoisted into that file's `nestedBots` (ids preserved, source library
untouched), so the exported file runs standalone. Exposed as an **Export .bot…** button in the nested-bot
properties panel and an **Export standalone .bot…** item in the child editor's File menu.
```

If a "Nested Bots" summary table row or Key-Modules entry mentions `Import`, add `Export` alongside it there too.

- [ ] **Step 2: Update `README.md`**

Search `README.md` for nested-bot / Import copy (`grep -in "nested" README.md`). If the README describes importing a nested bot, add a matching, accurate line for export in the same voice — e.g. that you can *yank a nested bot out into its own `.bot` file*, bundled and ready to run solo. If the README has no nested-bot import copy, no change is needed; note that in the commit body.

- [ ] **Step 3: Update the wiki**

The wiki is a sibling clone at `../ADB.wiki` (default branch `master`, not a submodule). Find the page covering nested bots / the `.bot` format:

Run: `grep -rl -i "nested" ../ADB.wiki`

Edit the relevant page(s) to document the Export path: both entry points, that the file is self-contained (bundles transitively-referenced nested bots), ids preserved, and that it's non-destructive. Mirror the wording used for Import so the two read as a pair.

- [ ] **Step 4: Verify docs against the diff**

Re-read the three edits against the actual code from Tasks 1–4. Confirm every claim (method name `Export`, "inverse of Import", button/menu labels, "self-contained", "ids preserved", "non-destructive") is backed by the implemented behavior. Fix any drift.

- [ ] **Step 5: Commit (main repo)**

```bash
git add CLAUDE.md README.md
git commit -m "docs: document nested-bot export across CLAUDE.md and README

Claude-Session: https://claude.ai/code/session_01LzMA3zD4rSCmwRhR1u6D2F"
```

- [ ] **Step 6: Commit + push the wiki (separate repo)**

```bash
cd ../ADB.wiki
git add -A
git commit -m "Document nested-bot export (self-contained .bot, two entry points)"
git push
cd ../ADB
```

---

## Final verification

- [ ] Run the whole suite: `dotnet test ADB.slnx` — all green.
- [ ] Build: `dotnet build ADB.slnx` — 0 errors.
- [ ] Manual (user): in BotBuilder, select a Nested Bot card with an assigned entry → **Export .bot…** writes a file; re-open it via **Import .bot…** and confirm the graph (and any inner nested bots) come back intact. Open a nested bot's child editor → **File ▸ Export standalone .bot…** exports the currently-edited bot including unsaved edits.
