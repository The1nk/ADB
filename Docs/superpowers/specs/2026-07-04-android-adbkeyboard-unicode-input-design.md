# Android Unicode text entry (ADBKeyboard) + Press Key key expansion

**Date:** 2026-07-04
**Status:** Draft — awaiting user review

## Problem

Send Text (`android.sendText`) can only reach the device through `adb shell input text`. Android's
`input` command translates each character through the virtual-keyboard **`KeyCharacterMap`** (US/Latin)
and injects the resulting key events; a character with no key combination on that map produces no
events, and on most Android versions `KeyCharacterMap.getEvents` returns `null` for the **whole**
string if *any* character is unmappable — so one stray non-ASCII character makes the entire `input
text` inject **nothing**.

This bit a real bot: `PokeGo Bot.bot` sets a Pokémon-GO rename search string that contains **superscript
digits** — `U+2070`, `U+00B9`–`U+00B3`, `U+2074`–`U+2079` (`⁰¹²³⁴⁵⁶⁷⁸⁹`) — as its "leave these alone"
tags. Those code points are non-ASCII, so `${SearchString}` never reaches the search box. The
interpolation, single-quote escaping, and shell transport all preserve the characters correctly; the
loss is entirely inside the device's `input text`.

`input text` fundamentally cannot carry Unicode. On the user's environment (**non-rooted Android 10+**)
the clipboard-set workaround is also blocked — the OS denies clipboard writes to the non-focused
`shell` user ("application is not in focus"), and `service call clipboard` cannot cleanly marshal a
`ClipData` on modern Android. The one mechanism that reliably injects arbitrary Unicode on that
environment is an **input method (IME)** — specifically the community **ADBKeyboard** IME
(`com.android.adbkeyboard/.AdbIME`), which receives text over a broadcast and commits it as real
Unicode, bypassing the KeyCharacterMap.

Separately, the Press Key (`android.pressKey`) dropdown is missing many useful keys — most notably
**Paste**, which pairs with the clipboard/IME story, plus common navigation and system/power keys.

## Goals

- **Unicode Send Text** — let Send Text inject arbitrary Unicode (including the superscript search
  string) via ADBKeyboard, chosen per-node, base64-encoded so no shell-quoting/encoding hazard remains.
- **Explicit, restorable IME switching** — two small nodes to make ADBKeyboard the active IME and to
  restore the previous one, so switching is predictable, reusable, and never left in a surprising state.
- **Never silently no-op** — the ADB-Keyboard send path fails with a clear message when ADBKeyboard is
  not the active IME, instead of dropping text like `input text` did.
- **Expand Press Key** — add clipboard (Paste/Copy/Cut), navigation, and system/power keys.

## Non-goals

- **No bundling or auto-install of ADBKeyboard.** The Enable node detects whether it is installed and
  fails with an install hint; the user installs it (via the existing Install APK action or manually).
  Redistributing a third-party APK is out of scope.
- **No auto-detection in Send Text's default path.** The method is an explicit dropdown (mirroring the
  Click/Type `method` field); the default stays `input text` so existing bots are unchanged.
