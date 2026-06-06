# ADB — visual bot builder & automation toolkit

ADB is a Windows desktop toolkit for building and running UI-automation "bots" against **Windows windows**, **Android devices** (over `adb`), and **web browsers** (via Playwright). You design a bot as a node graph in a visual editor, then run it from the editor or headlessly from the command line.

> Windows-only (WPF). Targets .NET 10 (`net10.0-windows`).

## What's in the box

| Project | Kind | What it does |
| --- | --- | --- |
| **BotBuilder** | WPF app | The visual editor — drag actions onto a canvas, wire them up, assign targets, test-run. |
| **BotRunner** | Console app | Runs a saved `.bot` file headlessly (used by the editor's Test Run and for scripts / Task Scheduler). |
| **BotCapture** | WPF app | Capture template images from a window for image-matching actions (region select + preview/confirm). |
| **AdbCore** | Library | The engine — action definitions, execution, target binding, screen capture, image matching, OCR, browser & Android drivers, Lua scripting. |
| **AdbUi.Theme** | Library | Shared WPF theming (Light / Dark / High-Contrast) used by the apps. |
| `*.Core` / `*.Tests` | Libraries | Testable view-model/logic layers and their xUnit test suites. |

## Concepts

- **Bot** — a graph of action nodes connected by success/failure paths, saved as a `.bot` file (JSON).
- **Action** — one step (tap, click, type, find image, read text, run Lua, …), grouped into palette **categories**: Control Flow, Screen, Input, Android, Browser, Data, Scripting.
- **Target** — what an action acts on. Three kinds, selected by a selector string:
  - **Window** — `process:<name>` or `title:<window title>`
  - **Android** — `serial:<device serial>`
  - **Browser** — `browser:<engine>` where engine is `chromium`, `firefox`, or `webkit`

  Type-specific actions bind to the matching target automatically; the editor's target picker writes these selectors for you.

## Capabilities

- **Visual node-graph editor** — drag/drop palette, multi-select, copy/paste, auto-layout ("Tidy Up"), undo/redo.
- **Image matching** (OpenCvSharp) — find / wait-for / assert-absent template images, on Screen and Android, with a coordinate & region picker.
- **OCR** (Tesseract, bundled `eng`) — read / find / wait-for / assert-absent text.
- **Lua scripting** (MoonSharp) — a "Run Lua Script" action with `http`, `json`, `fs`, `process`, and `log` host APIs for anything the visual actions don't cover.
- **Input & windows** — mouse/keyboard actions, activate window.
- **Theming** — Light / Dark / High-Contrast, following the OS by default (`View ▸ Theme` in BotBuilder).

## Requirements

- Windows 10/11
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Optional, per feature:
  - **Android actions** — `adb` on your `PATH` (Android Platform Tools)
  - **Browser actions** — Playwright browsers installed (`playwright install`)

  Categories whose tooling is missing are greyed out in the palette with a tooltip explaining what's needed.

## Build

```sh
dotnet build ADB.slnx
```

Run the test suite:

```sh
dotnet test ADB.slnx
```

## Run

**Author a bot** — launch BotBuilder:

```sh
dotnet run --project BotBuilder
```

Drag actions from the palette onto the canvas, connect them, add targets in the top bar (use **Pick…** to choose a window/device/browser), and press **F5** to Test Run. Save as a `.bot` file.

**Run a bot headlessly** — BotRunner takes the saved `.bot` and the targets to bind:

```sh
dotnet run --project BotRunner -- --bot path\to\my.bot --target Main=process:notepad
```

BotRunner arguments:

| Flag | Required | Description |
| --- | --- | --- |
| `--bot <path>` | yes | The `.bot` file to run. |
| `--target Name=selector` | repeatable | Bind a named target to a selector (e.g. `--target Main=serial:emulator-5554`). |
| `--log-level <level>` | no | `debug` \| `info` (default) \| `warn` \| `error`. |
| `--log-file <path>` | no | Also write logs to this file. |

**Capture template images** — launch BotCapture to grab a region of a window as a PNG for image-matching actions:

```sh
dotnet run --project BotCapture
```

## Repository layout

```
AdbCore/            engine: actions, execution, targets, screen/ocr/browser/android, scripting
AdbUi.Theme/        shared WPF theme (Light/Dark/High-Contrast)
BotBuilder/         visual editor (WPF)  + BotBuilder.Core (testable VMs)
BotRunner/          headless runner (console)
BotCapture/         template-image capture tool (WPF) + BotCapture.Core
Docs/Specs,Plans/   design specs and implementation plans
assets/             bundled assets (e.g. Tesseract eng traineddata)
```
