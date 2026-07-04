# Android Unicode text entry (ADBKeyboard) + Press Key key expansion — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let Android **Send Text** inject arbitrary Unicode (e.g. superscript digits) via the ADBKeyboard IME, add explicit **Enable/Restore ADB Keyboard** nodes, and expand the **Press Key** dropdown with 16 clipboard/navigation/system keys.

**Architecture:** All work is in `AdbCore` (engine) + tests. Send Text gains an `Input Text | ADB Keyboard` method enum (mirroring the Click/Type `method` pattern); the ADB-Keyboard path base64-encodes UTF-8 and broadcasts `ADB_INPUT_B64`, guarding that ADBKeyboard is the active IME so it never silently no-ops. Two new leaf actions manage the IME, handing the previous IME id through a run variable. No XAML — the enum renders via the existing `ConfigFieldType.Enum` template.

**Tech Stack:** C# / .NET 10, xUnit, AdvancedSharpAdbClient 3.6.16, hand-rolled test fakes (no mocking framework).

**Spec:** `Docs/superpowers/specs/2026-07-04-android-adbkeyboard-unicode-input-design.md`

**Execution note:** Backend-only slice (no visual surface) → self-merge after review per the project workflow. The five new device shell commands in `AdvancedSharpAdbDevice` cannot be unit-verified against a live phone; unit tests cover everything via the fakes, and the user confirms the on-device round-trip.

---

## File map

| File | Change |
| --- | --- |
| `AdbCore/Android/AndroidKeyCodes.cs` | Append 16 key entries |
| `AdbCore.Tests/Android/AndroidKeyCodesTests.cs` | Update count, add new-key cases |
| `AdbCore/Android/AdbInputCommand.cs` | Add `AdbKeyboardText` + IME command builders |
| `AdbCore.Tests/Android/AdbInputCommandTests.cs` | Tests for the new builders |
| `AdbCore/Android/AndroidImes.cs` | **Create** — the ADBKeyboard IME id constant |
| `AdbCore/Android/IAndroidDevice.cs` | Add 5 methods |
| `AdbCore/Android/AdvancedSharpAdbDevice.cs` | Implement the 5 methods + `ShellCapture` |
| `AdbCore.Tests/Actions/BuiltIn/Android/FakeAndroidDevice.cs` | Implement the 5 methods (recording) |
| `BotCapture.Core.Tests/Fakes.cs` | Implement the 5 methods (minimal stubs) |
| `AdbCore/Actions/BuiltIn/Android/SendTextAction.cs` | Add `method` field + ADB-Keyboard branch |
| `AdbCore/Actions/BuiltIn/Android/EnableAdbKeyboardAction.cs` | **Create** |
| `AdbCore/Actions/BuiltIn/Android/RestoreKeyboardAction.cs` | **Create** |
| `AdbCore/Actions/BuiltIn/BuiltInActions.cs` | Register the two new actions |
| `AdbCore.Tests/Actions/BuiltIn/Android/AndroidInputActionTests.cs` | Send Text method tests + Enable/Restore tests |
| `AdbCore.Tests/Actions/BuiltIn/Android/AndroidInputRegistrationTests.cs` | Register-check the two new type keys |
| `CLAUDE.md`, `README.md`, `ADB.wiki/Actions-Reference.md` | Docs (same unit of work) |

---

## Task 1: Press Key — add 16 keys

**Files:**
- Modify: `AdbCore/Android/AndroidKeyCodes.cs` (the `Entries` array, ends line 24)
- Test: `AdbCore.Tests/Android/AndroidKeyCodesTests.cs`

- [ ] **Step 1: Update the failing tests**

In `AndroidKeyCodesTests.cs`, add these `[InlineData]` rows to the existing `TryResolve_KnownName_ReturnsCode` theory (after the `Escape` row):

```csharp
    [InlineData("Paste", 279)]
    [InlineData("Copy", 278)]
    [InlineData("Cut", 277)]
    [InlineData("Home Button", 3)]
    [InlineData("Back", 4)]
    [InlineData("Recent Apps", 187)]
    [InlineData("Menu", 82)]
    [InlineData("Search", 84)]
    [InlineData("Page Up", 92)]
    [InlineData("Page Down", 93)]
    [InlineData("Power", 26)]
    [InlineData("Wake", 224)]
    [InlineData("Sleep", 223)]
    [InlineData("Volume Up", 24)]
    [InlineData("Volume Down", 25)]
    [InlineData("Mute", 164)]
```

