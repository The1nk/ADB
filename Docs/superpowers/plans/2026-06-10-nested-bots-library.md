# Nested Bots — Library Data Layer Slice (B3a) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The editor owns the root nested-bot library: a WPF-free `NestedBotLibrary` (add / import / rename / remove / lookup), edit-time cycle detection, and round-trip through `DocumentMapper` so `Bot.NestedBots` saves and loads. No UI in this slice (validated by tests).

**Architecture:** `NestedBotLibrary` (BotBuilder.Core) wraps an `ObservableCollection<Bot>` of library entries and exposes operations the future properties panel will call. `BotEditorViewModel` holds one (shareable with future child editors). `DocumentMapper.ToBot`/`Populate` carry the library between the model and the editor. Import deep-copies an external bot into the flat library with fresh **bot** ids and remapped `nestedBotId` references (internal action/connection/target ids are kept — they're scoped per bot, so they don't collide across entries).

**Tech Stack:** .NET 10, BotBuilder.Core (no WPF), xUnit.

Reference spec: `Docs/superpowers/specs/2026-06-10-title-bar-and-nested-bots-design.md` (Feature B, sections B0/B4/B5 cycle guard). Builds on merged B1/B2.

Work in worktree `C:\git\ADB-nested-lib` (branch `worktree-nested-bots-library`). Build/test from the worktree root.

---

### Task 1: `NestedBotLibrary` — entries, add, rename, remove, get

**Files:**
- Create: `BotBuilder.Core/NestedBots/NestedBotLibrary.cs`
- Test: `BotBuilder.Core.Tests/NestedBots/NestedBotLibraryTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `BotBuilder.Core.Tests/NestedBots/NestedBotLibraryTests.cs`:

```csharp
using AdbCore.Models;
using BotBuilder.Core.NestedBots;
using Xunit;

namespace BotBuilder.Core.Tests.NestedBots;

public class NestedBotLibraryTests
{
    [Fact]
    public void AddNew_CreatesNamedEntryWithFreshId()
    {
        var lib = new NestedBotLibrary();
        var bot = lib.AddNew("GoToPlayerMenu");

        Assert.Equal("GoToPlayerMenu", bot.Name);
        Assert.NotEqual(Guid.Empty, bot.Id);
        Assert.Single(lib.Entries);
        Assert.Same(bot, lib.Get(bot.Id));
    }

    [Fact]
    public void Rename_UpdatesEntryName()
    {
        var lib = new NestedBotLibrary();
        var bot = lib.AddNew("Old");
        lib.Rename(bot.Id, "New");
        Assert.Equal("New", lib.Get(bot.Id)!.Name);
    }

    [Fact]
    public void Remove_DropsEntry()
    {
        var lib = new NestedBotLibrary();
        var bot = lib.AddNew("X");
        Assert.True(lib.Remove(bot.Id));
        Assert.Empty(lib.Entries);
        Assert.Null(lib.Get(bot.Id));
    }

