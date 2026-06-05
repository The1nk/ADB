# M12 — Lua Scripting — Design

**Status:** Approved
**Date:** 2026-06-05
**Milestone:** M12 (post-V1 roadmap). **Absorbs the dropped M13 (Web & API) + M14 (Files & System)** — their functionality is delivered through the Lua host API rather than visual nodes.

---

## 1. Overview

The Lua escape hatch from V1 (§1, §4.3, §8): *"if the built-in actions aren't enough, a Lua scripting escape hatch covers the rest."* A single **Run Lua Script** action runs a user-authored Lua script with two-way access to the bot's variables and a **host API** exposing HTTP, JSON, filesystem, and process operations (the absorbed M13 + M14). It is deliberately unsandboxed — a script can do anything the user's account can — which is appropriate for a local "run your own bots" tool.

## 2. Engine

**MoonSharp** — a pure-C# Lua 5.2 interpreter (single managed DLL, **zero native dependencies**). Chosen over V1 §8's NLua because the design is host-API-interop-heavy and MoonSharp's clean .NET interop + no-native-deps profile fit better (and match V1's own stated "lightweight, embeddable, no external runtime" rationale). Lua 5.2 (no integer subtype, minor syntax deltas vs 5.4) is irrelevant for a scripting escape hatch. MoonSharp runs in-process and is interruptible.

## 3. Action

`scripting.runLua` — **Run Lua Script**, category **Scripting**.
- **Config:** one multiline `script` field (rendered by the existing multiline-string config-field type).
- **Ports:** `onSuccess` / `onFailure`. The script runs to completion → `onSuccess`; a Lua error (runtime error, or an explicit `error("msg")` in the script) → `onFailure` with the error message. This drives retry / onFailure routing like the matchable actions.
- **Target-agnostic:** no `TargetId` (scripting is not tied to a window/device/browser).
- **`SupportsRetry = true`.**

## 4. Variable bridge

A global **`vars`** table proxies `ExecutionContext.Variables`:
- `vars.foo` reads bot variable `foo` — mapped to a Lua type (CLR `string` → Lua string, `double`/number → Lua number, `bool` → Lua boolean); `nil` when unset.
- `vars.foo = x` writes it back, coercing the Lua value to its CLR equivalent (`string`/`double`/`bool`) stored in `ExecutionContext.Variables`.

So Lua reads and writes the exact same variables that `${foo}` interpolation, Set Variable, and Find Image's `matchRandX` etc. use — fully bidirectional. (The proxy is a MoonSharp `userdata`/table with index + newindex metamethods over the variables dictionary.)

## 5. Host API

Exposed as global tables. **Error model:** a *result/status* (HTTP non-2xx, a process's non-zero exit code) is a returned **value**, not an error; an *operational failure* (network transport error, file not found, process couldn't start) raises a **Lua error** the script can `pcall`, or let propagate → `onFailure`.

- **`http`** — `http.get(url [, headers])`, `http.post(url, body [, headers])` → response table `{ status = <int>, body = <string>, headers = <table> }`. Backed by a shared `HttpClient`. Honors the action's CancellationToken.
- **`json`** — `json.parse(str)` → Lua table/value; `json.encode(value)` → string. Backed by System.Text.Json. Malformed input raises a Lua error.
- **`fs`** — `fs.read(path) → string`, `fs.write(path, content)`, `fs.copy(src, dst)`, `fs.move(src, dst)`, `fs.exists(path) → bool`, `fs.delete(path)`. Backed by System.IO.File. Failures raise Lua errors.
- **`process`** — `process.run(command [, argsTable]) → { exitCode = <int>, stdout = <string>, stderr = <string> }`. Backed by System.Diagnostics.Process (run + wait). A non-zero exit is a value (not an error); failure to start raises a Lua error. Honors the CancellationToken.
- **`log(msg)`** — emits a line into the run log (the same `Action<string>` log sink the other actions use).

## 6. Cancellation, not a time limit

HTTP and process operations block synchronously inside their host functions (the underlying async work is awaited internally) so the Lua script stays simple and synchronous. The action's `CancellationToken` aborts a running script — MoonSharp execution is interrupted, and `HttpClient`/`Process` honor the token — so **Stop works and a runaway loop is cancellable**. There is **no hard execution timeout** (per "trust your own bots").

## 7. Architecture

- `AdbCore/Scripting/` holds the engine + host. The I/O host functions sit behind **injectable adapters** so the action is unit-testable without real I/O:
  - `IFileSystem` (read/write/copy/move/exists/delete),
  - `IProcessRunner` (`Run(command, args, ct) → (int exitCode, string stdout, string stderr)`),
  - `IHttpRequester` (`Send(method, url, body, headers, ct) → (int status, string body, IReadOnlyDictionary<string,string> headers)`).
  Concrete adapters (`System.IO.File`, `System.Diagnostics.Process`, `HttpClient`) are the thin, live-verified implementations.
- `RunLuaScriptAction` builds a MoonSharp `Script`, registers the `vars` proxy + the host tables (backed by the injected adapters + the log sink + the CancellationToken), runs the script, and maps a thrown `ScriptRuntimeException`/error to `onFailure` (else `onSuccess`).
- Because MoonSharp is pure managed code, **unit tests run real Lua scripts** against fake adapters and assert effects on `vars` (and that http/fs/process were called as expected). The interpreter is deterministic and fast.

## 8. Slicing

- **M12a — Lua core (self-mergeable):** MoonSharp engine + `RunLuaScriptAction` + the `vars` bridge + `json` + `log`. No external I/O → fully deterministic unit tests (run real Lua, assert variable effects + json round-trip + error→onFailure). Backend-only, no live deps → self-merged.
- **M12b — I/O host modules (user-verified):** `http` + `fs` + `process` (the three adapters + their Lua tables), wired into the same script host. Unit-tested with fake adapters; the concrete `HttpClient`/`File`/`Process` adapters get a hands-on live verify (real request / real file / real process). **Completes M12.**

## 9. Testing

- **M12a unit (AdbCore.Tests):** a script that sets `vars.x = 5` then asserts `Variables["x"]`; reads an existing variable; `json.parse`/`json.encode` round-trip; `log` reaches the sink; a script `error("boom")` (and a runtime error) → `onFailure` with the message; a clean script → `onSuccess`; cancellation aborts a long loop.
- **M12b unit:** scripts driving fake `IHttpRequester`/`IFileSystem`/`IProcessRunner` (e.g. `http.get` returns the fake's response table; `fs.read` returns fake content; `process.run` returns a fake exit/stdout; transport/fs failures → Lua error → pcall-catchable or onFailure).
- **Live (M12b, user):** a real HTTP GET against a known endpoint, a real file read/write, a real `process.run` — confirming the concrete adapters behave.

## 10. Out of scope

- Sandboxing / capability restriction (intentionally unsandboxed; flagged).
- A hard execution time limit (cancellation only).
- Per-script Lua module loading from disk / `require` of external `.lua` files (the script is self-contained in the config field) — possible later.
- Visual HTTP/JSON/File nodes (dropped M13/M14 — Lua is the path).

## 11. Merge handling

- **M12a:** AdbCore-only, deterministic unit tests, no live deps → built compile-clean + unit-green and **self-merged** via `gh` (per the backend-only-slice rule).
- **M12b:** has live I/O adapters needing a hands-on check → opened as a PR, **user-verified + merged**.