And replace the count assertion in `Names_AreOrdered_AndEveryNameResolves` (currently `Assert.Equal(12, …)`):

```csharp
        Assert.Equal("Backspace", AndroidKeyCodes.Names[0]);
        Assert.Equal(28, AndroidKeyCodes.Names.Count);
        Assert.Contains("Paste", AndroidKeyCodes.Names);
        Assert.DoesNotContain("Home Button", new[] { "Home" }); // "Home Button" is distinct from cursor "Home"
        Assert.All(AndroidKeyCodes.Names, n => Assert.True(AndroidKeyCodes.TryResolve(n, out _)));
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~AndroidKeyCodesTests"`
Expected: FAIL — new names don't resolve, count is 12 not 28.

- [ ] **Step 3: Append the entries**

In `AndroidKeyCodes.cs`, add to the `Entries` array immediately after the `("Escape", 111)` line:

```csharp
        // Text editing (API 24+)
        ("Paste", 279),         // KEYCODE_PASTE
        ("Copy", 278),          // KEYCODE_COPY
        ("Cut", 277),           // KEYCODE_CUT
        // Navigation — "Home Button" is the device home key (distinct from the cursor "Home" above)
        ("Home Button", 3),     // KEYCODE_HOME
        ("Back", 4),            // KEYCODE_BACK
        ("Recent Apps", 187),   // KEYCODE_APP_SWITCH
        ("Menu", 82),           // KEYCODE_MENU
        ("Search", 84),         // KEYCODE_SEARCH
        ("Page Up", 92),        // KEYCODE_PAGE_UP
        ("Page Down", 93),      // KEYCODE_PAGE_DOWN
        // System / power
        ("Power", 26),          // KEYCODE_POWER
        ("Wake", 224),          // KEYCODE_WAKEUP
        ("Sleep", 223),         // KEYCODE_SLEEP
        ("Volume Up", 24),      // KEYCODE_VOLUME_UP
        ("Volume Down", 25),    // KEYCODE_VOLUME_DOWN
        ("Mute", 164),          // KEYCODE_VOLUME_MUTE
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~AndroidKeyCodesTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Android/AndroidKeyCodes.cs AdbCore.Tests/Android/AndroidKeyCodesTests.cs
git commit -m "Android: add Paste/Copy/Cut, navigation, and system/power keys to Press Key"
```

---

## Task 2: AdbInputCommand — base64 broadcast + IME builders

