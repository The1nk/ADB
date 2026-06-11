# Design: Bot Name in Title Bar + Nested Bots

Date: 2026-06-10
Status: Approved (pending written-spec review)

This document covers two independent features requested together. They ship as
**separate branches/PRs**: Feature A is a small slice; Feature B is a milestone-sized
effort sequenced as its own plan.

---

## Feature A — Bot name in the title bar + smarter Save

### Goal
The window title reflects the open bot, and Save pre-fills a sensible filename.

- Fresh instance → `ADB Bot Builder: Untitled Bot`
- Saved/opened bot named "My Bot" → `ADB Bot Builder: My Bot.bot`
- Unsaved changes → a leading `*` on the name
- Save pre-fills the filename and appends `.bot` if the user omits the extension

### Design

**`BotEditorViewModel` (BotBuilder.Core)**
- Default `BotName` changes from `"Untitled"` to `"Untitled Bot"`.
- New computed read-only property `WindowTitle`:
  ```
  "ADB Bot Builder: " + (IsDirty ? "*" : "") + BotName + (FilePath != null ? ".bot" : "")
  ```
- `WindowTitle` change notification is raised whenever `BotName`, `FilePath`, or
  `IsDirty` change. (CommunityToolkit.Mvvm: either add `[NotifyPropertyChangedFor(nameof(WindowTitle))]`
  to the three `[ObservableProperty]` backing fields, or raise it from their
  `On…Changed` partial methods.)

**`MainWindow.xaml` (BotBuilder)**
- Replace static `Title="ADB Bot Builder"` with `Title="{Binding WindowTitle}"`.

**Save flow (`MainWindow.xaml.cs` `Save_Click`)**
- Keep `Filter`, `DefaultExt=".bot"`; set `AddExtension=true`; pre-fill
  `FileName = _editor.BotName`. (`DefaultExt`+`AddExtension` already append `.bot`
  when the user types a bare name; this just makes it explicit and pre-fills the name.)

### The open-bot name source
The title uses the bot's **editable `Name`** field (what the VM tracks and Save
pre-fills), not the on-disk file name. Opening `foo.bot` whose internal `Name` is
"My Bot" shows `My Bot.bot`.

### Tests (BotBuilder.Core.Tests)
- Fresh VM → `WindowTitle == "ADB Bot Builder: Untitled Bot"`.
- After an edit (IsDirty) but unsaved → `"ADB Bot Builder: *Untitled Bot"`.
- After `Save(path)` with `BotName="My Bot"` → `"ADB Bot Builder: My Bot.bot"`.
- After a post-save edit → `"ADB Bot Builder: *My Bot.bot"`; after re-save → marker clears.
- `Open` of a bot named "X" → `"ADB Bot Builder: X.bot"`.

---

## Feature B — Nested bots

A reusable sub-bot, referenced by a distinct action card, runnable in multiple
places without duplicating the sub-graph, editable in its own window.

### B0. Model decision: single root-level shared library

"Shared library + **cycle guard**" is realized as a **single root-level flat
library** (not a per-level one): a per-level library cannot form cycles, so a cycle
guard only has meaning when any card, at any depth, can reference into one shared pool.

- The **root** `Bot` gains `List<Bot> NestedBots` — the library of reusable sub-bot
  definitions. Each entry is a full `Bot` (own `Id`, `Name`, `Targets`, `Actions`,
  `Connections`). Nested entries do **not** carry their own `NestedBots`; every card
  at any depth references entries in the one root library by id. Persisted inside the
  parent `.bot` JSON (round-trips through the existing `BotSerializer`).
- A **Nested Bot card** references a library entry via config `nestedBotId`.
- Editing an entry (graph, name, targets) updates **every** card that references it,
  anywhere in the tree.
- **Recursion**: a library entry's graph may itself contain Nested Bot cards →
  genuine recursion → cycles possible → **guarded** (see B5).

### B1. The card — `NestedBotAction`

New `IActionDefinition` in `AdbCore/Actions/BuiltIn/`:
- `TypeKey = "control.nestedBot"`, `Category = "Control Flow"`, `DisplayName = "Nested Bot"`.
- Ports: `in` (input); `onSuccess`, `onFailure` (outputs) — matches the standard
  leaf-action routing the executor already drives.
- `SupportsRetry = true` (retrying the whole sub-run is meaningful).
- Config keys (all **per-card / call-site**, see B6):
  `nestedBotId` (string/guid), `sendVars` (bool), `sendTargets` (bool), `receiveVars` (bool).

