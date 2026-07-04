# Android Long Press, Send Text & Press Key actions

**Date:** 2026-07-03
**Status:** Approved — ready for implementation plan

## Problem

The Android action set can tap and swipe but cannot **long-press** a point, **type text** into a
field, or **send discrete key events** (notably Backspace, to clear a field before typing). This spec
adds three Android actions to close that gap, plus a small documentation fix the user flagged.

## Goals

- **Long Press** — press-and-hold at a coordinate for a configurable duration.
- **Send Text** — type a literal string into the currently focused field.
- **Press Key** — send a named key (Backspace, Enter, arrows, …) one or more times, so clearing a
  field is "Press Key → Backspace → count 30".
- Fill the wiki gap where `VirtualKeys` is referenced but its accepted key names are never listed.

## Non-goals

- No modifier keys (Ctrl/Alt/Shift) on Android — `adb input keyevent` has no clean chord equivalent
  and there is no use case here.
- No free-text key entry on Android — the curated dropdown covers the keys that matter, and literal
  characters are already handled by Send Text.

## Design

### Three new actions

All three extend `AndroidActionBase` (category **Android**, ports `onSuccess`/`onFailure`, no retry),
resolve the bound `IAndroidDevice` via `ResolveDevice`, and return `RequiresDevice()` when unbound —
identical to `TapAction`/`SwipeAction`/`PressBackAction`.

| Action | TypeKey | Config fields | adb command |
| --- | --- | --- | --- |
| **Long Press** | `android.longPress` | `x` (Number, 0), `y` (Number, 0), `durationMs` (Number, **600**) | `input swipe x y x y <dur>` |
| **Send Text** | `android.sendText` | `text` (MultilineString) | `input text '<escaped>'` |
| **Press Key** | `android.pressKey` | `key` (Enum, default `Backspace`), `count` (Number, 1) | `input keyevent <code> [<code> …]` |

**Long Press** uses the standard adb long-press idiom: a swipe whose start and end points are equal,
held for `durationMs`. This mirrors `SwipeAction` exactly, just with one point and a longer default
duration. Default **600 ms** (a comfortable long-press threshold on Android, which is ~500 ms).

**Send Text** types a literal string. **Empty text is a no-op that returns success** (a deliberate
single space is still typed), mirroring the Windows *Type Text* action. Non-empty text is escaped (see
below) and sent via `input text`.

**Press Key** resolves the selected key name to an Android keycode and sends `input keyevent` with the
code repeated `count` times **in a single shell invocation** (e.g. `input keyevent 67 67 67`), so one
"Backspace × 30" is one round-trip, not 30. `count` defaults to 1; a value below 1 is treated as 1.
The `key` dropdown's `Options` come from the same ordered table that resolves names to codes, so the
list and the resolver cannot drift.

### Android keycode resolver — `AdbCore/Android/AndroidKeyCodes.cs`

Win32 virtual-key codes (`AdbCore/Input/VirtualKeys.cs`) are **not** Android keycodes — Win32
Backspace is `0x08`, Android `KEYCODE_DEL` is `67`. So Press Key needs its own table. This is a new,
Android-specific static class that is the **single source of truth** for both the dropdown options and
name→code resolution:

| Display name | Android keycode | Constant |
| --- | --- | --- |
| `Backspace` | 67 | KEYCODE_DEL |
| `Delete (Fwd)` | 112 | KEYCODE_FORWARD_DEL |
| `Enter` | 66 | KEYCODE_ENTER |
| `Tab` | 61 | KEYCODE_TAB |
| `Space` | 62 | KEYCODE_SPACE |
| `Up` | 19 | KEYCODE_DPAD_UP |
| `Down` | 20 | KEYCODE_DPAD_DOWN |
| `Left` | 21 | KEYCODE_DPAD_LEFT |
| `Right` | 22 | KEYCODE_DPAD_RIGHT |
| `Home` | 122 | KEYCODE_MOVE_HOME |
| `End` | 123 | KEYCODE_MOVE_END |
| `Escape` | 111 | KEYCODE_ESCAPE |

