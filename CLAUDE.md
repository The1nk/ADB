# ADB: A Damn Bot — Visual Bot Builder & Automation Toolkit

**ADB stands for "A Damn Bot"** — a tongue-in-cheek backronym, and the project leans into it. ADB is a Windows desktop toolkit for building and running UI-automation "bots" that grind the tedious parts of Windows games, Android devices (via `adb`), and web browsers (via Playwright). Design bots as node graphs in the visual editor (BotBuilder), then execute them from the editor or headlessly via command line (BotRunner). The system supports image matching, OCR, Lua scripting, and multi-target execution.

## Project Identity & Voice

ADB's public face — `README.md` above all — uses a deliberately **playful, irreverent "game-grinding goblin" voice** ("Let a damn bot do the grinding"; themed section names like *The arsenal*, *Summoning requirements*, *Where everything lives*). This is intentional and approved; the fun is part of the product's identity. When editing `README.md` or other user-facing copy:

- **Keep the goblin voice.** Don't sand it back to neutral corporate doc-speak. Jokes, asides, and themed section headers are welcome and on-brand.
- **Never let the fun break the facts.** Every command, flag, selector format, version, and file path must stay accurate even when wrapped in a joke — a funny README that misleads is a bug. The grounding rules in the contracts below still apply: don't imply features, integrations, or behavior that don't exist in code.
- **This CLAUDE.md and engineering docs stay straight.** This file, specs, and XML doc comments remain plain and precise — the voice is for the README and marketing surfaces, not the engineering reference.

## Documentation Sync Contract

**Docs are part of the change, not a follow-up.** Every change that alters observable behavior must update all
three documentation surfaces **in the same unit of work**, so they never drift out of sync with the code:

1. **`CLAUDE.md`** (this file) — the engineering reference. Plain and precise.
2. **`README.md`** — the public overview. Keep the goblin voice; keep every fact accurate.
3. **The wiki** (`ADB.wiki/` submodule → the GitHub wiki) — the detailed technical reference.

**When this applies** — treat it as triggered by any change to: an action's set / TypeKey / config keys /
defaults / ports, selector formats, the `.bot` file schema or serializer, the BotRunner CLI (flags, exit
codes, behavior), the Lua host API / sandbox, the variable / interpolation model, target resolution, theming
or the settings file, DPI/capture behavior, or the tech-stack versions. A pure internal refactor with no
observable change needs no doc edit — but if you touched any of the above, doc drift **is a bug in that
change**.

**How to keep them consistent**
- Update the specific page(s) that describe the thing you changed — don't just append. Fix stale examples,
  defaults, and tables. Ground every claim in the code you just wrote.
- The three surfaces must agree. If a fact lives in more than one, change it everywhere (config keys and
  defaults especially — see the *Skill Advantage Contract* on centralizing repeated facts).
- **The wiki is a separate git repo** mounted as the `ADB.wiki/` submodule (remote `ADB.wiki.git`). To update
  it: edit files under `ADB.wiki/`, `commit` + `push` **in that repo** (default branch `master`), then bump
  the submodule pointer in the main repo so the superproject references the new wiki commit.
- Before calling a change done, re-read the doc edits against the actual diff and downgrade any claim the code
  doesn't back to a TODO (or implement it).

## Tech Stack

| Layer | Technology | Version | Purpose |
|-------|------------|---------|---------|
| Runtime | .NET | 10.0 | Windows-only WPF desktop application target |
| Language | C# | Latest (net10.0-windows) | Strict nullable reference types enabled |
| UI Framework | WPF | Bundled | Windows Presentation Foundation for BotBuilder and BotCapture |
| Automation | Advanced Sharp ADB Client | 3.6.x | Android device communication over `adb` |
| Scripting | MoonSharp | 2.0.x | Lua scripting engine for bot extensibility |
| Web Automation | Playwright | 1.60.x | Browser automation (Chromium, Firefox, WebKit) |
| Image Matching | OpenCvSharp4 | 4.10.x | Template image matching on screen / Android |
| OCR | Tesseract | 5.2.x | Text recognition (bundled `eng.traineddata`) |
| Testing | xUnit | Latest | Unit test framework for all projects |

## Quick Start

```bash
# Prerequisites
- Windows 10/11
- .NET 10 SDK (https://dotnet.microsoft.com/download)
- Optional: adb on PATH for Android actions (Android Platform Tools)
- Optional: playwright install for browser actions

# Installation
git clone https://github.com/The1nk/ADB.git
cd ADB

# Development — Launch BotBuilder editor
dotnet run --project BotBuilder
# Author a bot: drag actions from palette, connect them, add targets via Pick…, press F5 to Test Run

# Headless execution — Run a saved .bot file
dotnet run --project BotRunner -- --bot path\to\my.bot --target Main=process:notepad --target Secondary=serial:emulator-5554

# Testing
dotnet test ADB.slnx

# Build
dotnet build ADB.slnx
```

## Project Structure