The `nestedBotId`/checkboxes are surfaced by a **dedicated properties section** (B4),
not generic `ConfigField` rows, because picking a library entry needs custom UI.

### B2. Card appearance (visibly distinct)

In the node `DataTemplate` (`MainWindow.xaml`), Nested Bot nodes render distinctly:
- A distinct category accent color for `"control.nestedBot"` (extend the
  category-color mapping) plus a **double / "boxed" border** to read as a container.
- A **secondary line on the card** showing the referenced nested bot's **name**
  (resolved live from the library), or `(no bot assigned)` when unset — visible
  **without selecting** the card.
- A small **"open ▸"** affordance (vector `Path` glyph — no color emoji, per the WPF
  constraint) hinting double-click opens the child editor.
- Name display updates live when the library entry is renamed (B7).

### B3. Double-click → child editor

`MainWindow` handles node double-click; for a `"control.nestedBot"` node with an
assigned `nestedBotId`, it opens the child editor (B5). Double-clicking an unassigned
card focuses the properties panel's picker instead (nothing to open yet).

### B4. Properties panel & library UX

A dedicated panel section shown when the selected node is a Nested Bot card:
- **Library dropdown**: lists the root library entries by name; binds `nestedBotId`.
- **New empty nested bot**: creates a fresh library entry (default name
  "Untitled Bot"), assigns it, and opens the child editor.
- **Import from `.bot` file…**: deep-copies an external bot into the library with
  **fresh ids** (one-time; no link back to the source file). If the imported bot
  itself contains nested cards/library entries, its library merges into the root
  library with id-remapping so references stay intact.
- **Rename**: a rename affordance next to the dropdown (so renaming doesn't require
  opening the child editor).
- **Checkboxes**: Send Vars / Send Targets / Receive Vars (per-card).

State matrix for this section: unassigned (empty picker + hint), assigned, missing
reference (id not in library → inline warning + "reassign"), empty library (only
New/Import available).

### B5. Child editor — modeless, deduped, distinct title

- Reuse the BotBuilder editor in a **modeless** window. Small refactor: the editor
  window accepts an **injected `BotEditorViewModel`** plus a **shared library
  reference**, instead of constructing its own in the ctor. The root window keeps its
  current self-construction path.
- **One window per `nestedBotId`**: a registry of open child editors keyed by id;
  re-opening a nested bot **focuses the existing window** rather than opening a second
  — so two windows can't edit one shared definition concurrently.
- **Distinct title** (breadcrumb): `ADB Bot Builder — <RootName> ▸ <NestedName>` with
  the same `*` dirty marker. Deeper nesting extends the breadcrumb
  (`<RootName> ▸ <A> ▸ <B>`).
- Edits apply back into the shared library entry **in memory**; the **root document
  becomes dirty** until the user saves the `.bot`. (Nested bots have no independent
  file save; they persist only inside the root file.)
- The child editor's nested-bot picker shows the **same root library**.
- **Edit-time cycle guard**: assigning/importing an entry that would make the bot
  currently being edited reachable from itself (transitively) is blocked with a clear
  message. Implemented by a reachability check over the library reference graph.

### B6. Execution — `NestedBotExecutor`

A **leaf `IActionExecutor`** registered for `"control.nestedBot"`. On execution:
1. Read `nestedBotId` from the (interpolated) action config. Unassigned → `Fail("This
   Nested Bot card has no bot assigned.")`.
2. Resolve the entry from the run-wide library. Missing → `Fail("Nested bot '<id>'
   not found in this bot's library.")`.
3. **Cycle check**: if `nestedBotId` is already on the ancestry stack →
   `Fail("Nested bot cycle detected: …")`.
4. Build child `ExecutionOptions`:
   - `InitialVariables` = snapshot of parent variables when **Send Vars**, else empty.
   - `ResolvedTargets` = nested bot's own targets (lazily bound, B7-targets), with a
     parent target overlaid onto any nested target of the **same name** when
     **Send Targets**.
   - `NestedAncestry` = parent ancestry + `nestedBotId`.
   - `TargetBinder` and library threaded through unchanged.
   - `Log` = parent log sink.
5. `await new BotExecutor(executors).RunAsync(entry, childOptions, progress, ct)` —
   the parent walk is naturally **paused** until the child completes.
6. **Receive Vars**: merge the child's `FinalVariables` into the parent variables
   (add/overwrite by name).
7. Dispose any child-created target handles (those not shared from the parent).
8. Map `result.Success` → `ActionResult.Ok(outputPort: "onSuccess")`; failure →
   `ActionResult.Fail(result.ErrorMessage)` (routes to `onFailure`).