- **No clipboard route.** Rejected: blocked on non-rooted Android 10+ (see Problem).
- **No editor-action / "submit search" broadcast** (ADBKeyboard's `ADB_EDITOR_CODE`). Submitting is
  already covered by Press Key → Enter; can be a later addition.
- **No modifier chords on Android.** Consistent with the existing Press Key action.

## Design

### 1. Press Key — new keys (`AdbCore/Android/AndroidKeyCodes.cs`)

Pure data addition: append entries to the existing `Entries` table. The dropdown `Options` and the
name→code resolver both derive from this one table, so they cannot drift (unchanged invariant). New
entries are **appended after** the existing twelve so the current dropdown order and any saved `.bot`
values (which store the display-name string) are undisturbed. The existing cursor-movement `Home`/`End`
are left as-is; the new **device** Home button is named **`Home Button`** to avoid a name collision.

| Display name | Keycode | Constant | Group |
| --- | --- | --- | --- |
| `Paste` | 279 | KEYCODE_PASTE | Text editing |
| `Copy` | 278 | KEYCODE_COPY | Text editing |
| `Cut` | 277 | KEYCODE_CUT | Text editing |
| `Home Button` | 3 | KEYCODE_HOME | Navigation |
| `Back` | 4 | KEYCODE_BACK | Navigation |
| `Recent Apps` | 187 | KEYCODE_APP_SWITCH | Navigation |
| `Menu` | 82 | KEYCODE_MENU | Navigation |
| `Search` | 84 | KEYCODE_SEARCH | Navigation |
| `Page Up` | 92 | KEYCODE_PAGE_UP | Navigation |
| `Page Down` | 93 | KEYCODE_PAGE_DOWN | Navigation |
| `Power` | 26 | KEYCODE_POWER | System / power |
| `Wake` | 224 | KEYCODE_WAKEUP | System / power |
| `Sleep` | 223 | KEYCODE_SLEEP | System / power |
| `Volume Up` | 24 | KEYCODE_VOLUME_UP | System / power |
| `Volume Down` | 25 | KEYCODE_VOLUME_DOWN | System / power |
| `Mute` | 164 | KEYCODE_VOLUME_MUTE | System / power |

(Paste/Copy/Cut are API 24+; Wake/Sleep are API 20+/21+ — all satisfied by the target Android 10+.)
`PressKeyAction` is otherwise unchanged: it resolves the name and sends `input keyevent <code>` repeated
`count` times in one invocation.

### 2. Send Text — new `method` field (`AdbCore/Actions/BuiltIn/Android/SendTextAction.cs`)

Add a `method` config field, mirroring the `method` enum on the Windows Click/Type actions:

- `Key = "method"`, `Type = ConfigFieldType.Enum`, `DefaultValue = "Input Text"`,
  `Options = ["Input Text", "ADB Keyboard"]`.

Execution branches on the resolved method (empty `text` remains a success no-op in both branches, as
today):

- **`Input Text`** (default) — unchanged: `device.SendText(text)` → `input text '<escaped>'`.
- **`ADB Keyboard`** —
  1. **Guard:** query the active IME (`device.GetInputMethod()`); if it is not
     `AndroidImes.AdbKeyboard`, return
     `ActionResult.Fail("Send Text (ADB Keyboard) requires an 'Enable ADB Keyboard' node earlier in the bot — ADBKeyboard is not the active input method.")`.
     This is the deliberate "never silently no-op" behavior.
  2. **Send:** `device.SendAdbKeyboardText(text)` — base64-encodes the UTF-8 text and broadcasts it
     (see device layer). The text the action sees is already `${var}`-interpolated by the executor
     (`ConfigInterpolator.Resolve` runs before the action), so the resolved Unicode string is encoded.

The per-send IME query is one extra shell round-trip per Send Text; acceptable next to the broadcast,
and it is what makes a forgotten Enable node a clear failure rather than a silent drop.

### 3. Enable ADB Keyboard action (`android.enableAdbKeyboard`)

New action extending `AndroidActionBase` (category **Android**, `onSuccess`/`onFailure`, no retry).

- **Config:** `previousImeVar` (String, default `"PreviousIme"`) — the **name** of the run variable the
  node stashes the currently-active IME id into, for later restore.
- **Execution:**
  1. `current = device.GetInputMethod()`.
  2. If `!device.IsInputMethodAvailable(AndroidImes.AdbKeyboard)` → `Fail("ADBKeyboard is not installed
     on the device. Install the ADBKeyboard APK (e.g. with the Install APK action) and try again.")`.
  3. `device.EnableInputMethod(AndroidImes.AdbKeyboard)` then
     `device.SetInputMethod(AndroidImes.AdbKeyboard)`.
  4. `context.Context.Variables[previousImeVar] = current` (mirrors how `SetVariableAction` writes run
     variables). Skipped only if `previousImeVar` is blank.
  5. Return success.

### 4. Restore Keyboard action (`android.restoreKeyboard`)

New action extending `AndroidActionBase` (same ports/category/no-retry). The symmetric partner to
Enable, using the run-variable handoff.

- **Config:** `previousImeVar` (String, default `"PreviousIme"`) — the run variable to read the IME id
  from.
- **Execution:** read `context.Context.Variables[previousImeVar]` (as string). If non-empty,
  `device.SetInputMethod(previous)` and return success. If missing/empty →
  `Fail("No previous IME recorded in '<var>' — run 'Enable ADB Keyboard' first.")`.

Place Enable near the start of the run and Restore near the end (and/or off the Error Handler) so the
device is never left on ADBKeyboard after a failure.

### 5. Shared IME constant (`AdbCore/Android/AndroidImes.cs`)

New static holding the one fact both the Enable action and the Send Text guard need, so it is defined
once:

```csharp
public static class AndroidImes
{
    /// <summary>The ADBKeyboard IME component id (com.android.adbkeyboard/.AdbIME).</summary>
    public const string AdbKeyboard = "com.android.adbkeyboard/.AdbIME";
}
```

### 6. Command builders (`AdbCore/Android/AdbInputCommand.cs`)

Add unit-testable builders next to the existing `Text`/`KeyEvent`/`LongPress`:

```csharp
// am broadcast -a ADB_INPUT_B64 --es msg '<base64 of UTF-8 text>'
public static string AdbKeyboardText(string text);   // encodes internally; single-quote-wraps the base64
public static string SetIme(string ime)     => $"ime set {ime}";
public static string EnableIme(string ime)  => $"ime enable {ime}";
public static string GetDefaultIme()        => "settings get secure default_input_method";
public static string ListImes()             => "ime list -a -s";   // -a all, -s ids only
```

Base64 (`Convert.ToBase64String(Encoding.UTF8.GetBytes(text))`) is chosen over `ADB_INPUT_TEXT` so the
broadcast argument is pure ASCII (`A–Za-z0-9+/=`) — no Unicode or shell-metacharacter quoting hazard
survives to the device. The message is still single-quote-wrapped for uniformity.

### 7. Device layer (`AdbCore/Android/IAndroidDevice.cs` + `AdvancedSharpAdbDevice.cs`)

Add to the interface and implement as thin shell calls wrapped by the existing stale-handle `Invoke`
retry:

```csharp
string GetInputMethod();                  // settings get secure default_input_method  (trimmed)
bool   IsInputMethodAvailable(string ime); // ime list -a -s contains ime
void   EnableInputMethod(string ime);      // ime enable <ime>
void   SetInputMethod(string ime);         // ime set <ime>
void   SendAdbKeyboardText(string text);   // am broadcast -a ADB_INPUT_B64 --es msg '<base64>'
```

`GetInputMethod` / `IsInputMethodAvailable` must **capture stdout**, which the current fire-and-forget
`Shell` (`AdbClient.ExecuteRemoteCommand(cmd, device)`) does not. Add a `ShellCapture(string): string`
helper using AdvancedSharpAdbClient 3.6.16's output-collecting overload (the
`ExecuteRemoteCommand(cmd, device, IShellOutputReceiver)` receiver form with a collecting receiver such
as `ConsoleOutputReceiver`, then `receiver.ToString()`; confirm the exact receiver type against the
installed package during implementation). `GetInputMethod` trims the single-line result;
`IsInputMethodAvailable` returns whether the (newline-split) `ime list` output contains the id.
The device is the user's live-verified surface, so these five commands are the behaviors to confirm on
real hardware.