**Files:**
- Modify: `AdbCore/Android/AdbInputCommand.cs`
- Test: `AdbCore.Tests/Android/AdbInputCommandTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `AdbInputCommandTests.cs`:

```csharp
    [Fact]
    public void AdbKeyboardText_BroadcastsBase64Utf8()
    {
        const string prefix = "am broadcast -a ADB_INPUT_B64 --es msg '";
        var cmd = AdbInputCommand.AdbKeyboardText("¹²³");
        Assert.StartsWith(prefix, cmd);
        Assert.EndsWith("'", cmd);
        var b64 = cmd[prefix.Length..^1];
        var decoded = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(b64));
        Assert.Equal("¹²³", decoded);
    }

    [Fact]
    public void AdbKeyboardText_Empty_EncodesEmptyPayload()
        => Assert.Equal("am broadcast -a ADB_INPUT_B64 --es msg ''", AdbInputCommand.AdbKeyboardText(""));

    [Fact]
    public void ImeCommands_AreExact()
    {
        Assert.Equal("ime set com.android.adbkeyboard/.AdbIME", AdbInputCommand.SetIme("com.android.adbkeyboard/.AdbIME"));
        Assert.Equal("ime enable com.android.adbkeyboard/.AdbIME", AdbInputCommand.EnableIme("com.android.adbkeyboard/.AdbIME"));
        Assert.Equal("settings get secure default_input_method", AdbInputCommand.GetDefaultIme());
        Assert.Equal("ime list -a -s", AdbInputCommand.ListImes());
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~AdbInputCommandTests"`
Expected: FAIL — methods not defined (won't compile).

- [ ] **Step 3: Add the builders**

In `AdbInputCommand.cs`, add `using System.Text;` at the top (next to `using System.Globalization;`), then add these methods inside the class (before the private `SingleQuote`):

```csharp
    /// <summary>Broadcasts text to the ADBKeyboard IME as base64-encoded UTF-8 (<c>ADB_INPUT_B64</c>), so
    /// arbitrary Unicode passes through without any shell-quoting or encoding hazard. Requires the
    /// ADBKeyboard IME to be installed and active on the device.</summary>
    public static string AdbKeyboardText(string text)
        => $"am broadcast -a ADB_INPUT_B64 --es msg {SingleQuote(Convert.ToBase64String(Encoding.UTF8.GetBytes(text ?? string.Empty)))}";

    /// <summary>Makes <paramref name="ime"/> the active input method (<c>ime set</c>).</summary>
    public static string SetIme(string ime) => $"ime set {ime}";

    /// <summary>Enables <paramref name="ime"/> so it can be activated (<c>ime enable</c>).</summary>
    public static string EnableIme(string ime) => $"ime enable {ime}";

    /// <summary>Reads the currently-active IME id from secure settings.</summary>
    public static string GetDefaultIme() => "settings get secure default_input_method";

    /// <summary>Lists all installed IME ids, one per line (<c>-a</c> all, <c>-s</c> ids only).</summary>
    public static string ListImes() => "ime list -a -s";
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~AdbInputCommandTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Android/AdbInputCommand.cs AdbCore.Tests/Android/AdbInputCommandTests.cs
git commit -m "Android: add ADBKeyboard base64 broadcast + ime command builders"
```

---

## Task 3: AndroidImes constant

**Files:**
- Create: `AdbCore/Android/AndroidImes.cs`

- [ ] **Step 1: Create the file**

```csharp
namespace AdbCore.Android;

/// <summary>Known Android input-method (IME) component ids. Single source of truth so the Enable
/// action and the Send Text ADB-Keyboard guard reference the same string.</summary>
public static class AndroidImes
{
    /// <summary>The ADBKeyboard IME component id — the Unicode-capable keyboard used by Send Text's
    /// "ADB Keyboard" method (github.com/senzhk/ADBKeyBoard).</summary>
    public const string AdbKeyboard = "com.android.adbkeyboard/.AdbIME";
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build AdbCore -clp:ErrorsOnly`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add AdbCore/Android/AndroidImes.cs
git commit -m "Android: add AndroidImes.AdbKeyboard IME id constant"
```

---

## Task 4: IAndroidDevice — IME query/switch + Unicode broadcast

Adding methods to the interface breaks all three implementers, so update the interface and all three in one task to keep the build green. The `AdvancedSharpAdbDevice` implementation is real device code (verified on hardware, not unit-tested); the two fakes make the unit tests compile.

**Files:**
- Modify: `AdbCore/Android/IAndroidDevice.cs`
- Modify: `AdbCore/Android/AdvancedSharpAdbDevice.cs`
- Modify: `AdbCore.Tests/Actions/BuiltIn/Android/FakeAndroidDevice.cs`
- Modify: `BotCapture.Core.Tests/Fakes.cs`

- [ ] **Step 1: Extend the interface**

In `IAndroidDevice.cs`, add before `byte[] Screenshot();`:

```csharp
    /// <summary>Reads the currently-active IME id (secure setting <c>default_input_method</c>).</summary>
    string GetInputMethod();

    /// <summary>True if <paramref name="ime"/> appears in the device's installed IME list.</summary>
    bool IsInputMethodAvailable(string ime);

    /// <summary>Enables <paramref name="ime"/> so it can be set active (<c>ime enable</c>).</summary>
    void EnableInputMethod(string ime);

    /// <summary>Makes <paramref name="ime"/> the active input method (<c>ime set</c>).</summary>
    void SetInputMethod(string ime);

    /// <summary>Types Unicode text via the active ADBKeyboard IME (base64 <c>ADB_INPUT_B64</c> broadcast).</summary>
    void SendAdbKeyboardText(string text);
```

- [ ] **Step 2: Implement in AdvancedSharpAdbDevice**

In `AdvancedSharpAdbDevice.cs`, add a `using AdvancedSharpAdbClient.Receivers;` at the top (with the other `using AdvancedSharpAdbClient...` lines), and add after the existing `KeyEvent` method (line 58):

```csharp
    // Output-capturing shell: the fire-and-forget Shell(...) above discards stdout, but reading the
    // active IME needs it. 3.6.16: ExecuteRemoteCommand(command, device, IShellOutputReceiver) collects
    // into the receiver; ConsoleOutputReceiver.ToString() returns the accumulated text.
    private string ShellCapture(string command)
    {
        var receiver = new ConsoleOutputReceiver();
        Invoke(d => _client.ExecuteRemoteCommand(command, d, receiver));
        return receiver.ToString()?.Trim() ?? string.Empty;
    }

    public string GetInputMethod() => ShellCapture(AdbInputCommand.GetDefaultIme());

    public bool IsInputMethodAvailable(string ime)
        => ShellCapture(AdbInputCommand.ListImes())
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.Trim() == ime);

    public void EnableInputMethod(string ime) => Shell(AdbInputCommand.EnableIme(ime));

    public void SetInputMethod(string ime) => Shell(AdbInputCommand.SetIme(ime));

    public void SendAdbKeyboardText(string text) => Shell(AdbInputCommand.AdbKeyboardText(text));
```

> **Verify against the installed package (3.6.16):** confirm `ConsoleOutputReceiver`'s namespace and the
> `ExecuteRemoteCommand(string, DeviceData, IShellOutputReceiver)` overload via IntelliSense/build. If
> the receiver type or overload differs, adapt this one method — it is the sole device-surface unknown
> flagged in the spec. Everything else is package-independent.

- [ ] **Step 3: Implement in the AdbCore.Tests fake (recording)**

In `AdbCore.Tests/Actions/BuiltIn/Android/FakeAndroidDevice.cs`, add fields + methods:

```csharp
    public string ActiveIme { get; set; } = "com.original/.Ime";
    public bool AdbKeyboardInstalled { get; set; } = true;

    public string GetInputMethod() { Calls.Add("getime"); return ActiveIme; }
    public bool IsInputMethodAvailable(string ime) { Calls.Add($"imeavail {ime}"); return AdbKeyboardInstalled; }
    public void EnableInputMethod(string ime) => Calls.Add($"imeenable {ime}");
    public void SetInputMethod(string ime) { Calls.Add($"imeset {ime}"); ActiveIme = ime; }
    public void SendAdbKeyboardText(string text) => Calls.Add($"adbkbtext {text}");
```

- [ ] **Step 4: Implement in the BotCapture.Core.Tests fake (minimal stubs)**

In `BotCapture.Core.Tests/Fakes.cs`, add to the `FakeAndroidDevice` class (line 70) — this fake only needs to compile:

```csharp
    public string GetInputMethod() => string.Empty;
    public bool IsInputMethodAvailable(string ime) => false;
    public void EnableInputMethod(string ime) { }
    public void SetInputMethod(string ime) { }
    public void SendAdbKeyboardText(string text) { }
```

- [ ] **Step 5: Build the whole solution**

Run: `dotnet build ADB.slnx -clp:ErrorsOnly`
Expected: build succeeds (all three implementers satisfy the interface).

- [ ] **Step 6: Commit**

```bash
git add AdbCore/Android/IAndroidDevice.cs AdbCore/Android/AdvancedSharpAdbDevice.cs AdbCore.Tests/Actions/BuiltIn/Android/FakeAndroidDevice.cs BotCapture.Core.Tests/Fakes.cs
git commit -m "Android: IAndroidDevice gains IME query/switch + ADBKeyboard broadcast"
```

---

## Task 5: Send Text — `Method` field + ADB-Keyboard branch

**Files:**
- Modify: `AdbCore/Actions/BuiltIn/Android/SendTextAction.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/Android/AndroidInputActionTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `AndroidInputActionTests.cs` (the `WithDevice` helper and `AndroidImes` are available; add `using AdbCore.Android;` to the file if not present):

```csharp
    [Fact]
    public async Task SendText_AdbKeyboard_WhenActive_BroadcastsUnicode()
    {
        var action = new BotAction { Config = { ["text"] = "!¹&!²", ["method"] = "ADB Keyboard" } };
        var (ctx, dev) = WithDevice(action);
        dev.ActiveIme = AndroidImes.AdbKeyboard; // Enable node already ran

        var r = await new SendTextAction().ExecuteAsync(ctx, default);

        Assert.True(r.Success);
        Assert.Equal("getime", dev.Calls[0]);
        Assert.Equal("adbkbtext !¹&!²", dev.Calls[1]);
    }

    [Fact]
    public async Task SendText_AdbKeyboard_WhenNotActive_FailsWithoutSending()
    {
        var action = new BotAction { Config = { ["text"] = "hi", ["method"] = "ADB Keyboard" } };
        var (ctx, dev) = WithDevice(action);
        dev.ActiveIme = "com.other/.Ime";

        var r = await new SendTextAction().ExecuteAsync(ctx, default);

        Assert.False(r.Success);
        Assert.Contains("Enable ADB Keyboard", r.ErrorMessage);
        Assert.DoesNotContain(dev.Calls, c => c.StartsWith("adbkbtext"));
    }

    [Fact]
    public async Task SendText_DefaultMethod_UsesInputText()
    {
        var action = new BotAction { Config = { ["text"] = "hello" } }; // no method key
        var (ctx, dev) = WithDevice(action);

        await new SendTextAction().ExecuteAsync(ctx, default);

        Assert.Equal("text hello", dev.Calls.Single());
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~AndroidInputActionTests"`
Expected: FAIL — the ADB-Keyboard branch doesn't exist yet.

- [ ] **Step 3: Rewrite SendTextAction**

Replace the body of `AdbCore/Actions/BuiltIn/Android/SendTextAction.cs` with:

```csharp
using AdbCore.Android;
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Types text into the focused field. "Input Text" uses <c>input text</c> (ASCII only);
/// "ADB Keyboard" broadcasts base64 to the ADBKeyboard IME for full Unicode. Empty text is a no-op.</summary>
public sealed class SendTextAction : AndroidActionBase
{
    public const string MethodInputText = "Input Text";
    public const string MethodAdbKeyboard = "ADB Keyboard";

    public override string TypeKey => "android.sendText";
    public override string DisplayName => "Send Text";
    public override string Description => "Types text into the focused field on the device.";

    public override List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = "text", Label = "Text", Type = ConfigFieldType.MultilineString },
        new ConfigField
        {
            Key = "method",
            Label = "Method",
            Type = ConfigFieldType.Enum,
            DefaultValue = MethodInputText,
            Options = new List<string> { MethodInputText, MethodAdbKeyboard },
        },
    };

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return Task.FromResult(RequiresDevice());
        }

        var text = ConfigValues.GetString(context.Action.Config, "text");
        if (string.IsNullOrEmpty(text))
        {
            return Task.FromResult(ActionResult.Ok(SuccessPort)); // empty is a no-op, either method
        }

        var method = ConfigValues.GetString(context.Action.Config, "method", MethodInputText);
        if (string.Equals(method, MethodAdbKeyboard, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(device.GetInputMethod(), AndroidImes.AdbKeyboard, StringComparison.Ordinal))
            {
                return Task.FromResult(ActionResult.Fail(
                    "Send Text (ADB Keyboard) requires an 'Enable ADB Keyboard' node earlier in the bot — ADBKeyboard is not the active input method."));
            }

            device.SendAdbKeyboardText(text);
        }
        else
        {
            device.SendText(text);
        }

        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~AndroidInputActionTests"`
Expected: PASS — including the pre-existing `SendText_*` tests (empty no-op, single space, raw text) which still hold because the default method is `Input Text`.

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Actions/BuiltIn/Android/SendTextAction.cs AdbCore.Tests/Actions/BuiltIn/Android/AndroidInputActionTests.cs
git commit -m "Android: Send Text gains Input Text | ADB Keyboard method (Unicode via base64 broadcast)"
```

---

## Task 6: Enable ADB Keyboard action

**Files:**
- Create: `AdbCore/Actions/BuiltIn/Android/EnableAdbKeyboardAction.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/Android/AndroidInputActionTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `AndroidInputActionTests.cs`:

```csharp
    [Fact]
    public async Task EnableAdbKeyboard_StashesPreviousAndActivates()
    {
        var action = new BotAction(); // default previousImeVar = "PreviousIme"
        var (ctx, dev) = WithDevice(action);
        dev.ActiveIme = "com.original/.Ime";
        dev.AdbKeyboardInstalled = true;

        var r = await new EnableAdbKeyboardAction().ExecuteAsync(ctx, default);

        Assert.True(r.Success);
        Assert.Equal("com.original/.Ime", ctx.Context.Variables["PreviousIme"]);
        Assert.Contains($"imeenable {AndroidImes.AdbKeyboard}", dev.Calls);
        Assert.Contains($"imeset {AndroidImes.AdbKeyboard}", dev.Calls);
        Assert.Equal(AndroidImes.AdbKeyboard, dev.ActiveIme);
    }

    [Fact]
    public async Task EnableAdbKeyboard_NotInstalled_FailsWithHint()
    {
        var action = new BotAction();
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
        var exec = new ActionExecutionContext(new BotAction(), ctx, _ => { });

        var r = await new EnableAdbKeyboardAction().ExecuteAsync(exec, default);

        Assert.False(r.Success);
        Assert.Contains("Android", r.ErrorMessage);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~AndroidInputActionTests"`
Expected: FAIL — `EnableAdbKeyboardAction` not defined.

- [ ] **Step 3: Create the action**

```csharp
using AdbCore.Android;
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Makes the ADBKeyboard IME active (for Unicode Send Text) and stashes the previously-active
/// IME id into a run variable so <see cref="RestoreKeyboardAction"/> can put it back.</summary>
public sealed class EnableAdbKeyboardAction : AndroidActionBase
{
    public const string PreviousImeVarKey = "previousImeVar";
    public const string DefaultPreviousImeVar = "PreviousIme";

    public override string TypeKey => "android.enableAdbKeyboard";
    public override string DisplayName => "Enable ADB Keyboard";
    public override string Description => "Activates the ADBKeyboard IME (for Unicode text) and remembers the previous keyboard.";

    public override List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField
        {
            Key = PreviousImeVarKey,
            Label = "Remember Previous IME In",
            Type = ConfigFieldType.String,
            DefaultValue = DefaultPreviousImeVar,
        },
    };

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return Task.FromResult(RequiresDevice());
        }

        if (!device.IsInputMethodAvailable(AndroidImes.AdbKeyboard))
        {
            return Task.FromResult(ActionResult.Fail(
                "ADBKeyboard is not installed on the device. Install the ADBKeyboard APK (e.g. with the Install APK action) and try again."));
        }

        var previous = device.GetInputMethod();
        device.EnableInputMethod(AndroidImes.AdbKeyboard);
        device.SetInputMethod(AndroidImes.AdbKeyboard);

        var varName = ConfigValues.GetString(context.Action.Config, PreviousImeVarKey, DefaultPreviousImeVar);
        if (!string.IsNullOrWhiteSpace(varName))
        {
            context.Context.Variables[varName] = previous;
        }

        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~AndroidInputActionTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Actions/BuiltIn/Android/EnableAdbKeyboardAction.cs AdbCore.Tests/Actions/BuiltIn/Android/AndroidInputActionTests.cs
git commit -m "Android: add Enable ADB Keyboard action"
```

---

## Task 7: Restore Keyboard action

**Files:**
- Create: `AdbCore/Actions/BuiltIn/Android/RestoreKeyboardAction.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/Android/AndroidInputActionTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `AndroidInputActionTests.cs`:

```csharp
    [Fact]
    public async Task RestoreKeyboard_SetsImeFromVariable()
    {
        var action = new BotAction(); // default previousImeVar = "PreviousIme"
        var (ctx, dev) = WithDevice(action);
        ctx.Context.Variables["PreviousIme"] = "com.original/.Ime";

        var r = await new RestoreKeyboardAction().ExecuteAsync(ctx, default);

        Assert.True(r.Success);
        Assert.Equal("imeset com.original/.Ime", dev.Calls.Single());
    }

    [Fact]
    public async Task RestoreKeyboard_NoVariable_FailsClearly()
    {
        var action = new BotAction();
        var (ctx, dev) = WithDevice(action);

        var r = await new RestoreKeyboardAction().ExecuteAsync(ctx, default);

        Assert.False(r.Success);
        Assert.Contains("PreviousIme", r.ErrorMessage);
        Assert.Empty(dev.Calls);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~AndroidInputActionTests"`
Expected: FAIL — `RestoreKeyboardAction` not defined.

- [ ] **Step 3: Create the action**

```csharp
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Restores the input method saved by <see cref="EnableAdbKeyboardAction"/>, reading the IME id
/// from the named run variable.</summary>
public sealed class RestoreKeyboardAction : AndroidActionBase
{
    public const string PreviousImeVarKey = "previousImeVar";
    public const string DefaultPreviousImeVar = "PreviousIme";

    public override string TypeKey => "android.restoreKeyboard";
    public override string DisplayName => "Restore Keyboard";
    public override string Description => "Restores the input method saved by Enable ADB Keyboard.";

    public override List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField
        {
            Key = PreviousImeVarKey,
            Label = "Previous IME Variable",
            Type = ConfigFieldType.String,
            DefaultValue = DefaultPreviousImeVar,
        },
    };

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return Task.FromResult(RequiresDevice());
        }

        var varName = ConfigValues.GetString(context.Action.Config, PreviousImeVarKey, DefaultPreviousImeVar);
        context.Context.Variables.TryGetValue(varName, out var stored);
        var ime = stored as string ?? stored?.ToString();
        if (string.IsNullOrWhiteSpace(ime))
        {
            return Task.FromResult(ActionResult.Fail(
                $"No previous IME recorded in '{varName}' — run 'Enable ADB Keyboard' first."));
        }

        device.SetInputMethod(ime);
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~AndroidInputActionTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Actions/BuiltIn/Android/RestoreKeyboardAction.cs AdbCore.Tests/Actions/BuiltIn/Android/AndroidInputActionTests.cs
git commit -m "Android: add Restore Keyboard action"
```

---

## Task 8: Register the two new actions

**Files:**
- Modify: `AdbCore/Actions/BuiltIn/BuiltInActions.cs:62` (after `PressKeyAction`)
- Test: `AdbCore.Tests/Actions/BuiltIn/Android/AndroidInputRegistrationTests.cs`

- [ ] **Step 1: Extend the registration test**

In `AndroidInputRegistrationTests.cs`, add two `[InlineData]` rows to the theory:

```csharp
    [InlineData("android.enableAdbKeyboard")]
    [InlineData("android.restoreKeyboard")]
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~AndroidInputRegistrationTests"`
Expected: FAIL — the two type keys aren't registered.

- [ ] **Step 3: Register them**

In `BuiltInActions.cs`, immediately after the `Add(new PressKeyAction(), definitions, executors);` line:

```csharp
        Add(new EnableAdbKeyboardAction(), definitions, executors);
        Add(new RestoreKeyboardAction(), definitions, executors);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~AndroidInputRegistrationTests"`
Expected: PASS.

- [ ] **Step 5: Run the whole AdbCore.Tests suite**

Run: `dotnet test AdbCore.Tests`
Expected: PASS (0 failures) — no regressions across the Android/serialization/execution suites.

- [ ] **Step 6: Commit**

```bash
git add AdbCore/Actions/BuiltIn/BuiltInActions.cs AdbCore.Tests/Actions/BuiltIn/Android/AndroidInputRegistrationTests.cs
git commit -m "Android: register Enable/Restore ADB Keyboard actions"
```

---

## Task 9: Documentation (all three surfaces)

Per the Docs Sync Contract, docs ship in the same unit of work. Ground every claim in what Tasks 1–8 actually built.

**Files:**
- Modify: `CLAUDE.md`
- Modify: `README.md`
- Modify: `ADB.wiki/Actions-Reference.md` (the wiki submodule — commit + push there, then bump the pointer)

- [ ] **Step 1: CLAUDE.md**

Find the Android module description (the "Android Driver" row / Android actions area) and add a one-line note:

> Send Text has two methods: **Input Text** (`input text`, ASCII only) and **ADB Keyboard** (base64
> `ADB_INPUT_B64` broadcast for Unicode). The ADB-Keyboard path requires the ADBKeyboard IME installed
> and made active by the **Enable ADB Keyboard** node (which stashes the prior IME for **Restore
> Keyboard**); its component id lives in `AndroidImes.AdbKeyboard`.

- [ ] **Step 2: README.md — the arsenal line**

In the *Input & windows* bullet (line 42), extend the Android input clause to mention Unicode, keeping the goblin voice, e.g.:

> …and on Android: tap, long-press, swipe, send text (plain, or **full Unicode via ADBKeyboard**), and hammer a key (Backspace ×50 to nuke a field). The clicky-clicky.

- [ ] **Step 3: README.md — Summoning requirements subsection**

Under *Summoning requirements* (after the Android/Browser optional-deps bullet, before "Missing a dependency?"), add:

```markdown
  - **Unicode / non-Latin text on Android (ADBKeyboard)** — Android's built-in `input text` (what plain **Send Text** uses) only speaks plain ASCII; superscripts, emoji, accents, CJK — all silently vanish. To send the weird stuff, install the free **ADBKeyboard** IME and flip Send Text's **Method** to **ADB Keyboard**:
    1. Grab `ADBKeyboard.apk` from the [ADBKeyBoard releases](https://github.com/senzhk/ADBKeyBoard/releases).
    2. Install it — `adb install ADBKeyboard.apk`, or point the **Install APK** action at the file.
    3. Drop an **Enable ADB Keyboard** node (Android) before you type — it switches the device to ADBKeyboard and remembers the old keyboard.
    4. Set your **Send Text** node's **Method** to **ADB Keyboard**. Now `${those_superscripts}` actually land.
    5. Drop a **Restore Keyboard** node when you're done (or hang it off the Error Handler) to give the phone its normal keyboard back.

    Check it took: `adb shell ime list -a` should list `com.android.adbkeyboard/.AdbIME`.
```

- [ ] **Step 4: Wiki Actions-Reference.md**

In the wiki working copy (`ADB.wiki/Actions-Reference.md`):
- Add rows to the **Android** actions table for **Enable ADB Keyboard** (`android.enableAdbKeyboard`, config `Remember Previous IME In`) and **Restore Keyboard** (`android.restoreKeyboard`, config `Previous IME Variable`).
- Document Send Text's new **Method** field: `Input Text` (default, `input text`, ASCII) vs `ADB Keyboard` (Unicode via `ADB_INPUT_B64`; requires ADBKeyboard installed + an Enable node active; fails clearly otherwise).
- Extend the Press Key key-name list with: Paste, Copy, Cut, Home Button, Back, Recent Apps, Menu, Search, Page Up, Page Down, Power, Wake, Sleep, Volume Up, Volume Down, Mute.
- Add a **"Typing Unicode / non-ASCII on Android"** note (plain voice) describing the `input text` ASCII limitation and the Enable → Send Text (ADB Keyboard) → Restore pattern, mirroring the README install steps.

- [ ] **Step 5: Commit main-repo docs**

```bash
git add CLAUDE.md README.md
git commit -m "Docs: document Send Text ADB Keyboard method, Enable/Restore IME nodes, and new Press Key keys"
```

- [ ] **Step 6: Commit + push the wiki, bump the submodule pointer**

```bash
cd ADB.wiki
git add Actions-Reference.md
git commit -m "Document ADBKeyboard Unicode Send Text, Enable/Restore Keyboard, and expanded Press Key keys"
git push
cd ..
git add ADB.wiki
git commit -m "Docs: bump wiki pointer for ADBKeyboard Unicode input"
```

---

## Final verification

- [ ] Run the full solution test suite: `dotnet test ADB.slnx` → all green.
- [ ] `dotnet build ADB.slnx -clp:ErrorsOnly` → clean.
- [ ] **On-device (user):** install ADBKeyboard, build a mini bot — Enable ADB Keyboard → Tap search field → Send Text `${SearchString}` (Method = ADB Keyboard) → Restore Keyboard — and confirm the superscript search string lands in Pokémon GO's search box.