**Engine plumbing (AdbCore.Execution):**
- `ExecutionOptions` gains: `IReadOnlyDictionary<string, object>? InitialVariables`,
  `IReadOnlyList<Guid> NestedAncestry` (default empty), `ITargetBinder? TargetBinder`,
  and `IReadOnlyDictionary<Guid, Bot>? NestedBotLibrary`.
- `ExecutionResult` gains `IReadOnlyDictionary<string, object> FinalVariables`
  (snapshot of context variables at run end).
- `BotExecutionContext` carries the library map (`IReadOnlyDictionary<Guid, Bot>`),
  `ITargetBinder?`, and `NestedAncestry`.
- `BotExecutor.RunAsync` seeds context variables from `InitialVariables`; sets the
  context library map to `options.NestedBotLibrary` **when provided** (a child run
  inherits the root's flat library unchanged), else builds it from `bot.NestedBots`
  (the root run, whose `NestedBots` *is* the library); copies binder + ancestry from
  options; and snapshots `FinalVariables` into the result.
- `NestedBotExecutor` passes the **same** library map down via
  `childOptions.NestedBotLibrary` — because the library is flat on the root and nested
  entries carry an empty `NestedBots`, the map must be threaded through, not rebuilt.
- `NestedBotExecutor` is constructed with the `ActionExecutorRegistry` so it can build a
  child `BotExecutor`. Registered in `BuiltInActions.Register`.

### B7. Targets — lazy binder service

- New interface in the engine, e.g. `AdbCore.Execution.ITargetBinder`:
  ```csharp
  public interface ITargetBinder
  {
      Task<ResolvedTarget> BindAsync(BotTarget target, CancellationToken ct);
  }
  ```
  Returns a `ResolvedTarget` whose `Handle` may be `IDisposable`/`IAsyncDisposable`.
- **`BotRunner` implements it** by composing the existing per-type binders
  (`Win32WindowResolver`, Android, Browser) for a single `BotTarget`. Refactor the
  current per-collection binders to expose per-target binding. `RunnerApp` constructs
  the binder and passes it via `ExecutionOptions.TargetBinder`.
- **Top-level** targets stay pre-bound as today (no behavior change for existing bots).
- A nested bot's **own** targets bind **lazily** — only when its card executes, so a
  never-reached card launches nothing. Selectors come from the embedded nested
  `BotTarget.Selector` (no CLI plumbing for nested targets). Child-created handles are
  disposed when the child run ends.
- **Send Targets** overlays a parent `ResolvedTarget` onto a nested target of the same
  **name**, sharing the live handle (so a shared window/browser isn't reopened).
- **Test Run** spawns `BotRunner.exe`, so nested bots there get the same binder for free.

### B8. Naming rename propagation

The nested bot's name is `Bot.Name` of the library entry. Renaming it (child-editor
name field or the properties Rename affordance) updates the shared entry; all cards
re-render the new name (they bind to the resolved entry), and open child-editor title
breadcrumbs update.

### B9. README update (final step of Feature B)

Update `README.md` (keeping the "game-grinding goblin" voice, facts accurate) to
document the Nested Bot card: what it does, the shared library + reuse, double-click to
edit in a child window, the Send Vars / Send Targets / Receive Vars wiring, and that an
imported sub-bot is copied into the parent file once. No invented behavior.

### Tests (representative)
- **Serialization**: a `Bot` with a populated `NestedBots` library round-trips
  (BotSerializer); nested cards keep their `nestedBotId`.
- **Execution (AdbCore.Tests)** with fakes:
  - Nested card success/failure routes to `onSuccess`/`onFailure`.
  - Send Vars seeds child; Receive Vars merges back; neither leaks when off.
  - Unassigned / missing-id → clear failure.
  - Cycle (A→B→A) → failure, no infinite loop.
  - Lazy binder: a never-reached nested card never calls `BindAsync`; a reached one
    binds its own targets; Send Targets overlays by name.
- **BotBuilder.Core.Tests**:
  - Library add/import (fresh ids)/rename/remove; rename reflected on referencing cards.
  - Edit-time cycle guard blocks a self-referential assignment.
  - One-editor-per-id dedupe logic.

### Out of scope (v1)
- No independent on-disk persistence for nested bots (they live in the parent file).
- No per-action target laz*iness finer than per-nested-card (binding happens when the
  card runs, not per leaf action inside it).
- No library default flags (flags are strictly per-card, B6).