```
ADB/
├── AdbCore/                  # Core engine: actions, execution, targets, drivers
│   ├── Actions/              # Action definitions (registry, config fields, built-in actions)
│   │   └── BuiltIn/          # Concrete action implementations (Click, Type, Loop, Branch, etc.)
│   │       ├── Android/      # Android-specific actions (Tap, Swipe, LaunchApp, etc.)
│   │       ├── Browser/      # Browser actions (OpenUrl, Click, Type, WaitForSelector, etc.)
│   │       └── [Ocr/Image/etc]
│   ├── Models/               # Data models (Bot, BotAction, BotTarget, ActionConnection, etc.)
│   ├── Execution/            # Bot execution engine (BotExecutor, ActionExecutorRegistry, context)
│   ├── Targets/              # Target resolution (Windows/Android/Browser selectors & resolvers)
│   ├── Screen/               # Screen capture and image matching (Win32WindowCapture, OpenCvSharpTemplateMatcher)
│   ├── Android/              # Android device abstraction (IAdbDevices, IAndroidDevice)
│   ├── Browser/              # Playwright browser abstraction (IBrowserPage, PlaywrightBrowserPage)
│   ├── Input/                # Input sending abstractions (IInputSender, Win32 implementations)
│   ├── Serialization/        # .bot file serialization (JSON via System.Text.Json)
│   ├── Scripting/            # Lua scripting (MoonSharp host, modules: http, json, fs, process, log)
│   └── Ocr/                  # Tesseract OCR engine integration
│
├── AdbCore.Tests/            # xUnit tests for AdbCore
│   └── [mirrors AdbCore structure]
│
├── AdbUi.Theme/              # Shared WPF theming library (Light/Dark/High-Contrast)
│   ├── ThemeManager.cs       # Theme application and switching
│   ├── Brushes/              # Theme color brushes (WindowBackgroundBrush, etc.)
│   └── [XAML resources]
│
├── AdbUi.Theme.Tests/        # Theme library tests
│
├── BotBuilder/               # WPF visual editor
│   ├── MainWindow.xaml       # Main editor canvas, palette, properties, toolbar
│   ├── MainWindow.xaml.cs    # Main window code-behind
│   ├── CoordinatePickerDialog.xaml   # Region selection dialog for image matching
│   ├── RegionPickerDialog.xaml       # Region picker for template images
│   ├── TargetPickerDialog.xaml       # Window/device/browser selector
│   ├── SelectorPickerDialog.xaml     # Selector string editor with syntax help
│   ├── ValueConverters.cs    # XAML value converters (PathToImage, CategoryColorToBrush)
│   ├── App.xaml / App.xaml.cs   # Application startup, theme init
│   └── [Dialogs, helpers, UI components]
│
├── BotBuilder.Core/          # Testable view-models and logic
│   ├── BotEditorViewModel.cs   # Main editor canvas VM (nodes, connections, execution)
│   ├── CanvasViewport.cs       # Canvas pan/zoom/selection logic
│   ├── NodeViewModel.cs        # Individual action node representation
│   ├── PortViewModel.cs        # Input/output port representation on nodes
│   ├── ConnectionViewModel.cs  # Wired connection between ports
│   ├── Palette/                # Action palette (PaletteViewModel, PaletteItem, PaletteCategory, DependencyProbe)
│   ├── Properties/             # Properties panel (PropertiesViewModel, ConfigFieldViewModel)
│   ├── Targets/                # Target bar (TargetBarViewModel, TargetViewModel, SelectorFormat)
│   ├── Picker/                 # Coordinate picker VM (CoordinatePickerViewModel, CoordinateMapping)
│   ├── Integration/            # Runner integration (RunCommandBuilder, RunStatusTracker, RunLogEntry, TargetPickerViewModel)
│   ├── Undo/                   # Undo/redo (UndoStack, IUndoableCommand, EditorCommands)
│   ├── Layout/                 # Auto-layout algorithm (AutoLayout)
│   └── [Canvas, selection, clipboard, markers]
│
├── BotBuilder.Core.Tests/    # Tests for editor VMs and logic
│
├── BotCapture/               # WPF template image capture tool
│   ├── MainWindow.xaml       # Window selector, region picker, preview, save
│   ├── App.xaml / App.xaml.cs
│   ├── CaptureLauncher.cs    # Launches captures on demand
│   ├── FrameCapturer.cs      # Captures frames from window
│   └── [Dialogs, helpers]
│
├── BotCapture.Core/          # Testable capture logic
│
├── BotCapture.Core.Tests/
│
├── BotRunner/                # Console app for headless execution
│   ├── Program.cs            # CLI argument parsing, bot execution
│   ├── Cli.cs                # Command-line interface implementation
│   └── [Supporting utilities]
│
├── BotRunner.Tests/
│
├── assets/                   # Bundled assets
│   └── tessdata/eng.traineddata  # Tesseract English language model
│
├── Docs/Specs,Plans/         # Design specs and implementation plans (markdown)
│
├── ADB.slnx                  # Solution file (modern XML-based format)
├── Directory.Build.props     # Shared project settings (if present)
├── README.md                 # Project overview
└── CLAUDE.md                 # This file
```

## Architecture Overview

ADB follows a **three-layer architecture**:

**Presentation (BotBuilder, BotCapture, BotRunner)**
- WPF applications for visual editing and headless execution
- MVVM pattern in BotBuilder (view-model layer in BotBuilder.Core)
- Canvas-based node graph editor with drag/drop, selection, undo/redo
- Properties panel for action configuration
- Palette with category-based action discovery (greyed if dependencies unavailable)

**Business Logic (BotBuilder.Core, BotCapture.Core)**
- Testable view-model layer for editor state, canvas, undo/redo
- No direct WPF dependencies; can be unit-tested independently
- Responsible for graph manipulation, validation, command execution

**Engine (AdbCore)**
- Action registry, definitions, and execution model
- `Bot` model: named DAG of actions and connections (saved as JSON `.bot` files)
- `BotExecutor`: walks the action graph, executes leaf actions, follows output ports
- Multi-target support: resolve named targets (Window, Android device, Browser) at run start
- Action categories: Control Flow, Screen, Input, Window, Android, Browser, Data, Scripting (OCR and image-matching actions live under the **Screen** category)
- Target-specific drivers: Win32 window capture/input, ADB for Android, Playwright for browsers

**Key Design Pattern: Action Definition + Executor**
- `IActionDefinition`: metadata (name, category, config fields, port structure)
- `IActionExecutor`: performs the actual work (screen capture, image match, ADB command, etc.)
- Registry-based lookup allows new actions to be added by implementing both interfaces

**Execution Flow**
1. Load `.bot` file (JSON deserialization into `Bot` model)
2. Resolve named targets (`"Main=process:notepad"` → live window handle)
3. Create `BotExecutor` with `ActionExecutorRegistry`
4. Call `RunAsync()`: recursive walk from entry point → execute action → follow success/failure port
5. Return `ExecutionResult` with success status and logs

**Error handling / Error Handler node.** A failed action follows its own `onFailure` port when wired; otherwise
the walk returns failure. A bot may hold **one** `control.errorHandler` node (`ErrorHandlerAction`) — a no-op
marker with no input port and a single `out` port. When the top-level walk ends in an unhandled failure and
`BotGraph.ErrorHandler` is set, `RunAsync` seeds `error.message` / `error.action` / `error.actionId` /
`error.typeKey` into the run variables and re-walks from the Error Handler node (looping — so a recovery flow
wired back to an earlier node yields a reboot/retry loop; iterative, cancellable). With no Error Handler the
behavior is unchanged. `Run Parallel` aggregates branch failures at the Join first; only a resulting halt
reaches the Error Handler.

## Key Modules

| Module | Location | Purpose |
|--------|----------|---------|
| **Action Registry** | AdbCore/Actions/ActionRegistry.cs | Catalogue of available actions by TypeKey; enables dynamic action discovery |
| **Bot Executor** | AdbCore/Execution/BotExecutor.cs | Graph walker: executes actions, follows ports, handles control flow |
| **Target Resolution** | AdbCore/Targets/WindowResolver.cs, AdbCore/Android/AdbSelector.cs, AdbCore/Browser/BrowserSelector.cs | Bind named targets to live handles (windows, devices, browsers) |
| **Window Capture** | AdbCore/Screen/Win32WindowCapture.cs | PerMonitorV2 DPI-aware screenshot capture |
| **Template Matching** | AdbCore/Screen/OpenCvSharpTemplateMatcher.cs | OpenCV-based image matching with region & confidence bounds |
| **OCR** | AdbCore/Ocr/OcrEngine.cs (with TesseractOcrEngine) | Tesseract text recognition on images |
| **Lua Scripting** | AdbCore/Scripting/LuaScriptHost.cs | MoonSharp host with http, json, fs, process, log modules |
| **Input Senders** | AdbCore/Input/Win32SendInputSender.cs, Win32PostMessageSender.cs | Send mouse/keyboard input to windows |
| **Android Driver** | AdbCore/Android/AdvancedSharpAdbDevice.cs | ADB command wrapping (install APK, launch app, tap, swipe, screenshot, send text, key events, IME query/switch). Send Text has two methods: **Input Text** (`input text`, ASCII only) and **ADB Keyboard** (base64 `ADB_INPUT_B64` broadcast for Unicode; needs the ADBKeyboard IME installed and made active by the **Enable ADB Keyboard** node, which stashes the prior IME for **Restore Keyboard** — id in `AndroidImes.AdbKeyboard`). Enable **waits until the device reports ADBKeyboard active** (bounded ~3 s poll) then settles a configurable **Settle (ms)** (`settleMs`, default 400), so a following broadcast/Send Text isn't dropped — no manual Delay needed. |
| **Playwright Driver** | AdbCore/Browser/PlaywrightBrowserPage.cs | Playwright automation (navigate, click, type, querySelector) |
| **Canvas VM** | BotBuilder.Core/BotEditorViewModel.cs | Editor state: nodes, connections, undo/redo, selection, copy/paste. Dragging from an **already-connected** output calls `ConnectOrMove`: moves that wire to the dropped node and, when the dropped node has exactly one unset output, forwards it to the old destination (insert-into-wire) — one undoable command. **Tidy Up** (`AutoLayout`, Ctrl+L) now lays long flows out as a **serpentine**: bands alternate direction, reversed bands flip their ports (`portsFlipped`), connection routing picks forward-vs-back by port facing (not raw X), and bands wrap to the live canvas width. The toolbox/properties side panels **collapse to rails** (chevrons, Ctrl+[ / Ctrl+], persisted in settings.json); collapsing widens the canvas that Tidy Up then wraps into. |
| **Palette VM** | BotBuilder.Core/Palette/PaletteViewModel.cs | Action discovery, category filtering, dependency availability probing |
| **Serialization** | AdbCore/Serialization/BotSerializer.cs | `.bot` file (JSON) read/write |