### 8. Wiring (the complete slice)

- **Registration** — `BuiltInActions.cs`, in the Android block: register
  `EnableAdbKeyboardAction` and `RestoreKeyboardAction` after `PressKeyAction`.
- **Send Text method field** — no new UI: `ConfigFieldType.Enum` already renders as a themed dropdown
  via `ConfigFieldTemplateSelector` (same as Press Key's `key`).
- **Coordinate picker** — N/A; none of these actions take x/y.

### 9. Tests (`AdbCore.Tests`, mirroring the existing Android/`AdbInputCommand` tests)

Using the hand-rolled `FakeAndroidDevice` (extended to record IME calls / return a scripted active IME
and availability):

- **AdbInputCommand.AdbKeyboardText** — the emitted command is `am broadcast -a ADB_INPUT_B64 --es msg
  '<b64>'`, and base64-decoding `<b64>` as UTF-8 round-trips the input (assert with the superscript
  string `!defender&!¹&…&!⁹`); empty text encodes to an empty payload.
- **AdbInputCommand** IME builders emit the exact `ime set` / `ime enable` /
  `settings get secure default_input_method` / `ime list -a -s` strings.
- **SendText (method=Input Text)** — unchanged behavior (escaped `input text`); default when `method`
  absent is `Input Text`.
- **SendText (method=ADB Keyboard)** — when active IME is ADBKeyboard, calls `SendAdbKeyboardText`
  with the resolved text; when the active IME is something else, returns failure and sends nothing;
  empty text is a success no-op.
- **EnableAdbKeyboard** — stashes the prior IME into `previousImeVar` (default `PreviousIme`), enables
  then sets ADBKeyboard; fails with the install hint when `IsInputMethodAvailable` is false; returns
  `RequiresDevice()` when unbound.
- **RestoreKeyboard** — sets the IME to the value in `previousImeVar`; fails clearly when the variable
  is missing/empty; returns `RequiresDevice()` when unbound.
- **AndroidKeyCodes** — every new `Names` entry resolves via `TryResolve` to the code in the table
  above (guards the single-source-of-truth invariant); `Home Button` ≠ `Home`.

### 10. Documentation (all three surfaces, same unit of work — per the Docs Sync Contract)

- **Wiki `Actions-Reference.md`** — add `Enable ADB Keyboard` and `Restore Keyboard` to the Android
  table; document Send Text's new `Method` field (Input Text vs ADB Keyboard) and that ADB Keyboard
  requires the ADBKeyboard IME installed + an Enable node active; extend the Press Key key list with
  the new names. Add a short "Typing Unicode / non-ASCII on Android" note describing the
  Enable → Send Text(ADB Keyboard) → Restore pattern and the `input text` ASCII limitation.
- **README.md** — two edits, goblin voice, nothing that outruns the code:
  - *The arsenal* (the Android input line): note Send Text can also send Unicode via ADBKeyboard.
  - *Summoning requirements* (the optional per-feature deps list): add a **"Unicode / non-Latin text
    on Android (ADBKeyboard)"** subsection with the concrete install + usage steps, since this is the
    one Android feature that needs a device-side install. Exact copy to apply:

    > **Unicode / non-Latin text on Android (ADBKeyboard)** — Android's built-in `input text` (what
    > plain **Send Text** uses) only speaks plain ASCII; superscripts, emoji, accents, CJK — all
    > silently vanish. To send the weird stuff, install the free **ADBKeyboard** IME and flip Send
    > Text's **Method** to **ADB Keyboard**:
    > 1. Grab `ADBKeyboard.apk` from the [ADBKeyBoard releases](https://github.com/senzhk/ADBKeyBoard/releases).
    > 2. Install it — `adb install ADBKeyboard.apk`, or point the **Install APK** action at the file.
    > 3. Drop an **Enable ADB Keyboard** node (Android) before you type — it switches the device to
    >    ADBKeyboard and remembers the old keyboard.
    > 4. Set your **Send Text** node's **Method** to **ADB Keyboard**. Now `${those_superscripts}`
    >    actually land.
    > 5. Drop a **Restore Keyboard** node when you're done (or hang it off the Error Handler) to give
    >    the phone its normal keyboard back.
    >
    > Check it took: `adb shell ime list -a` should list `com.android.adbkeyboard/.AdbIME`.

  The same install + usage steps belong in the wiki `Actions-Reference.md` "Typing Unicode / non-ASCII
  on Android" note (fuller, plain voice); the README version is the goblin-voiced short form.
- **CLAUDE.md** — add a one-line note under the Android area that Send Text has an `input text` (ASCII)
  vs ADBKeyboard (Unicode) method and that the ADBKeyboard path needs the IME installed + activated by
  the Enable node; the ADBKeyboard IME id lives in `AndroidImes`.

## Execution

Subagent-driven development after the implementation plan is written. The change is AdbCore engine +
data + tests with **no XAML/visual surface** (the `method` dropdown renders through the existing Enum
template), so this is a **backend-only slice**: self-merge after review rather than parking for visual
sign-off. The five new device shell commands cannot be unit-verified against a live phone from here, so
the user's on-device check is the confirmation step for the ADBKeyboard round-trip.
