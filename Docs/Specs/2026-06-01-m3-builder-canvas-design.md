# M3 — Builder Canvas Design Spec

**Status:** Approved (brainstorm)
**Date:** 2026-06-01
**Milestone:** M3 (Builder Canvas), per `Docs/Design/V1.md` §5 and §9
**Builds on:** M1 (AdbCore models/serialization/registry) + M2 (execution engine, BotRunner) — both merged to `main`.

---

## 1. Purpose & Scope

Deliver the visual editing experience of the Bot Builder: a WPF application with a drag-and-drop node-graph canvas, a searchable action palette, port-to-port connections, pan/zoom, and the Target Bar. This is the editor's *canvas*; the Properties Panel is M4.

Because a WPF UI cannot be verified headlessly the way M1/M2 were, the guiding constraint is: **all non-visual logic lives in a tested core; the WPF shell is thin and verified manually by the user running the app.**

### In scope (across the M3 slices)
- WPF app shell with the §5.1 layout (menu, Target Bar, palette, canvas, properties placeholder, status bar).
- Searchable action palette grouped by category, sourced from an `ActionRegistry` seeded with the M2 built-ins (Start, End, Log).
- Canvas: data-templated node cards (category-coloured header, label, input/output ports, target badge), add (palette double-click + drag-drop), move (drag), select (click + marquee).
- Port-to-port connections rendered as bezier curves, with validation (output→input only, no self, no duplicate, **no cycles** per §4.3 DAG rule); disconnect.
- Delete (nodes cascade their connections; connections individually).
- Undo/redo across all editing operations.
- Pan (middle-drag / Space+drag) and zoom (wheel, zoom-to-cursor).
- Target Bar: target chips, add/edit; node target badges.
- File: New / Open / Save `.bot` via `AdbCore.BotSerializer`.

### Out of scope (later milestones)
- Properties Panel config form (M4).
- Implementing additional action types (M5) — the palette shows only M2's built-ins for now.
- Test Run / target picker / BotCapture integration (M6, M8).
- `DrawingVisual`-based rendering (see §7 deviation).

---

## 2. Architecture & Projects

```
ADB.slnx
├── AdbCore/                 # (existing) models, serialization, registries, execution
├── BotRunner/               # (existing) console runner
├── BotBuilder.Core/         # NEW: view-models + editor logic — pure, fully tested
├── BotBuilder/              # NEW: WPF exe — XAML views + thin gesture code-behind
└── *.Tests/                 # AdbCore.Tests, BotRunner.Tests, BotBuilder.Core.Tests (NEW)
```

- **`BotBuilder.Core`** — class library, `net10.0-windows` (matches `AdbCore`) but **no `UseWPF`**, so it carries no WPF assemblies. References `AdbCore` and `CommunityToolkit.Mvvm`. `CommunityToolkit.Mvvm`'s `ObservableObject`/`RelayCommand` depend only on base-framework types (`INotifyPropertyChanged`, and `System.Windows.Input.ICommand`, which lives in the base `System.ObjectModel` assembly in modern .NET — not WPF). This keeps the core UI-framework-free and trivially unit-testable with no dispatcher/STA concerns.
- **`BotBuilder`** — WPF executable (`OutputType=WinExe`, `UseWPF=true`), `net10.0-windows`. References `BotBuilder.Core` + `AdbCore`. Contains `App.xaml`, `MainWindow.xaml`, data templates, value converters, and minimal code-behind for input gestures.
- **`BotBuilder.Core.Tests`** — xUnit, references `BotBuilder.Core`.

**Testability rule:** no editing/graph/geometry logic in the WPF project. Code-behind only translates raw input (mouse, keyboard, wheel) into calls on `BotBuilder.Core` commands/methods.

---

## 3. The Testable Core (`BotBuilder.Core`)

### 3.1 View-models
- **`BotEditorViewModel`** — root. Holds:
  - `ObservableCollection<NodeViewModel> Nodes`, `ObservableCollection<ConnectionViewModel> Connections`.
  - Selection state (selected nodes/connection).
  - `PaletteViewModel Palette`, `TargetBarViewModel TargetBar`.
  - An `UndoStack`.
  - Commands: `AddNode`, `MoveNode`, `DeleteSelection`, `Connect`, `Disconnect`, `Undo`, `Redo`, `New`, `Open`, `Save`.
  - Current document identity (file path, dirty flag, bot Id/Name/Description metadata).
- **`NodeViewModel`** — wraps a `BotAction`: `X`, `Y` (observable, for drag), `Label`, `TypeKey`, `Category`, `CategoryColor` (string/hex from a category→colour map), `IsSelected`, `IReadOnlyList<PortViewModel> InputPorts/OutputPorts`, `TargetBadge` (target name when the bot has >1 target).
- **`PortViewModel`** — `Name`, `Direction` (`In`/`Out`), back-reference to its node; exposes a relative anchor offset used by the view/geometry to position the port and route connections.
- **`ConnectionViewModel`** — references source node+output port and target node+input port; exposes the four bezier points (start, two controls, end) computed from the connected ports' anchors.
- **`PaletteViewModel`** — built from an `ActionRegistry` (seeded via `AdbCore.Actions.BuiltIn.BuiltInActions`); exposes categories each holding their `IActionDefinition`s, plus `SearchText` and a filtered view (case-insensitive match on display name/category).
- **`TargetBarViewModel`** / **`TargetViewModel`** — wraps the bot's `BotTarget`s; `AddTarget`, edit name/type/selector.