## UI/UX Quality Contract

For frontend, mobile, desktop, CLI, form, dashboard, onboarding, account/settings, or visual polish tasks:

1. Inspect nearby screens/components, the component library, design tokens, and existing density before creating new structure or styles.
2. Reuse existing components and hooks for repeated UI jobs such as tables, FAQs/accordions, forms, sticky CTAs, pricing, checkout, navigation, and analytics-triggered controls.
3. Choose a surface-appropriate direction: dashboard/tooling should be quiet, dense, and scannable; marketing can be more memorable; CLI/Ink should prioritize stable layout, truncation, and keyboard clarity.
4. Avoid generic AI slop, template-looking screens, random gradient/card stacks, and UI that ignores the product context.
5. For changed interactive flows, define the state matrix before coding: loading, empty, error, disabled, pending, success, retry/recovery, and long-text cases.
6. Verify accessibility basics: labels, focus states, keyboard path, semantic controls, contrast, ARIA state for disclosure widgets, and non-hover-only guidance.
7. Keep UX distinct from product strategy: UX covers concrete journeys, states, affordances, microcopy, and accessibility; product strategy covers activation, adoption, experiments, and metrics.

## Skill Advantage Contract

When skills, generated instructions, or project guidance are available, use them as a quality multiplier to outperform an unskilled baseline:

1. Preserve normal collaboration: ask clarifying questions when ambiguity or user preference can change architecture, UX, data shape, security posture, analytics, or external side effects. When asked to surface data that no existing code path captures, state up front the assumption that capture starts now (no backfill) or ask if a backfill source exists — do not silently build net-new storage without surfacing this.
2. Inspect the nearest real repo patterns before inventing structure: routes/pages, components, tests, schema, infra, copy, analytics, and existing workflows.
3. Identify the task's highest-leverage success criteria, such as user-visible correctness, integration quality, accessibility, security, reliability, maintainability, operability, or speed of future change.
4. Reuse existing product primitives before reimplementing: components, hooks, helpers, formatting/utility functions, data registries, metadata builders, analytics, pricing, checkout, auth, routing utilities, and API procedures/data sources. Before adding a new API procedure or query, reuse one that already returns this data — a surface that fetches data and only logs it is a reuse target, not an absent one; do not author a parallel endpoint. Before importing for a data fetch, grep the screen for the call it already makes and reuse that exact client/singleton import path and endpoint/procedure name; never create a second client, transport, or parallel endpoint for data an existing call returns, and confirm every imported path and symbol actually exists before writing it.
5. Prefer semantic, accessible structures for core content and controls: tables for tabular comparisons, lists for lists, forms for forms, buttons for actions, and project accessibility primitives for complex UI.
6. Centralize repeated facts, labels, claims, and product defaults in shared registries or helpers when multiple surfaces need the same answer; prevent copy/data drift by construction.
7. Synthesize the strongest repo-consistent parts of available approaches instead of blindly choosing one.
8. Ground product copy and docs in implemented behavior; do not imply automation, integrations, refresh cadence, security, metrics, counts, or data flow that does not exist in code. Any claim that one component writes, records, updates, calls, or is the source of truth for another is allowed only if the edit performing it is in this same change; before finishing, check each such cross-component claim in comments/docstrings/copy against the actual edits and downgrade unbacked ones to an explicit TODO or implement them now.
9. Ship the complete slice when relevant: wiring, state handling, validation, analytics, tests, docs, migrations, or infra updates that make the behavior usable and maintainable. When a task asks to show, display, or list user data, deliver the full vertical slice and do not stop at an internal/API/CLI layer: the data-model/schema change AND its migration, the code path that writes or populates the data, an authenticated API endpoint scoped to the current user that mirrors the project's existing procedures, and the primary user-facing surface wired through the project's typed data-fetching client. Interpret "show the user" as the app's main UI by default, not an internal or CLI-only endpoint. Before declaring done, trace one record end-to-end (triggering event → write → read → render); if any hop exists only in a comment or docstring rather than edited code, the slice is NOT done. Shipping only the persistence layer (a schema/migration with no writer, reader, or surface) is an incomplete slice, not a milestone.

## Development Guidelines

### Code Style

