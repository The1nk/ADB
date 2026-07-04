# Enable ADB Keyboard waits for IME readiness — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make **Enable ADB Keyboard** block until ADBKeyboard is actually the active IME (poll + configurable settle) so no manual Delay is needed before a broadcast.

**Architecture:** `EnableAdbKeyboardAction.ExecuteAsync` becomes async: after `SetInputMethod`, poll `GetInputMethod()` until it reports ADBKeyboard (bounded internal timeout, cancellation-aware), then `await Task.Delay(settleMs)`. Fail clearly on timeout. A public 2-arg ctor overload is the test/tuning seam for fast timeout tests; the registry uses the parameterless ctor. No device-layer change.

**Tech Stack:** C# / .NET 10, xUnit, hand-rolled `FakeAndroidDevice`.

**Spec:** `Docs/superpowers/specs/2026-07-04-enable-adbkeyboard-waits-for-ime.md`

**Execution note:** Backend-only slice (no visual surface) → self-merge after review. Device-side timing is the user's on-device confirmation; the poll/settle/timeout logic is fully fake-tested.

---

## File map

| File | Change |
| --- | --- |
| `AdbCore.Tests/Actions/BuiltIn/Android/FakeAndroidDevice.cs` | Add `SuppressImeActivation` knob |
| `AdbCore/Actions/BuiltIn/Android/EnableAdbKeyboardAction.cs` | Async poll + settle + timeout + `settleMs` config + ctor seam |
| `AdbCore.Tests/Actions/BuiltIn/Android/AndroidInputActionTests.cs` | Update 3 Enable tests; add poll + timeout tests |
| `CLAUDE.md`, `README.md`, `ADB.wiki/Actions-Reference.md` | Docs |

---

## Task 1: FakeAndroidDevice — `SuppressImeActivation` knob

**Files:**
- Modify: `AdbCore.Tests/Actions/BuiltIn/Android/FakeAndroidDevice.cs`

- [ ] **Step 1: Add the knob and gate SetInputMethod**

The fake currently has (among others):
```csharp
    public string ActiveIme { get; set; } = "com.original/.Ime";
    public bool AdbKeyboardInstalled { get; set; } = true;
    ...
    public void SetInputMethod(string ime) { Calls.Add($"imeset {ime}"); ActiveIme = ime; }
```

Add the field next to `AdbKeyboardInstalled`:
```csharp
    /// <summary>When true, SetInputMethod records the call but does NOT flip ActiveIme — simulates a
    /// device whose IME switch never takes, so the Enable action polls to timeout.</summary>
    public bool SuppressImeActivation { get; set; }
```

And change `SetInputMethod` to:
```csharp
    public void SetInputMethod(string ime) { Calls.Add($"imeset {ime}"); if (!SuppressImeActivation) ActiveIme = ime; }
```

- [ ] **Step 2: Build the test project to confirm it compiles**

Run: `dotnet build AdbCore.Tests -clp:ErrorsOnly`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add AdbCore.Tests/Actions/BuiltIn/Android/FakeAndroidDevice.cs
git commit -m "test: FakeAndroidDevice can suppress IME activation for timeout tests"
```
Append the trailer line to the commit body: `Claude-Session: https://claude.ai/code/session_01CmX3RvuoA9B7XJ8WCUoFgU`

---

## Task 2: EnableAdbKeyboardAction — poll + settle + timeout

**Files:**
- Modify: `AdbCore/Actions/BuiltIn/Android/EnableAdbKeyboardAction.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/Android/AndroidInputActionTests.cs`

- [ ] **Step 1: Update the existing Enable tests + add new ones**

In `AndroidInputActionTests.cs`, REPLACE the existing `EnableAdbKeyboard_StashesPreviousAndActivates` test (currently around lines 233-248) with this version (adds `settleMs = 0`, and asserts a poll ran after `imeset`):

```csharp
    [Fact]
    public async Task EnableAdbKeyboard_StashesPreviousAndActivates()
    {
        var action = new BotAction { Config = { ["settleMs"] = 0 } }; // default previousImeVar = "PreviousIme"
        var (ctx, dev) = WithDevice(action);
        dev.ActiveIme = "com.original/.Ime";
        dev.AdbKeyboardInstalled = true;

        var r = await new EnableAdbKeyboardAction().ExecuteAsync(ctx, default);

        Assert.True(r.Success);
        Assert.Equal("com.original/.Ime", ctx.Context.Variables["PreviousIme"]);
        Assert.Contains($"imeenable {AndroidImes.AdbKeyboard}", dev.Calls);
        Assert.Contains($"imeset {AndroidImes.AdbKeyboard}", dev.Calls);
        Assert.Equal(AndroidImes.AdbKeyboard, dev.ActiveIme);
        // Verified activation by polling AFTER issuing the switch (not fire-and-forget):
        Assert.True(dev.Calls.IndexOf($"imeset {AndroidImes.AdbKeyboard}") < dev.Calls.LastIndexOf("getime"));
    }

    [Fact]
    public async Task EnableAdbKeyboard_TimesOut_WhenImeNeverActivates()
    {
        var action = new BotAction { Config = { ["settleMs"] = 0 } };
        var (ctx, dev) = WithDevice(action);
        dev.AdbKeyboardInstalled = true;
        dev.SuppressImeActivation = true; // ime set never "takes"

        var r = await new EnableAdbKeyboardAction(maxWaitMs: 50, pollIntervalMs: 5).ExecuteAsync(ctx, default);

        Assert.False(r.Success);
        Assert.Contains("did not become the active input method", r.ErrorMessage);
        // Prior IME is still stashed before the (failed) switch, so Restore can recover:
        Assert.Equal("com.original/.Ime", ctx.Context.Variables["PreviousIme"]);
    }
```

