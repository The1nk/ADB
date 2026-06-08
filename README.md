# ADB: A Damn Bot 🤖

*You've got games to NOT play. Let a damn bot do the grinding.*

Why click 10,000 times when a damn bot will click 10,001 and never complain? ADB is a Windows desktop toolkit for building UI-automation bots that grind **Windows games**, **Android devices** (over `adb`), and **web browsers** (via Playwright) while you go touch grass. Drag actions onto a canvas, wire 'em up, point them at a target, and walk away a richer person (in-game currency only — we make no promises).

> Windows-only (WPF). Targets .NET 10 (`net10.0-windows`). The name is a backronym and we are deeply proud of it.

## What's in this glorious box

| Project | Kind | What it actually does |
| --- | --- | --- |
| **BotBuilder** | WPF app | The lab. Drag actions onto a canvas, wire them up, assign targets, test-run, cackle. |
| **BotRunner** | Console app | The night-shift worker. Runs a saved `.bot` file headlessly — scripts, Task Scheduler, set-it-and-forget-it grinding. |
| **BotCapture** | WPF app | The eyes. Snip template images from a window so your bot knows what "the shiny button" looks like. |
| **AdbCore** | Library | The brain. Action definitions, execution, target binding, screen capture, image matching, OCR, browser & Android drivers, Lua scripting. Everything that makes the bot *damn*. |
| **AdbUi.Theme** | Library | The wardrobe. Light / Dark / High-Contrast, because grinding at 3 a.m. shouldn't blind you. |
| `*.Core` / `*.Tests` | Libraries | The responsible adults. Testable logic layers + xUnit suites that keep the chaos from becoming *actual* chaos. |

## Concepts (the lore)

- **Bot** — a graph of action nodes wired together by success/failure paths, saved as a `.bot` file (JSON). It's a flowchart that gets off the couch and does the work.
- **Action** — one move (tap, click, type, find image, read text, run Lua, …), sorted into palette **categories**: Control Flow, Screen, Input, Android, Browser, Data, Scripting. Mix and match into combos.
- **Target** — the poor victim of your automation. Three kinds, summoned with a selector string:
  - **Window** — `process:<name>` or `title:<window title>`
  - **Android** — `serial:<device serial>`
  - **Browser** — `browser:<engine>` where engine is `chromium`, `firefox`, or `webkit`

  Type-specific actions snap to the right target on their own, and the editor's target picker writes the selectors for you — because nobody has memorized a selector syntax willingly, ever.

## The arsenal

- **Visual node-graph editor** — drag/drop palette, multi-select, copy/paste, undo/redo, and **"Tidy Up"** auto-layout for when your masterpiece looks like a plate of spaghetti.
- **Image matching** (OpenCvSharp) — find / wait-for / assert-absent template images on Screen *and* Android, with a coordinate & region picker. Show it the loot button once; it clicks it until the heat death of the universe.
- **OCR** (Tesseract, bundled `eng`) — read / find / wait-for / assert-absent text. Reads your gold counter, your cooldowns, and the "YOU DIED" screen so the bot knows when to ragequit gracefully.
- **Lua scripting** (MoonSharp) — a "Run Lua Script" action with `http`, `json`, `fs`, `process`, and `log` host APIs for whenever the visual blocks aren't enough and you need to go full mad scientist.
- **Input & windows** — mouse/keyboard actions, activate window. The clicky-clicky.
- **Theming** — Light / Dark / High-Contrast, following the OS by default (`View ▸ Theme` in BotBuilder).

## Summoning requirements

- Windows 10/11
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Optional, per feature:
  - **Android actions** — `adb` on your `PATH` (Android Platform Tools)
  - **Browser actions** — Playwright browsers installed (`playwright install`)

  Missing a dependency? The bot doesn't throw a tantrum — the categories you can't use just go grey in the palette with a tooltip telling you exactly what to install. Polite, for a goblin.

## Build the beast

```sh
dotnet build ADB.slnx
```

Run the tests, because an *unreliable* damn bot is just a random number generator with extra steps:

```sh
dotnet test ADB.slnx
```

## Unleash it

**Build a bot** — fire up BotBuilder:

```sh
dotnet run --project BotBuilder
```

Drag actions from the palette onto the canvas, connect them, add targets in the top bar (smash **Pick…** to grab a window/device/browser), and hit **F5** to Test Run. Save it as a `.bot` file and congratulations — you have constructed a damn bot.

**Run it headlessly** — BotRunner takes the saved `.bot` and the targets to bind, then grinds in the dark:

```sh
dotnet run --project BotRunner -- --bot path\to\my.bot --target Main=process:notepad
```

BotRunner arguments:

| Flag | Required | Description |
| --- | --- | --- |
| `--bot <path>` | yes | The `.bot` file to run. |
| `--target Name=selector` | repeatable | Bind a named target to a selector (e.g. `--target Main=serial:emulator-5554`). |
| `--log-level <level>` | no | `debug` \| `info` (default) \| `warn` \| `error`. |
| `--log-file <path>` | no | Also write logs to this file (proof of grind). |

**Capture template images** — launch BotCapture and snip a region of a window into a PNG for image-matching actions:

```sh
dotnet run --project BotCapture
```

## Where everything lives

```
AdbCore/            the brain: actions, execution, targets, screen/ocr/browser/android, scripting
AdbUi.Theme/        the wardrobe: shared WPF theme (Light/Dark/High-Contrast)
BotBuilder/         the lab: visual editor (WPF) + BotBuilder.Core (testable VMs)
BotRunner/          the night shift: headless runner (console)
BotCapture/         the eyes: template-image capture tool (WPF) + BotCapture.Core
Docs/Specs,Plans/   the grimoire: design specs and implementation plans
assets/             loot: bundled assets (e.g. Tesseract eng traineddata)
```

---

*ADB: A Damn Bot. Click responsibly. Or don't — that's kind of the whole point.* 🤖