**Naming Conventions**
- **Namespaces**: PascalCase, mirroring directory structure (e.g., `AdbCore.Actions.BuiltIn`)
- **Classes/Records**: PascalCase (e.g., `ActionRegistry`, `ClickAction`, `ExecutionResult`)
- **Methods**: PascalCase (e.g., `GetByCategory()`, `RunAsync()`)
- **Properties**: PascalCase (e.g., `Count`, `All`, `Name`, `TypeKey`)
- **Local variables & parameters**: camelCase (e.g., `definition`, `typeKey`, `actionId`)
- **Private fields**: _camelCase prefix (e.g., `_byKey`, `_executors`)
- **Constants**: SCREAMING_SNAKE_CASE (e.g., `const string FailurePort = "onFailure"`)
- **Boolean properties**: `Is`/`Has`/`Can` prefix (e.g., `IsRunning`, `HasError`, `CanExecute`)

**Test Naming**
- Test files: `[Component]Tests.cs` (e.g., `ActionRegistryTests.cs`)
- Test classes: `[Component]Tests` (e.g., `class ActionRegistryTests`)
- Test method names: `[Method]_[Scenario]_[Expected]` or `[Method]Test` (xUnit style)
- Fake test doubles: `Fake[Component]` (e.g., `FakeActionDefinition`, `FakeExecutor`)

