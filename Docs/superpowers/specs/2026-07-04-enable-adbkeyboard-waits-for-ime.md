# Enable ADB Keyboard waits for IME readiness

**Date:** 2026-07-04
**Status:** Approved — ready for implementation plan

## Problem

`EnableAdbKeyboardAction` runs `ime set com.android.adbkeyboard/.AdbIME` and returns immediately. The
`adb` call is already **synchronous** (`AdvancedSharpAdbDevice.Shell` blocks on
`ExecuteRemoteCommand`), but Android switches the active IME **asynchronously on the device**: after
`ime set` returns, the framework still has to unbind the previous IME and bind + initialize ADBKeyboard.
Until that finishes, ADBKeyboard's broadcast receiver isn't registered, so a broadcast fired immediately
afterward — our Send Text (`ADB_INPUT_B64`) node **or** a user's Lua `am broadcast -a ADB_CLEAR_TEXT` —
lands on nothing and is silently dropped. The user currently works around this by hand-placing a Delay
node after Enable.

## Goal

Make **Enable ADB Keyboard** block until ADBKeyboard is actually the active IME (plus a short settle for
the receiver to register), so no downstream Delay is ever needed. Fail clearly if the switch never takes.

## Non-goals

- No change to Send Text, Restore Keyboard, the device layer, or `AdbInputCommand` — `GetInputMethod()`
  already exists and is all we need to poll.
- No general "wait for arbitrary IME" node — this is specific to the Enable action's own switch.

## Design

`EnableAdbKeyboardAction.ExecuteAsync` becomes genuinely **async** (it currently returns
`Task.FromResult`). New order of operations:

1. `ct.ThrowIfCancellationRequested()`; resolve device or `RequiresDevice()` (unchanged).
2. `IsInputMethodAvailable(AdbKeyboard)` → fail with the install hint if absent (unchanged).
3. `previous = GetInputMethod()`, then **stash `previous` into the run variable immediately** (moved to
   here, before the switch) so a later Restore has the pre-Enable IME even if activation later times out.
4. `EnableInputMethod(AdbKeyboard)`; `SetInputMethod(AdbKeyboard)` (unchanged).
5. **Poll for activation:** loop up to `maxWaitMs` (internal, default **3000 ms**), checking
   `GetInputMethod() == AndroidImes.AdbKeyboard` (ordinal); between checks `await Task.Delay(pollIntervalMs, ct)`
   (internal, default **150 ms**). Break as soon as it reports active.
6. If it never became active within `maxWaitMs` → `ActionResult.Fail("ADBKeyboard did not become the active
   input method within 3000 ms — the device may be slow to switch keyboards; try again or add a Delay.")`.
7. **Settle:** `await Task.Delay(settleMs, ct)` — give the IME's broadcast receiver time to register.
8. Return `ActionResult.Ok(SuccessPort)`.

### New config field

- `settleMs` (Number, `Label = "Settle (ms)"`, **DefaultValue 400**) — the post-activation settle. Read via
  `ConfigValues.GetInt(config, "settleMs", 400)`; a value `< 0` is treated as `0`. This is the user's
  tunable knob (their choice: *poll + configurable settle*). `maxWaitMs`/`pollIntervalMs` stay internal.

### Test seam for fast tests

The parameterless constructor (used by the action registry) delegates to `(maxWaitMs: 3000,
pollIntervalMs: 150)`. Add a second **public** constructor `EnableAdbKeyboardAction(int maxWaitMs, int
pollIntervalMs)` as the test/tuning seam so tests can pass tiny values (e.g. `maxWaitMs: 50,
pollIntervalMs: 5`) and exercise the timeout path in milliseconds. (This repo has no
`InternalsVisibleTo`, so the seam is public rather than internal — harmless: the registry only ever calls
the parameterless ctor.) `settleMs` is config-driven, so tests pass `["settleMs"] = 0` to avoid a real
400 ms wait.

### Cancellation

The poll and settle use `Task.Delay(..., ct)`, so a cancelled run throws `OperationCanceledException` out
of the action — the standard cancellation behavior (same as `DelayAction`); no special handling.

### Fake device change (`AdbCore.Tests` `FakeAndroidDevice`)

Add a knob so the timeout path is testable:

- `bool SuppressImeActivation { get; set; } = false;`
- `SetInputMethod(string ime)` records `imeset {ime}` and, **only when `!SuppressImeActivation`**, sets
  `ActiveIme = ime`. Default (false) keeps today's behavior — the switch "takes" immediately, so the poll
  sees it active on the first check. With `SuppressImeActivation = true`, `GetInputMethod()` keeps
  returning the old IME → the action polls to timeout.

(The `BotCapture.Core.Tests` fake needs no change — its `SetInputMethod` is a no-op stub and it never
exercises Enable.)

## Tests (`AdbCore.Tests`, `AndroidInputActionTests`)

Use the internal ctor with small timings and `["settleMs"] = 0` unless noted:

- **Activates then succeeds** — default fake (activation takes immediately): the action stashes the prior
  IME, calls `imeenable`/`imeset`, and returns success; assert a `getime` poll ran **after** `imeset`
  (i.e. it verified activation, not just fire-and-forget).
- **Times out when the IME never activates** — `SuppressImeActivation = true`, `maxWaitMs: 50,
  pollIntervalMs: 5`: returns failure whose message contains "did not become the active input method"; the
  prior IME was still stashed (robustness).
- **Not installed still fails with the install hint** — `AdbKeyboardInstalled = false` → fail before any
  `imeset` (unchanged behavior, preserved around the new async path).
- **No device bound → `RequiresDevice()` failure** (unchanged).
- **Settle honored** — with `["settleMs"] = 0` the happy-path test completes effectively instantly
  (guards that settle is config-driven and 0-skippable). Existing Enable tests are updated to pass
  `["settleMs"] = 0` so they stay fast.

The existing Task-6 Enable assertions (stashes `PreviousIme`, enables+sets ADBKeyboard, install-hint,
no-device) all still hold — they're updated only to construct via the internal ctor / pass `settleMs = 0`.

## Documentation

- **Wiki `Actions-Reference.md`** — Enable ADB Keyboard row: add the `settleMs` (num, `400`) config and a
  note that it waits for the IME to actually activate (no manual Delay needed).
- **CLAUDE.md** — the Android Driver row already describes Enable/Restore; add that Enable **waits for
  activation + a configurable settle**.
- **README.md** — the ADBKeyboard steps say "Drop an Enable ADB Keyboard node before you type"; add a
  half-sentence that it now waits for the keyboard to be ready (so no manual delay).

## Execution

Subagent-driven development after the plan. Pure AdbCore engine + one test-fake knob + tests, **no visual
surface** → backend-only slice, **self-merge** after review. The device-side activation timing is the
user's on-device confirmation, but the logic (poll + settle + timeout) is fully unit-tested via the fake.