### 3.2 Editing as undoable commands
- `IUndoableCommand { void Do(); void Undo(); }`.
- `UndoStack` — `Push/Do`, `Undo`, `Redo`, `CanUndo`, `CanRedo`; redo cleared on a new operation.
- Operations: `AddNodeCommand`, `MoveNodeCommand` (records old/new position), `DeleteCommand` (nodes + their incident connections, or a lone connection), `ConnectCommand`, `DisconnectCommand`.

### 3.3 Validation & geometry (pure functions)
- **Connection validation**: reject output→output / input→input, self-connection, duplicate edges, and any edge that would introduce a **cycle** (reachability check over existing connections). Returns a typed result (ok / reason).
- **Bezier geometry**: given two port anchor points, produce control points for a smooth horizontal cubic curve.
- **Marquee hit-testing**: given a rectangle, return the nodes whose bounds intersect it.
- **Zoom/pan math**: zoom-to-cursor transform (scale about a point), pan translation, clamping.

### 3.4 Document mapping
- **`DocumentMapper`** — `Bot → BotEditorViewModel` (build VMs from a loaded bot) and `BotEditorViewModel → Bot` (assemble a `Bot` for save). File I/O delegates to `AdbCore.BotSerializer`. Round-trip (load→edit-free→save) must reproduce an equivalent `.bot`.

---

## 4. The WPF Shell (`BotBuilder`)

- **`MainWindow`** implements the §5.1 layout: menu bar (File: New/Open/Save; Edit: Undo/Redo/Delete), Target Bar row, left palette, central canvas, right **properties placeholder** (empty panel labelled for M4), status bar.
- **Canvas**: an `ItemsControl` bound to `Nodes` with a `Canvas` `ItemsPanel`; an `ItemContainerStyle` binds `Canvas.Left`/`Canvas.Top` to `NodeViewModel.X`/`Y`. Node cards are a `DataTemplate` (coloured header, label, ports on left/right edges, optional target badge). Connections are a second `ItemsControl` overlaid behind/over the nodes, bound to `Connections`, each item a bezier `Path` built from the connection's points.
- **Gestures** (code-behind, each delegating to a core command/method):
  - Drag a node → `MoveNode` (single undoable op on mouse-up).
  - Drag from an output port to an input port → `Connect` (validated).
  - Click → select; empty-area drag → marquee multi-select.
  - `Delete` → `DeleteSelection`; `Ctrl+Z`/`Ctrl+Y` → `Undo`/`Redo`.
  - Mouse wheel → zoom-to-cursor; middle-drag / Space+drag → pan (via `ScaleTransform` + `TranslateTransform` on the canvas).
  - Palette double-click → `AddNode` at canvas centre; palette drag-drop onto canvas → `AddNode` at the drop point.

---

## 5. Slice Breakdown (one PR per slice)

- **M3a (PR #3)** — Projects + shell + layout. Palette (grouped + search). Canvas renders node cards (ports, category colour, target badge). Add node (double-click + drag-drop). Move (drag). Click-select. New/Open/Save `.bot`.
  *Tested:* `BotEditorViewModel`, `NodeViewModel`, `PortViewModel`, `PaletteViewModel` (filter/grouping), `AddNodeCommand`, `MoveNodeCommand`, selection, `DocumentMapper` round-trip.
- **M3b (PR #4)** — Port-to-port connections + validation (DAG/direction/no-dup), connection rendering, disconnect, node delete (cascading), undo/redo across all operations.
  *Tested:* `ConnectCommand`, `DisconnectCommand`, `DeleteCommand`, connection validation incl. cycle detection, `UndoStack` over every op, bezier geometry.
- **M3c (PR #5)** — Pan + zoom-to-cursor (transform math), marquee multi-select, full Target Bar (chips/add/edit) + multi-target badges, polish.
  *Tested:* zoom/pan math, marquee hit-testing, `TargetBarViewModel`, target-assignment logic.

---

## 6. Testing Strategy

- **Automated (subagent TDD loop):** 100% of `BotBuilder.Core` — view-models, commands, undo/redo, validation, geometry, document mapping — via `BotBuilder.Core.Tests` (xUnit). Each slice's core logic is red-green-tested before its shell is wired.
- **Manual (user):** the XAML views and gesture code-behind are verified by the user running `BotBuilder.exe` at the end of each slice's PR (look, drag/connect/zoom feel, palette, save/reload). Code-behind is kept thin and delegating so almost no logic escapes the automated tests.

---

## 7. Key Decisions & Deviations

| Decision | Choice | Rationale |
|---|---|---|
| Architecture | Testable `BotBuilder.Core` + thin WPF shell | UI can't be verified headlessly; keep logic under the automated test loop |
| MVVM plumbing | `CommunityToolkit.Mvvm` | Source-generated `ObservableObject`/`RelayCommand`; keeps core WPF-free and test-friendly |
| Canvas rendering | `ItemsControl` + `Canvas` panel + data templates + bezier `Path`s | MVVM-friendly and testable; adequate for target bot sizes. **Deviates from `Docs/Design/V1.md` §8** (`DrawingVisual`), which is deferred as a future optimisation if a real perf need appears |
| Delivery | Three incremental PRs (M3a/b/c) | Early, frequent visual checkpoints for the user-as-verifier |
| Palette contents | M2 built-ins only (Start/End/Log) | Additional actions are M5; palette is registry-driven so it grows automatically |

---

## 8. Risks

- **Gesture logic creeping into code-behind** (untested). Mitigation: keep code-behind to input translation only; push any decision/computation into tested core methods.
- **Connection cycle-detection correctness** — important to the DAG invariant; covered by explicit unit tests in M3b.
- **`DataTemplate`/binding perf** at very large graphs — acceptable for M3; `DrawingVisual` remains the escape hatch.