    [Fact]
    public void Load_ReplacesEntries()
    {
        var lib = new NestedBotLibrary();
        lib.AddNew("Stale");
        var fresh = new Bot { Id = Guid.NewGuid(), Name = "Loaded" };
        lib.Load(new[] { fresh });
        Assert.Single(lib.Entries);
        Assert.Equal("Loaded", lib.Entries[0].Name);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotLibraryTests"`
Expected: FAIL — type does not exist.

- [ ] **Step 3: Create the class**

Create `BotBuilder.Core/NestedBots/NestedBotLibrary.cs`:

```csharp
using System.Collections.ObjectModel;
using AdbCore.Models;

namespace BotBuilder.Core.NestedBots;

/// <summary>The root bot's flat library of reusable nested-bot definitions. Cards reference an entry by id;
/// editing an entry updates every card that uses it. Round-tripped through <c>DocumentMapper</c> into
/// <see cref="Bot.NestedBots"/>.</summary>
public sealed class NestedBotLibrary
{
    private readonly ObservableCollection<Bot> _entries = new();

    public ReadOnlyObservableCollection<Bot> Entries { get; }

    public NestedBotLibrary() => Entries = new ReadOnlyObservableCollection<Bot>(_entries);

    /// <summary>Creates an empty nested bot (fresh id) and adds it to the library.</summary>
    public Bot AddNew(string name = "Untitled Bot")
    {
        var bot = new Bot { Id = Guid.NewGuid(), Name = name };
        _entries.Add(bot);
        return bot;
    }

    public Bot? Get(Guid id) => _entries.FirstOrDefault(b => b.Id == id);

    public void Rename(Guid id, string name)
    {
        if (Get(id) is { } bot) { bot.Name = name; }
    }

    public bool Remove(Guid id)
    {
        if (Get(id) is { } bot) { return _entries.Remove(bot); }
        return false;
    }

    /// <summary>Replaces all entries (used when loading a document).</summary>
    public void Load(IEnumerable<Bot> entries)
    {
        _entries.Clear();
        foreach (var b in entries) { _entries.Add(b); }
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotLibraryTests"`
Expected: PASS (4).

- [ ] **Step 5: Commit**

```bash
git add BotBuilder.Core/NestedBots/NestedBotLibrary.cs BotBuilder.Core.Tests/NestedBots/NestedBotLibraryTests.cs
git commit -m "Add NestedBotLibrary (add/rename/remove/get/load)"
```

---

### Task 2: Edit-time cycle detection

**Files:**
- Modify: `BotBuilder.Core/NestedBots/NestedBotLibrary.cs`
- Test: `BotBuilder.Core.Tests/NestedBots/NestedBotCycleTests.cs` (create)

A reference graph over the library: each entry "references" the `nestedBotId`s of its `control.nestedBot` actions. `WouldCreateCycle(hostId, candidateId)` answers: if a card inside `hostId` referenced `candidateId`, would that create a cycle? True when `candidateId == hostId` (self-reference) or `hostId` is already reachable from `candidateId` (candidate transitively references host).

- [ ] **Step 1: Write the failing test**

Create `BotBuilder.Core.Tests/NestedBots/NestedBotCycleTests.cs`:

```csharp
using AdbCore.Actions.BuiltIn;
using AdbCore.Models;
using BotBuilder.Core.NestedBots;
using Xunit;

namespace BotBuilder.Core.Tests.NestedBots;

public class NestedBotCycleTests
{
    private static BotAction Ref(Guid nestedId) => new()
    {
        Id = Guid.NewGuid(),
        TypeKey = NestedBotAction.NestedBotTypeKey,
        Config = { [NestedBotAction.NestedBotIdKey] = nestedId.ToString() },
    };

    [Fact]
    public void SelfReference_IsCycle()
    {
        var lib = new NestedBotLibrary();
        var a = lib.AddNew("A");
        Assert.True(lib.WouldCreateCycle(a.Id, a.Id));
    }

    [Fact]
    public void DirectBackReference_IsCycle()
    {
        var lib = new NestedBotLibrary();
        var a = lib.AddNew("A");
        var b = lib.AddNew("B");
        b.Actions.Add(Ref(a.Id)); // B already references A
        // Assigning a card in A that references B would close the loop A->B->A.
        Assert.True(lib.WouldCreateCycle(a.Id, b.Id));
    }

    [Fact]
    public void TransitiveBackReference_IsCycle()
    {
        var lib = new NestedBotLibrary();
        var a = lib.AddNew("A");
        var b = lib.AddNew("B");
        var c = lib.AddNew("C");
        b.Actions.Add(Ref(c.Id)); // B->C
        c.Actions.Add(Ref(a.Id)); // C->A
        // A->B would close A->B->C->A.
        Assert.True(lib.WouldCreateCycle(a.Id, b.Id));
    }

    [Fact]
    public void IndependentReference_IsNotCycle()
    {
        var lib = new NestedBotLibrary();
        var a = lib.AddNew("A");
        var b = lib.AddNew("B");
        Assert.False(lib.WouldCreateCycle(a.Id, b.Id)); // B doesn't reference A
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotCycleTests"`
Expected: FAIL — `WouldCreateCycle` doesn't exist.

- [ ] **Step 3: Add cycle detection**

In `BotBuilder.Core/NestedBots/NestedBotLibrary.cs`, add `using AdbCore.Actions;` and `using AdbCore.Actions.BuiltIn;` at the top, and add these members:

```csharp
    /// <summary>Would a card inside <paramref name="hostId"/> referencing <paramref name="candidateId"/> create a
    /// reference cycle? True for a self-reference, or when <paramref name="hostId"/> is reachable from
    /// <paramref name="candidateId"/> through existing nested-bot references.</summary>
    public bool WouldCreateCycle(Guid hostId, Guid candidateId)
    {
        if (candidateId == hostId) { return true; }

        var visited = new HashSet<Guid>();
        var stack = new Stack<Guid>();
        stack.Push(candidateId);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current == hostId) { return true; }
            if (!visited.Add(current)) { continue; }
            if (Get(current) is { } bot)
            {
                foreach (var referenced in ReferencedIds(bot)) { stack.Push(referenced); }
            }
        }
        return false;
    }

    /// <summary>The nested-bot ids referenced by a bot's Nested Bot action cards.</summary>
    private static IEnumerable<Guid> ReferencedIds(Bot bot)
    {
        foreach (var action in bot.Actions)
        {
            if (action.TypeKey != NestedBotAction.NestedBotTypeKey) { continue; }
            if (action.Config.TryGetValue(NestedBotAction.NestedBotIdKey, out var raw)
                && Guid.TryParse(raw?.ToString(), out var id))
            {
                yield return id;
            }
        }
    }
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotCycleTests"`
Expected: PASS (4).

- [ ] **Step 5: Commit**

```bash
git add BotBuilder.Core/NestedBots/NestedBotLibrary.cs BotBuilder.Core.Tests/NestedBots/NestedBotCycleTests.cs
git commit -m "Add edit-time cycle detection to NestedBotLibrary"
```

---

### Task 3: Import an external bot into the library

**Files:**
- Modify: `BotBuilder.Core/NestedBots/NestedBotLibrary.cs`
- Test: `BotBuilder.Core.Tests/NestedBots/NestedBotImportTests.cs` (create)

Import deep-copies an external bot as a new library entry. Every imported bot (the external top + its own flat nested library) gets a fresh **bot** id; `nestedBotId` references are remapped old→new. Internal action/connection/target ids are kept (scoped per bot; no cross-entry collision). The import is fully detached from the source (fresh config dictionaries).

- [ ] **Step 1: Write the failing test**

Create `BotBuilder.Core.Tests/NestedBots/NestedBotImportTests.cs`:

```csharp
using AdbCore.Actions.BuiltIn;
using AdbCore.Models;
using BotBuilder.Core.NestedBots;
using Xunit;

namespace BotBuilder.Core.Tests.NestedBots;

public class NestedBotImportTests
{
    [Fact]
    public void Import_AddsEntryWithFreshId_DetachedFromSource()
    {
        var lib = new NestedBotLibrary();
        var external = new Bot
        {
            Id = Guid.NewGuid(),
            Name = "GoToPlayerMenu",
            Actions = { new BotAction { Id = Guid.NewGuid(), TypeKey = "control.start", Config = { ["k"] = "v" } } },
        };

        var imported = lib.Import(external);

        Assert.Contains(imported, lib.Entries);
        Assert.NotEqual(external.Id, imported.Id);            // fresh entry id
        Assert.Equal("GoToPlayerMenu", imported.Name);
        Assert.NotSame(external.Actions[0], imported.Actions[0]); // deep copy
        Assert.NotSame(external.Actions[0].Config, imported.Actions[0].Config);
    }

    [Fact]
    public void Import_FlattensNestedLibrary_AndRemapsReferences()
    {
        // external is itself a root: top graph has a card referencing its own nested entry "Inner".
        var inner = new Bot { Id = Guid.NewGuid(), Name = "Inner" };
        var external = new Bot { Id = Guid.NewGuid(), Name = "Outer", NestedBots = { inner } };
        external.Actions.Add(new BotAction
        {
            Id = Guid.NewGuid(),
            TypeKey = NestedBotAction.NestedBotTypeKey,
            Config = { [NestedBotAction.NestedBotIdKey] = inner.Id.ToString() },
        });

        var lib = new NestedBotLibrary();
        var imported = lib.Import(external);

        // Both bots are now flat entries with fresh ids.
        Assert.Equal(2, lib.Entries.Count);
        var importedInner = lib.Entries.Single(b => b.Name == "Inner");
        Assert.NotEqual(inner.Id, importedInner.Id);

        // The card's reference was remapped to the new Inner id.
        var card = imported.Actions.Single(a => a.TypeKey == NestedBotAction.NestedBotTypeKey);
        Assert.Equal(importedInner.Id.ToString(), card.Config[NestedBotAction.NestedBotIdKey]);

        // The imported entries carry no own NestedBots (flattened into the root library).
        Assert.Empty(imported.NestedBots);
        Assert.Empty(importedInner.NestedBots);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotImportTests"`
Expected: FAIL — `Import` doesn't exist.

- [ ] **Step 3: Add `Import`**

In `BotBuilder.Core/NestedBots/NestedBotLibrary.cs`, add these members (the clone helpers are private):

```csharp
    /// <summary>Imports an external bot as a new library entry, deep-copied and detached from the source. The
    /// external's own nested library is flattened into this library; every imported bot gets a fresh id and all
    /// <c>nestedBotId</c> references are remapped. Returns the new top-level entry.</summary>
    public Bot Import(Bot external)
    {
        ArgumentNullException.ThrowIfNull(external);

        var sources = new List<Bot> { external };
        sources.AddRange(external.NestedBots);
        var idMap = sources.ToDictionary(b => b.Id, _ => Guid.NewGuid());

        Bot top = null!;
        foreach (var src in sources)
        {
            var clone = CloneBot(src, idMap);
            _entries.Add(clone);
            if (ReferenceEquals(src, external)) { top = clone; }
        }
        return top;
    }

    private static Bot CloneBot(Bot src, IReadOnlyDictionary<Guid, Guid> idMap) => new()
    {
        Id = idMap[src.Id],
        Name = src.Name,
        Description = src.Description,
        Targets = src.Targets.Select(CloneTarget).ToList(),
        Actions = src.Actions.Select(a => CloneAction(a, idMap)).ToList(),
        Connections = src.Connections.Select(CloneConnection).ToList(),
        // NestedBots intentionally left empty — flattened into the library.
    };

    private static BotAction CloneAction(BotAction a, IReadOnlyDictionary<Guid, Guid> idMap)
    {
        var config = new Dictionary<string, object>(a.Config);
        if (a.TypeKey == NestedBotAction.NestedBotTypeKey
            && config.TryGetValue(NestedBotAction.NestedBotIdKey, out var raw)
            && Guid.TryParse(raw?.ToString(), out var oldRef)
            && idMap.TryGetValue(oldRef, out var newRef))
        {
            config[NestedBotAction.NestedBotIdKey] = newRef.ToString();
        }

        return new BotAction
        {
            Id = a.Id, // kept — scoped to this bot's graph
            TypeKey = a.TypeKey,
            Label = a.Label,
            TargetId = a.TargetId,
            Config = config,
            Retry = a.Retry is null ? null : new RetryPolicy { MaxAttempts = a.Retry.MaxAttempts, DelayMs = a.Retry.DelayMs },
            CanvasPosition = new Position { X = a.CanvasPosition.X, Y = a.CanvasPosition.Y },
        };
    }

    private static ActionConnection CloneConnection(ActionConnection c) => new()
    {
        Id = c.Id,
        SourceActionId = c.SourceActionId,
        SourcePort = c.SourcePort,
        TargetActionId = c.TargetActionId,
        TargetPort = c.TargetPort,
    };

    private static BotTarget CloneTarget(BotTarget t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        Type = t.Type,
        Config = new Dictionary<string, string>(t.Config),
    };
```

(Confirm `RetryPolicy`, `Position`, `ActionConnection`, `BotTarget` member names against the models in `AdbCore/Models/` — `ActionConnection` uses `SourceActionId`/`SourcePort`/`TargetActionId`/`TargetPort`; `BotAction.CanvasPosition` is a `Position` with `X`/`Y`.)

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotImportTests"`
Expected: PASS (2).

- [ ] **Step 5: Commit**

```bash
git add BotBuilder.Core/NestedBots/NestedBotLibrary.cs BotBuilder.Core.Tests/NestedBots/NestedBotImportTests.cs
git commit -m "Add NestedBotLibrary.Import (deep-copy, flatten, remap references)"
```

---

### Task 4: Wire the library into the editor + DocumentMapper round-trip

**Files:**
- Modify: `BotBuilder.Core/BotEditorViewModel.cs`
- Modify: `BotBuilder.Core/DocumentMapper.cs`
- Test: `BotBuilder.Core.Tests/NestedBots/NestedBotLibraryRoundTripTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `BotBuilder.Core.Tests/NestedBots/NestedBotLibraryRoundTripTests.cs`:

```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests.NestedBots;

public class NestedBotLibraryRoundTripTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    [Fact]
    public void ToBot_WritesLibrary()
    {
        var editor = NewEditor();
        editor.NestedBotLibrary.AddNew("Sub");

        var bot = DocumentMapper.ToBot(editor);

        Assert.Single(bot.NestedBots);
        Assert.Equal("Sub", bot.NestedBots[0].Name);
    }

    [Fact]
    public void Populate_LoadsLibrary()
    {
        var editor = NewEditor();
        var registry = new ActionRegistry();
        BuiltInActions.Register(registry, new ActionExecutorRegistry());
        var bot = new Bot { Id = Guid.NewGuid(), Name = "Root", NestedBots = { new Bot { Id = Guid.NewGuid(), Name = "Sub" } } };

        DocumentMapper.Populate(editor, bot, registry);

        Assert.Single(editor.NestedBotLibrary.Entries);
        Assert.Equal("Sub", editor.NestedBotLibrary.Entries[0].Name);
    }

    [Fact]
    public void New_ClearsLibrary()
    {
        var editor = NewEditor();
        editor.NestedBotLibrary.AddNew("Sub");
        editor.New();
        Assert.Empty(editor.NestedBotLibrary.Entries);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotLibraryRoundTripTests"`
Expected: FAIL — `BotEditorViewModel.NestedBotLibrary` doesn't exist.

- [ ] **Step 3a: Add the property to `BotEditorViewModel`**

In `BotBuilder.Core/BotEditorViewModel.cs`:
- Add `using BotBuilder.Core.NestedBots;` to the usings.
- Add a constructor parameter so a child editor can share the root's library, defaulting to a new one. Change the constructor signature from:
```csharp
    public BotEditorViewModel(ActionRegistry registry)
    {
        _registry = registry;
```
to:
```csharp
    public BotEditorViewModel(ActionRegistry registry, NestedBotLibrary? nestedBotLibrary = null)
    {
        _registry = registry;
        NestedBotLibrary = nestedBotLibrary ?? new NestedBotLibrary();
```
- Add the property (near the other public properties like `Palette`, `Viewport`):
```csharp
    /// <summary>The root nested-bot library (shared with any child editors editing entries of it).</summary>
    public NestedBotLibrary NestedBotLibrary { get; }
```
- In `New()`, after `TargetBar.Targets.Clear();`, add:
```csharp
        NestedBotLibrary.Load(Array.Empty<Bot>());
```
(`New()` already needs `Bot`; ensure `using AdbCore.Models;` is present — it is.)

- [ ] **Step 3b: Round-trip in `DocumentMapper`**

In `BotBuilder.Core/DocumentMapper.cs`:
- In `ToBot`, before `return bot;`, add:
```csharp
        bot.NestedBots = editor.NestedBotLibrary.Entries.ToList();
```
- In `Populate`, before `editor.RefreshTargetBadges();`, add:
```csharp
        editor.NestedBotLibrary.Load(bot.NestedBots);
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotLibraryRoundTripTests"`
Expected: PASS (3).

- [ ] **Step 5: Commit**

```bash
git add BotBuilder.Core/BotEditorViewModel.cs BotBuilder.Core/DocumentMapper.cs BotBuilder.Core.Tests/NestedBots/NestedBotLibraryRoundTripTests.cs
git commit -m "Wire NestedBotLibrary into editor + DocumentMapper round-trip"
```

---

### Task 5: Full suite green

- [ ] **Step 1: Run the whole suite**

Run: `dotnet test ADB.slnx`
Expected: PASS, no regressions.

- [ ] **Step 2: Commit any fixups** (only if needed)

---

## Self-Review

- **Spec coverage (B3a):** library entries + add/new/rename/remove (Task 1, B4); edit-time cycle detection (Task 2, B5 cycle guard); import deep-copy + flatten + remap (Task 3, B4 import); editor ownership + save/load round-trip + New clears (Task 4, B0/B5). The shareable-library constructor param sets up B3c's child editors. ✓
- **Placeholders:** none. ✓
- **Type consistency:** `NestedBotLibrary` API (`Entries`/`AddNew`/`Get`/`Rename`/`Remove`/`Load`/`WouldCreateCycle`/`Import`) used identically across tasks; `NestedBotAction.NestedBotTypeKey`/`NestedBotIdKey` reused from the merged engine slice. ✓
- **Carry-forward note:** the B1 duplicate-id guard nit is naturally avoided here because Import assigns fresh entry ids; AddNew uses `Guid.NewGuid()`. No duplicate-id path is introduced.
- **Note for executor:** verify `RetryPolicy`/`Position`/`ActionConnection`/`BotTarget` member names against `AdbCore/Models/` before writing the clone helpers; adjust if any differ.