Then update `EnableAdbKeyboard_NotInstalled_FailsWithHint` and `EnableAdbKeyboard_NoDeviceBound_Fails` to pass `settleMs = 0` so they never wait (they fail before the settle, but keep them consistent). Change their `new BotAction()` / `new BotAction { ... }` to include `Config = { ["settleMs"] = 0 }`:

```csharp
    [Fact]
    public async Task EnableAdbKeyboard_NotInstalled_FailsWithHint()
    {
        var action = new BotAction { Config = { ["settleMs"] = 0 } };
        var (ctx, dev) = WithDevice(action);
        dev.AdbKeyboardInstalled = false;

        var r = await new EnableAdbKeyboardAction().ExecuteAsync(ctx, default);

        Assert.False(r.Success);
        Assert.Contains("ADBKeyboard is not installed", r.ErrorMessage);
        Assert.DoesNotContain(dev.Calls, c => c.StartsWith("imeset"));
    }

    [Fact]
    public async Task EnableAdbKeyboard_NoDeviceBound_Fails()
    {
        var ctx = new BotExecutionContext();
        var exec = new ActionExecutionContext(new BotAction { Config = { ["settleMs"] = 0 } }, ctx, _ => { });

        var r = await new EnableAdbKeyboardAction().ExecuteAsync(exec, default);

        Assert.False(r.Success);
        Assert.Contains("Android", r.ErrorMessage);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~AndroidInputActionTests"`
Expected: FAIL — no `EnableAdbKeyboardAction(int, int)` ctor (compile error) and the timeout message assertion can't be met yet.

- [ ] **Step 3: Rewrite EnableAdbKeyboardAction**

Replace the entire body of `AdbCore/Actions/BuiltIn/Android/EnableAdbKeyboardAction.cs` with:

```csharp
using AdbCore.Android;
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Makes the ADBKeyboard IME active (for Unicode Send Text), waits until the device reports it
/// active plus a short settle, and stashes the previously-active IME id into a run variable so a later
/// Restore Keyboard node can put it back. The wait means no manual Delay is needed before a broadcast.</summary>
public sealed class EnableAdbKeyboardAction : AndroidActionBase
{
    public const string PreviousImeVarKey = "previousImeVar";
    public const string DefaultPreviousImeVar = "PreviousIme";
    public const string SettleMsKey = "settleMs";
    public const int DefaultSettleMs = 400;

    private readonly int _maxWaitMs;
    private readonly int _pollIntervalMs;

    public EnableAdbKeyboardAction() : this(maxWaitMs: 3000, pollIntervalMs: 150) { }

    /// <summary>Test/tuning seam: smaller timings make the timeout path fast to exercise. The action
    /// registry only ever calls the parameterless constructor.</summary>
    public EnableAdbKeyboardAction(int maxWaitMs, int pollIntervalMs)
    {
        _maxWaitMs = maxWaitMs;
        _pollIntervalMs = pollIntervalMs;
    }

    public override string TypeKey => "android.enableAdbKeyboard";
    public override string DisplayName => "Enable ADB Keyboard";
    public override string Description => "Activates the ADBKeyboard IME (for Unicode text), waits for it to become active, and remembers the previous keyboard.";

    public override List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField
        {
            Key = PreviousImeVarKey,
            Label = "Remember Previous IME In",
            Type = ConfigFieldType.String,
            DefaultValue = DefaultPreviousImeVar,
        },
        new ConfigField
        {
            Key = SettleMsKey,
            Label = "Settle (ms)",
            Type = ConfigFieldType.Number,
            DefaultValue = DefaultSettleMs,
        },
    };

    public override async Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return RequiresDevice();
        }

        if (!device.IsInputMethodAvailable(AndroidImes.AdbKeyboard))
        {
            return ActionResult.Fail(
                "ADBKeyboard is not installed on the device. Install the ADBKeyboard APK (e.g. with the Install APK action) and try again.");
        }

        // Capture and stash the prior IME BEFORE switching, so Restore can recover it even if the
        // activation below times out.
        var previous = device.GetInputMethod();
        var varName = ConfigValues.GetString(context.Action.Config, PreviousImeVarKey, DefaultPreviousImeVar);
        if (!string.IsNullOrWhiteSpace(varName))
        {
            context.Context.Variables[varName] = previous;
        }

        device.EnableInputMethod(AndroidImes.AdbKeyboard);
        device.SetInputMethod(AndroidImes.AdbKeyboard);

        // The IME switch is asynchronous ON THE DEVICE: poll until it reports ADBKeyboard active.
        var waited = 0;
        while (!string.Equals(device.GetInputMethod(), AndroidImes.AdbKeyboard, StringComparison.Ordinal))
        {
            if (waited >= _maxWaitMs)
            {
                return ActionResult.Fail(
                    $"ADBKeyboard did not become the active input method within {_maxWaitMs} ms — the device may be slow to switch keyboards; try again or add a Delay.");
            }
            await Task.Delay(_pollIntervalMs, ct);
            waited += _pollIntervalMs;
        }

        // Settle: give ADBKeyboard's broadcast receiver time to register before any broadcast is sent.
        var settleMs = Math.Max(0, ConfigValues.GetInt(context.Action.Config, SettleMsKey, DefaultSettleMs));
        if (settleMs > 0)
        {
            await Task.Delay(settleMs, ct);
        }

        return ActionResult.Ok(SuccessPort);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~AndroidInputActionTests"`