API:
- `IReadOnlyList<string> Names` — ordered display names, used verbatim as the `key` field's `Options`.
- `bool TryResolve(string name, out int keyCode)` — case-insensitive; false for an unknown name.

`PressKeyAction` fails with a clear message if resolution fails (defensive — the dropdown makes this
unreachable in normal use, but a hand-edited `.bot` could carry a stale/free value).

### Text escaping

`AdvancedSharpAdbDevice.Shell` runs the command through a single device shell via
`AdbClient.ExecuteRemoteCommand`. To make spaces and shell metacharacters literal, `SendText`
**single-quote-wraps** the argument and escapes embedded single quotes as `'\''`:

```
input text 'hello world'
input text 'it'\''s me'
```

This is more robust than the `%s`-for-space substitution trick and correctly handles `" & | ; ( ) < >
* $` etc. inside the quotes. The device is the user's live-verified surface, so this escaping is the
one behavior to confirm against real hardware.

### `IAndroidDevice` additions

Add to the interface and implement in `AdvancedSharpAdbDevice` (each a thin `Shell(...)` call, wrapped
by the existing stale-handle `Invoke` retry):

```csharp
void LongPress(int x, int y, int durationMs);   // input swipe x y x y durationMs
void SendText(string text);                      // input text '<escaped>'
void KeyEvent(int keyCode, int count);           // input keyevent <code> [<code> ...]
```

`KeyEvent` builds the repeated-code string; `count < 1` is clamped to 1 at the action layer.

### Wiring (the complete slice)

- **Registration** — `BuiltInActions.cs`: `LongPressAction` after `SwipeAction`; `SendTextAction`
  and `PressKeyAction` after `PressBackAction`.
- **Coordinate picker** — add `["android.longPress"] = [new CoordinatePoint("x", "y", "Target")]` to
  `CoordinateFieldMap` so Long Press gets the "Pick…" button, exactly like `android.tap`.
- **Properties panel** — no UI change: `ConfigFieldType.Enum` already renders as a themed dropdown via
  the existing `ConfigFieldTemplateSelector`; `MultilineString` and `Number` already render.

### Tests (`AdbCore.Tests`, mirroring existing Android action tests)

Using a hand-rolled `FakeAndroidDevice` that records calls:

- **LongPress** — emits a same-point swipe (`x==x1==x2`, `y==y1==y2`) with the configured duration;
  default duration is 600; returns `RequiresDevice()` failure when the target is unbound.
- **SendText** — escapes embedded single quotes and preserves spaces; empty text is a
  no-op that returns success and issues no command (a deliberate single space is still typed).
- **PressKey** — each dropdown name resolves to the correct keycode; `count` repeats the code that
  many times; `count < 1` behaves as 1; unresolvable key name fails with a clear message.
- **AndroidKeyCodes** — every entry in `Names` resolves via `TryResolve`, and the resolved codes match
  the table above (guards the single-source-of-truth invariant).
- **CoordinateFieldMap** — `Supports("android.longPress")` is true and maps to the `x`/`y` point.

### Documentation (all three surfaces, same unit of work)

- **Wiki `Actions-Reference.md`** — add the three rows to the **Android** table; add a short list of
  the Press Key key names. Separately, in the **Input** section, list the accepted `VirtualKeys` names
  for the Windows *Key Press* action (Enter/Return, Esc/Escape, Tab, Space, Backspace, Delete/Del,
  Insert, Home, End, PageUp, PageDown, arrows, A–Z, 0–9, F1–F12) — the user-flagged gap.
- **README.md** — the *arsenal* section lists actions only illustratively; fold "long-press" into the
  input mention (light touch), keeping the goblin voice.
- **CLAUDE.md** — no per-action table exists (Android actions are covered by "Tap, Swipe, LaunchApp,
  etc."); no change required.

## Execution

Subagent-driven development after the implementation plan is written. This change is entirely AdbCore
engine + one `BotBuilder.Core` logic map (`CoordinateFieldMap`) with no XAML/visual surface, so it is a
backend-only slice: self-merge after review rather than parking for visual sign-off.