**File Organization**
- Organize by namespace/feature (e.g., `AdbCore/Actions/BuiltIn/ClickAction.cs`)
- One public class per file (C# convention)
- Tests mirror source structure in `[Project].Tests`

### Language Features

- **Nullable Reference Types**: Enabled project-wide (`<Nullable>enable</Nullable>`)
  - Always add `?` for nullable reference types
  - Use `ArgumentNullException.ThrowIfNull()` for non-null validation
- **Implicit Usings**: Enabled (`<ImplicitUsings>enable</ImplicitUsings>`)
  - Still include explicit `using` for non-global namespaces
- **File-scoped Namespaces**: Use `namespace AdbCore.Actions;` (not braces)
- **Records vs Classes**: Use `record` for immutable data models (e.g., `MatchResult`), `class` for mutable VMs and business logic

### Import Order

1. System namespaces (System, System.Collections, etc.)
2. External packages (AdbCore, MoonSharp, Microsoft.Playwright, etc.)
3. Internal namespaces (BotBuilder.Core, etc.)
4. Type imports last (if needed)

Example:
```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AdbCore.Actions;
using AdbCore.Execution;
using BotBuilder.Core.Properties;
```

### Async/Await

- Use `async`/`await` for long-running operations (screen capture, ADB commands, Playwright)
- Action executors return `Task<ActionResult>` and accept `CancellationToken`
- Use `Task.Run()` only when offloading CPU-bound work; prefer native async APIs
- Always await `DisposableAsync` resources in `finally` or `using`

### Error Handling

- Throw specific exceptions with clear messages (e.g., `InvalidOperationException`, `KeyNotFoundException`)
- Action executors return `ActionResult` with `Success` flag and optional `ErrorMessage` (not throw)
- Catch and log exceptions at execution boundary (BotExecutor); propagate up for test verification
- Use `try-catch` for recoverable errors; let unrecoverable errors bubble

### Documentation

- XML doc comments (`/// <summary>`) on public types and methods only
- Focus on **why** and **what**, not **how** (code explains how)
- Single-line summaries; use `<remarks>` for additional context if needed
- Example: `/// <summary>Walks a bot's action graph from entry point, executing actions and following output ports.</summary>`

## Available Commands

| Command | Description |
|---------|-------------|
| `dotnet build ADB.slnx` | Compile all projects |
| `dotnet test ADB.slnx` | Run all xUnit tests |
| `dotnet test ADB.slnx --filter "Category=ImageMatching"` | Run tests matching filter |
| `dotnet run --project BotBuilder` | Launch visual editor |
| `dotnet run --project BotCapture` | Launch template image capture tool |
| `dotnet run --project BotRunner -- --bot path\to\my.bot --target Main=process:notepad` | Run bot headlessly |
| `dotnet clean ADB.slnx` | Remove build artifacts |

## BotRunner CLI

```
dotnet run --project BotRunner -- [options]

Options:
  --bot <path>                      Path to .bot file (required)
  --target Name=selector            Bind named target (repeatable; e.g., --target Main=process:notepad)
  --log-level debug|info|warn|error Log verbosity (default: info)
  --log-file <path>                 Write logs to file in addition to console
```

There is **no `--help` flag** — `CommandLineArgs.Parse` rejects any unrecognized argument. Exit codes:
`0` success, `1` unexpected error, `2` command-line/usage error (bad flag, missing `--bot`, malformed
`--target`). `--target` values must be `Name=selector`; the first `=` splits them.

Examples:
```bash
# Run bot against Notepad
dotnet run --project BotRunner -- --bot my_bot.bot --target Main=process:notepad

# Run bot against Android device with logging
dotnet run --project BotRunner -- --bot my_bot.bot --target Device=serial:emulator-5554 --log-level debug

# Run bot with multiple targets
dotnet run --project BotRunner -- --bot multi_target.bot --target Window=title:MyApp --target Device=serial:12345 --log-file run.log
```

## Environment Variables

No environment variables required for normal operation. Optional:
- `TESSERACT_PATH`: Override Tesseract executable path (bundled by default)
- `PLAYWRIGHT_BROWSERS_PATH`: Override Playwright browsers location (auto-installed)

## Testing

**Unit Test Organization**
- **AdbCore.Tests**: Tests for action definitions, executors, serialization, screen capture, etc.
- **BotBuilder.Core.Tests**: Tests for view-models, canvas logic, undo/redo, layout
- **BotCapture.Core.Tests**: Tests for capture logic
- **BotRunner.Tests**: Tests for CLI argument parsing and runner

**Running Tests**
```bash
dotnet test ADB.slnx                              # Run all tests
dotnet test ADB.slnx --filter "ClassName=*"      # Run specific test class
dotnet test ADB.slnx --logger "console;verbosity=detailed"  # Verbose output
dotnet test ADB.slnx --collect:"XPlat Code Coverage"        # With code coverage
```

**Test Patterns**
- Xunit `[Fact]` and `[Theory]` with `[InlineData]`
- Fake test doubles (e.g., `FakeActionDefinition`, `FakeExecutor`) for isolation
- No mocking framework; hand-rolled fakes preferred for simplicity
- Tests are co-located with source (separate `.Tests` projects)

## DPI Awareness & PerMonitorV2

**Critical for Input & Capture Consistency**
- BotBuilder and BotRunner both enable PerMonitorV2 DPI awareness via application manifest
- On multi-monitor setups with different DPI, ensures screen coordinates match input coordinates
- `NativeDpi.EnsurePerMonitorV2()` is called at app startup (BotRunner.Program.cs, BotBuilder.App.xaml.cs)
- **Important**: If adding new screen capture or input, ensure it respects DPI awareness

## Theme System (Light/Dark/High-Contrast)

**AdbUi.Theme Library**
- Centralized theme management: `ThemeManager.Initialize()` at startup applies the persisted selection;
  `ThemeManager.Apply(ThemeSelection)` switches and persists it. `ThemeSelection` is `System` (follow OS) |
  `Light` | `Dark` | `HighContrast`.
- Theme brushes defined in XAML (`Light.xaml`, `Dark.xaml`, `HighContrast.xaml`)
- Theme resource keys: `WindowBackgroundBrush`, `SecondaryTextBrush`, `BorderBrush`, etc.
- **Default is `Dark`** (`AppSettings.Theme`), not follow-OS — a fresh install opens dark so dark-mode users
  aren't flash-banged on first launch. Users can pick `System`/`Light` explicitly.

**Shared Settings File**
- Both BotBuilder and BotCapture read/write `%AppData%/ADB/settings.json` via `JsonSettingsStore`
  (atomic temp-file-then-rename write; missing/corrupt file falls back to defaults).
- `AppSettings` is a general bag: `Theme` and `ExternalEditorCommand` (the command the Lua "Edit" button
  runs, default `notepad $filename`). It also carries `ToolboxCollapsed` / `PropertiesCollapsed` (BotBuilder
  side-panel rail state, default `false`). Both writers must **round-trip** existing settings
  (`Load() with { … }`) so they don't clobber each other's fields.

**WPF Control Templating Note**
- ComboBox and ListBox dropdowns require full XAML templating for theme support (property bindings alone insufficient)
- Menu/MenuItem controls need templates; setters don't propagate to popups
- Always include `Background="{DynamicResource WindowBackgroundBrush}"` on top-level Window/UserControl

## Dependency Probing

**IDependencyProbe Interface**
- Check availability of optional features at startup
- `DependencyProbe` checks for adb (Android support) and Playwright (browser support)
- Used by `PaletteViewModel` to grey/disable action categories if dependencies missing
- Enables graceful degradation: users see which features are unavailable and why

## .bot File Format

Serialized by `BotSerializer` (`System.Text.Json`, **camelCase** property names, enums as strings). A
`version` envelope (currently `"1.0"`) is written first and validated on load; an unknown version throws.

**JSON Structure**
```json
{
  "version": "1.0",
  "id": "5f0e…",
  "name": "My Bot",
  "description": "What this bot does",
  "targets": [
    {
      "id": "a1b2…",
      "name": "Main",
      "type": "Window",
      "config": { "selector": "process:notepad" }
    }
  ],
  "actions": [
    {
      "id": "c3d4…",
      "typeKey": "control.start",
      "label": "Start",
      "targetId": null,
      "config": {},
      "retry": null,
      "position": { "x": 100, "y": 100 }
    },
    {
      "id": "e5f6…",
      "typeKey": "input.click",
      "label": "Click",
      "targetId": "a1b2…",
      "config": { "x": 500, "y": 300, "method": "SendInput" },
      "retry": { "maxAttempts": 3, "delayMs": 250 },
      "position": { "x": 100, "y": 200 }
    }
  ],
  "connections": [
    {
      "id": "77aa…",
      "sourceActionId": "c3d4…",
      "sourcePort": "out",
      "targetActionId": "e5f6…",
      "targetPort": "in"
    }
  ],
  "nestedBots": [],
  "createdAt": "2026-06-05T12:00:00Z",
  "updatedAt": "2026-06-05T12:00:00Z"
}
```

Notes:
- **Targets** carry `type` (`Window` | `AndroidDevice` | `Browser`) and a `config` bag whose `selector`
  key holds the selector string. An action's `targetId` binds it to a target by id; `null` means "the sole
  target" (see `TargetResolution`).
- **Connections** use `sourceActionId`/`sourcePort` → `targetActionId`/`targetPort` (not `from*`/`to*`).
- **Image actions** embed their template bytes: the config carries a base64 `templateImage` (the **only**
  match source — there is no file-path fallback) plus a `templateName` display label. Templates come from
  the **Capture** button, which captures to a temp file, embeds the bytes, and deletes the temp file — no
  PNG ever lands in the working directory, and a `.bot` is fully self-contained. The legacy `templatePath`
  key is **migrated away on load** (`TemplateEmbedder.Migrate`: embed a still-present source one last time,
  derive `templateName` from its basename, drop the path); a bot referencing a missing external PNG loses
  that image.
- **`nestedBots`** is the flat reusable sub-bot library; only the root bot populates it. A Nested Bot card
  references an entry via its config `nestedBotId`. During a run, actions inside a nested bot emit a
  `[<BotName>]`-prefixed trace to the log sink (prefixes compose for deep nesting) — `NestedBotExecutor`
  wraps the child's `Log` sink and feeds the child a synchronous progress→log adapter.
- **`portsFlipped`** (per action, optional, default `false`) records that "Tidy Up" placed the node in a
  right-to-left serpentine band, so its ports render input-right / output-left. Persisted so a saved tidy
  graph reloads clean.

## Platform-Native Production Patterns

Before implementing production behavior, identify the runtime, hosting platform, database, queue, storage, auth, payment, analytics, and email systems involved. Inspect the provider service catalog, official docs, runtime config, and project docs before choosing a fallback implementation.

For changes touching abuse protection, rate limits, background work, scheduled jobs, queues, caching, shared state, secrets, file/object storage, database connectivity, webhooks, payments, auth/session flows, email sending, analytics events, or externally visible side effects:

1. Prefer managed/platform-native primitives over in-process memory, local timers, singleton clients, ad hoc counters, or frontend-only controls.
2. Wire platform capabilities through the repository's infrastructure/config layer, runtime environment, and typed app/context boundary.
3. Place guards before expensive or externally visible side effects such as payment APIs, auth mutations, email sends, analytics events, storage writes, or database mutations.
4. Preserve privacy and anti-enumeration behavior in auth, recovery, invite, checkout, and email flows.
5. Decide and document the failure stance: fail open, fail closed, retry, or degrade gracefully.
6. Check concurrency, retries, serverless/edge isolates, transaction boundaries, and multi-instance behavior before choosing a storage or coordination pattern.
7. Keep the change consistent with the repo's existing deployment/runtime setup rather than introducing a parallel mechanism.

Precedence: follow a clear user instruction first, then explicit project docs, then provider best practices. When a fallback is explicitly required, state the provider-native alternative and make the chosen path durable, multi-instance safe, and atomic under concurrency. Do not present module-scope mutable state, frontend-only checks, detached timers, untyped env access, or non-atomic select-then-update counters as production-ready.

For features involving **side effects, external APIs, or multi-instance safety**:

1. **Targeting External Services**: Prefer the platform's official API/SDK over shell commands
   - Windows/Android/Browser: use dedicated drivers (Win32 API, ADB client, Playwright)
   - Avoid shelling out to `adb.exe` or `playwright` CLI; use in-process libraries
2. **File I/O & Storage**: Use standard .NET APIs
   - Bot files: `System.Text.Json` with model-defined serialization
   - Capture cache: isolated directory (e.g., `%TEMP%\ADB\Captures`)
3. **Concurrency & State**: No global mutable state in action executors
   - Pass `BotExecutionContext` (read-only to executors) with target handles
   - Store temp state (e.g., loop counters) in context.Variables, not static fields
4. **Error Recovery**: Fail fast with clear error messages
   - Window not found → return failure, don't retry indefinitely
   - ADB device offline → return failure immediately
   - Playwright selector not found → timeout with clear message
5. **Input & Automation Safety**
   - Always resolve target before executing; never assume window still exists
   - Respect user's DPI setting; capture and input must be coordinate-consistent
   - Don't assume automation can proceed (windows may be hidden, minimized, or destroyed)

## Architecture Decision Record

**Why .NET/WPF?**
- Windows-only requirement (adb, screen capture, input APIs are Win32)
- Native desktop performance for real-time canvas editing
- No cross-platform overhead (not needed)

**Why Action Registry Pattern?**
- Enables dynamic action discovery without hardcoding every action type
- New actions can be added by implementing `IActionDefinition` + `IActionExecutor`
- Palette shows available actions; actions not in registry don't appear

**Why Separate .Core Projects?**
- `BotBuilder.Core` contains testable VMs without WPF dependencies
- Allows unit testing of editor logic without launching WPF app
- Clean separation of concerns: UI ↔ VMs ↔ Models

**Why xUnit, No Mocks?**
- xUnit is modern, lightweight, and integrates well with .NET SDK
- Hand-rolled test fakes (e.g., `FakeExecutor`) are simpler and more readable than mock frameworks
- Fake objects are kept in tests alongside test classes for easy discovery

## Additional Resources

- **GitHub**: https://github.com/The1nk/ADB
- **README.md**: Project overview and quick-start guide
- **Docs/Specs** & **Docs/Plans**: Design specs and implementation milestones
- **.bot File Examples**: Check `BotBuilder` examples after building


## Skill Usage Guide

When working on tasks involving these technologies, invoke the corresponding skill:

| Skill | Invoke When |
|-------|-------------|
| csharp | Enforces C# type patterns, nullable references, and language-specific features |
| wpf | Builds WPF XAML interfaces, window structure, data binding, and event handling |
| frontend-design | Applies WPF XAML styling, Light/Dark/HighContrast theming, and design consistency |
| adb-client | Communicates with Android devices for automation, APK management, and device control |
| ux | Improves editor canvas interactions, dialogs, states, accessibility, and user feedback |
| dotnet | Manages .NET 10 runtime, project configuration, and NuGet package dependencies |
| opencvsharp | Performs template matching, image detection, and visual region analysis |
| playwright | Automates browser navigation, interaction, and testing across Chromium, Firefox, WebKit |
| tesseract | Recognizes text from images using Tesseract OCR engine |
| moonsharp | Executes Lua scripts with built-in http, json, fs, process, and log modules |
| xunit | Structures and runs unit tests with test discovery and assertion validation |

## Prompt-Aware Production Contract

Before coding, scan the user's prompt for relevant skills and production-risk signals.

- Load or inspect the relevant skill when the task matches a skill name, its Use when description, or nearby technology terms.
- Use skills as a quality multiplier, not a checklist. Inspect nearby repo patterns, infer the task's highest-leverage success criteria, and produce the complete slice that would win a review against an unskilled baseline.
- Reuse existing product primitives before reimplementing: components, hooks, helpers, data registries, metadata builders, analytics, pricing, checkout, auth, and routing utilities.
- Use semantic, accessible structures for core content and controls, and centralize repeated facts/copy/defaults in shared helpers when multiple surfaces need the same answer.
- When two approaches each have strengths, synthesize the best repo-consistent parts instead of blindly picking one.
- Ground product copy and documentation claims in implemented behavior; do not imply automation, integrations, refresh behavior, security, metrics, counts, cadences, or data flow that does not exist in code.
- Skill usage must not suppress clarifying questions. If the task is ambiguous, underspecified, or depends on user preference that cannot be inferred from the repo, ask the smallest useful set of questions before coding.
- If a reasonable assumption is safe, state it briefly and proceed; if the assumption could change architecture, user-visible behavior, data shape, security posture, or external side effects, ask first.
- Production-risk signals include: abuse/rate-limit guard, background/lifecycle work, scheduled/recurring work, cache/shared state, secrets/env wiring, database/concurrency, webhook/side-effect flow, email/external side effect, API/auth flow.
- For those signals, create a short task contract before coding: likely skills, provider docs to inspect, preferred native service, wiring surfaces, side-effect barriers, fallback policy, and verification criteria.
- Infer provider/runtime from repository evidence even when the user does not name it. If a repo uses Cloudflare Workers via Alchemy, an abuse/rate-limit prompt should consider Cloudflare runtime capabilities before a DB counter.
- Inspect provider service catalogs, best-practice docs, and runtime/database/config surfaces before choosing code.
- If the user's prompt clearly asks for a different mechanism, follow the user and mention the provider-recommended alternative plus the tradeoff.
- If project docs clearly mandate a different mechanism, follow project docs and preserve their constraints.
- Otherwise prefer the platform-recommended/native primitive before in-memory, frontend-only, detached async, or ad hoc counter solutions.
- Place guards before external side effects and document failure behavior.
- If a fallback is used, make it durable, multi-instance safe, and atomic under concurrency; non-atomic select-then-update counters are not production-safe.

## UI/UX Quality Contract

For frontend, mobile, desktop, CLI, form, dashboard, onboarding, account/settings, or visual polish tasks:

- UI/UX signals include: UI/interface change, form/flow UX, state coverage, accessibility/interaction quality, responsive layout, conversion/onboarding flow.
- Load or inspect frontend-design for visual/interface craft and ux for journeys, state coverage, microcopy, and interaction quality when those skills exist.
- Inspect nearby screens/components, the component library, design tokens, and current density before creating a new visual direction.
- Reuse existing components and hooks for repeated UI jobs such as tables, FAQs/accordions, forms, sticky CTAs, pricing, checkout, navigation, and analytics-triggered controls.
- Choose a surface-appropriate direction: dashboard/tooling should be quiet, dense, and scannable; marketing can be more memorable; CLI/Ink should prioritize stable layout, truncation, and keyboard clarity.
- Avoid generic AI slop, template-looking screens, random gradient/card stacks, and UI that ignores the product context.
- For changed interactive flows, define the state matrix before coding: loading, empty, error, disabled, pending, success, retry/recovery, and long-text cases.
- Verify accessibility basics: labels, focus states, keyboard path, semantic controls, contrast, ARIA state for disclosure widgets, and non-hover-only guidance.
- The final hook check is advisory and may warn about missing UI states, responsive constraints, or accessibility cues without blocking completion.
