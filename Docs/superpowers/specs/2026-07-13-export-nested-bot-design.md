# Export a Nested Bot to a Standalone `.bot` File

**Date:** 2026-07-13
**Status:** Approved — ready for implementation plan

## Problem

Nested bots live only inside a root bot's flat library (`Bot.NestedBots`); a Nested Bot
card references a library entry by `nestedBotId`. There is already a symmetric **Import**
path (`NestedBotLibrary.Import`) that pulls an external `.bot` *into* the library, flattening
and remapping ids. The inverse is missing: there is no way to pull a library entry back **out**
into its own standalone, runnable `.bot` file.

## Goal

Let the user export a nested bot as a self-contained `.bot` file that:

- contains the nested bot's own graph as the top-level bot, and
- bundles every nested bot it *transitively* references, so the file runs standalone
  (e.g. via `BotRunner --bot exported.bot --target …`).

## Non-Goals

- No change to how nested bots run inside a parent (target binding, logging prefixes, etc.).
- No new file format or schema version — the exported file is an ordinary `.bot`.
- Export does not modify the source library in any way.

## Core Routine — `NestedBotLibrary.Export`

Add a pure, non-mutating method, the inverse of the existing `Import`:

```csharp
public Bot Export(Guid id)
```

Behavior:

- **Top level** = the entry's own graph (actions, connections, targets), **deep-copied** so the
  returned `Bot` is fully detached from the library (later mutation of one cannot affect the other).
- **`NestedBots`** = every library entry the exported one **transitively references** — walking
  `nestedBotId` config through the existing `ReferencedIds` closure — hoisted into the flat list.
  This mirrors exactly how a root bot stores its library. Unrelated library entries are **not**
  included; the bundle is minimal.
- **Ids preserved.** The clone runs through the existing `CloneBot` with an *identity* id-map
  (each id maps to itself). Because every bundled bot and every `nestedBotId` reference keeps its
  id, the exported file is internally consistent with **no remapping needed**. `Import` already
  remaps to fresh ids on the way back in, so a round-trip stays clean.
- Throws `InvalidOperationException` if `id` is not in the library (matching
  `NestedBotEditorSession.Open`).

Rationale for placement: `NestedBotLibrary` already owns `Import`, `ReferencedIds`, and
`CloneBot`. Building the bot inline in the WPF handlers instead would duplicate the transitive
walk across two entry points and could not be unit-tested. Keeping the logic in `.Core` makes
`Import`/`Export` verifiable as inverses.

### Transitive closure

Collect the reachable set by walking `nestedBotId` references from the exported entry (reusing the
same reference-extraction the cycle guard uses). The exported entry itself is the top-level bot;
all *other* reachable entries populate `NestedBots`.

## Entry Point 1 — Properties Panel

When a Nested Bot card is selected, add an **"Export .bot…"** button beside the existing
"Import .bot…" / "Remove" buttons.

- `PropertiesViewModel` exposes the selected entry as an exportable `Bot` (via
  `NestedBotLibrary.Export(SelectedNestedBotId)`), mirroring how `ImportNestedBot(Bot)` splits
  work: `.Core` builds/returns the `Bot`; the WPF layer owns the file dialog + serializer.
- WPF handler: `SaveFileDialog` (default filename = the entry's `Name`, sanitized for invalid path
  chars; `.bot` filter), then `BotSerializer.Save`.
- The button is disabled / no-ops when no entry is selected.

## Entry Point 2 — Child Editor

Add a **"Save as standalone .bot…"** command to the nested bot's child-editor window
(menu/toolbar).

- Calls `NestedBotEditorSession.SyncBack()` first so in-flight edits are captured into the library
  entry.
- Then exports its own `NestedBotId` through the **same** `NestedBotLibrary.Export` routine and the
  same `SaveFileDialog` + `BotSerializer.Save` path.

Both entry points share one export routine and one save path.

## Settled Behaviors

- **Non-destructive:** the library is untouched by export.
- **Self-contained & runnable:** target *definitions* (names/selectors) are preserved so the file
  runs standalone under `BotRunner --target …`.
- **Default filename** derives from the bot's `Name`.

## Documentation (Sync Contract)

Update all three surfaces in the same unit of work, adding the Export path alongside the existing
Import description:

- `CLAUDE.md` — `.bot` format / nested-bots notes.
- `README.md` — keep the goblin voice; describe export accurately.
- `../ADB.wiki` — the detailed nested-bots / `.bot` reference page(s).

## Testing

`NestedBotLibrary` unit tests (xUnit, hand-rolled fakes per repo convention):

- Export bundles transitively-referenced nested bots into `NestedBots`.
- Export excludes library entries the exported bot does not reference.
- Export preserves ids (top-level + bundled + `nestedBotId` references stay consistent).
- Export does **not** mutate the source library (entry count and contents unchanged).
- **Round-trip:** `Import(Export(x))` yields an equivalent graph (structure/labels/connections),
  with fresh ids as `Import` guarantees.
- Export of an unknown id throws `InvalidOperationException`.