Expected: PASS (all Enable tests, plus the untouched Send Text / Restore / Press Key tests in the same class).

- [ ] **Step 5: Run the full AdbCore.Tests suite (no regressions)**

Run: `dotnet test AdbCore.Tests`
Expected: PASS, 0 failures. (The registration test still constructs `new EnableAdbKeyboardAction()` via `BuiltInActions`, which uses the parameterless ctor — unaffected.)

- [ ] **Step 6: Commit**

```bash
git add AdbCore/Actions/BuiltIn/Android/EnableAdbKeyboardAction.cs AdbCore.Tests/Actions/BuiltIn/Android/AndroidInputActionTests.cs
git commit -m "Android: Enable ADB Keyboard waits for IME activation + configurable settle"
```
Append the trailer: `Claude-Session: https://claude.ai/code/session_01CmX3RvuoA9B7XJ8WCUoFgU`

---

## Task 3: Documentation (all three surfaces)

**Files:**
- Modify: `CLAUDE.md`
- Modify: `README.md`
- Modify: `ADB.wiki/Actions-Reference.md` (wiki submodule — commit + push there, then bump the pointer)

- [ ] **Step 1: CLAUDE.md**

In the **Android Driver** row (the one describing Send Text methods + Enable/Restore), append a sentence:

> The **Enable ADB Keyboard** node now **waits until the device reports ADBKeyboard active** (bounded ~3 s poll) plus a configurable **Settle (ms)** (`settleMs`, default 400) so a following broadcast/Send Text isn't dropped — no manual Delay needed.

- [ ] **Step 2: README.md**

In the *Summoning requirements* ADBKeyboard steps, step 3 currently reads "Drop an **Enable ADB Keyboard** node (Android) before you type — it switches the device to ADBKeyboard and remembers the old keyboard." Extend it:

> 3. Drop an **Enable ADB Keyboard** node (Android) before you type — it switches the device to ADBKeyboard, **waits until the keyboard is actually ready** (so you don't need a manual Delay), and remembers the old keyboard.

- [ ] **Step 3: Wiki Actions-Reference.md**

In the **Enable ADB Keyboard** row, change the Config cell to add `settleMs` and note the wait:

> `previousImeVar` (str, `PreviousIme`), `settleMs` (num, `400`)

and in its Notes cell append: "Waits until ADBKeyboard reports active (bounded ~3 s) then settles `settleMs` so a following broadcast/Send Text isn't dropped." Also update the "Typing Unicode / non-ASCII on Android" section step 2 to mention Enable waits for readiness (drop the implication that a manual delay might be needed).

- [ ] **Step 4: Commit main-repo docs**

```bash
git add CLAUDE.md README.md
git commit -m "Docs: Enable ADB Keyboard waits for IME activation + settleMs"
```
Append the trailer: `Claude-Session: https://claude.ai/code/session_01CmX3RvuoA9B7XJ8WCUoFgU`

- [ ] **Step 5: Wiki commit + push + pointer bump**

The wiki working copy lives in the MAIN checkout (`C:/git/ADB/ADB.wiki`), not this worktree. Defer the wiki edit + push + submodule-pointer bump to merge time (same as the previous ADBKeyboard slice), OR perform it against the main checkout after this branch merges. Note this in the PR/merge summary so it isn't dropped. (Do not push the wiki from an unmerged branch.)

---

## Final verification

- [ ] `dotnet test AdbCore.Tests` → all green (the Enable tests run in milliseconds — no real 400 ms/3 s waits).
- [ ] `dotnet build ADB.slnx -clp:ErrorsOnly` → clean.
- [ ] **On-device (user):** build Enable ADB Keyboard → (no Delay) → Send Text `${SearchString}` (ADB Keyboard) or a Lua `ADB_CLEAR_TEXT` broadcast → confirm it lands without a manual Delay; try lowering/raising **Settle (ms)** if a slow device still drops the first broadcast.
