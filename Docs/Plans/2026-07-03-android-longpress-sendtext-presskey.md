# Android Long Press, Send Text & Press Key — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add three Android actions — Long Press, Send Text, and Press Key (with a named-key dropdown so a field can be cleared with Backspace × N) — plus a documentation fix listing the accepted `VirtualKeys` names.

**Architecture:** Three new `AndroidActionBase` subclasses call new `IAndroidDevice` methods. Command-string construction (swipe format, `input text` single-quote escaping, repeated `keyevent`) lives in a testable static `AdbInputCommand` builder so escaping is unit-tested without a device. A new `AndroidKeyCodes` static table is the single source of truth for both the key dropdown's options and name→Android-keycode resolution (Win32 `VirtualKeys` codes do not match `adb` keyevent codes).

**Tech Stack:** C# / .NET 10, AdbCore engine, AdvancedSharpAdbClient, xUnit. Tests use the existing hand-rolled `FakeAndroidDevice`.

---

## File Structure

**New files**
- `AdbCore/Android/AndroidKeyCodes.cs` — ordered name→keycode table; `Names` + `TryResolve`.
- `AdbCore/Android/AdbInputCommand.cs` — builds `input …` shell command strings (testable).
- `AdbCore/Actions/BuiltIn/Android/LongPressAction.cs`
- `AdbCore/Actions/BuiltIn/Android/SendTextAction.cs`
- `AdbCore/Actions/BuiltIn/Android/PressKeyAction.cs`
- `AdbCore.Tests/Android/AndroidKeyCodesTests.cs`
- `AdbCore.Tests/Android/AdbInputCommandTests.cs`

**Modified files**
- `AdbCore/Android/IAndroidDevice.cs` — add `LongPress`, `SendText`, `KeyEvent`.
- `AdbCore/Android/AdvancedSharpAdbDevice.cs` — implement the three via `AdbInputCommand` + `Shell`.
- `AdbCore.Tests/Actions/BuiltIn/Android/FakeAndroidDevice.cs` — record the three new calls.
- `AdbCore.Tests/Actions/BuiltIn/Android/AndroidInputActionTests.cs` — tests for the three actions.
- `AdbCore/Actions/BuiltIn/BuiltInActions.cs` — register the three actions.
- `BotBuilder.Core/Picker/CoordinateFieldMap.cs` — map `android.longPress` → x/y point.
- `BotBuilder.Core.Tests/Picker/CoordinateFieldMapTests.cs` — Long Press picks x/y.
- `BotBuilder.Core.Tests/PaletteViewModelTests.cs` — Android 13→16, total 48→51.
- `ADB.wiki/Actions-Reference.md` — 3 Android rows, Press Key key list, VirtualKeys names.
- `README.md` — fold "long-press" into the arsenal input mention.
- `Docs/Specs/2026-07-03-android-longpress-sendtext-presskey-design.md` — align "empty text" wording.

---

## Task 1: AndroidKeyCodes (name→keycode table)

**Files:**
- Create: `AdbCore/Android/AndroidKeyCodes.cs`
- Test: `AdbCore.Tests/Android/AndroidKeyCodesTests.cs`

- [ ] **Step 1: Write the failing test**

Create `AdbCore.Tests/Android/AndroidKeyCodesTests.cs`:

```csharp
using AdbCore.Android;
using Xunit;

namespace AdbCore.Tests.Android;

public class AndroidKeyCodesTests
{
    [Theory]
    [InlineData("Backspace", 67)]
    [InlineData("Delete (Fwd)", 112)]
    [InlineData("Enter", 66)]
    [InlineData("Tab", 61)]
    [InlineData("Space", 62)]
    [InlineData("Up", 19)]
    [InlineData("Down", 20)]
    [InlineData("Left", 21)]
    [InlineData("Right", 22)]
    [InlineData("Home", 122)]
    [InlineData("End", 123)]
    [InlineData("Escape", 111)]
    public void TryResolve_KnownName_ReturnsCode(string name, int expected)
    {
        Assert.True(AndroidKeyCodes.TryResolve(name, out var code));
        Assert.Equal(expected, code);
    }

    [Fact]
    public void TryResolve_IsCaseInsensitive()
    {
        Assert.True(AndroidKeyCodes.TryResolve("backSPACE", out var code));
        Assert.Equal(67, code);
    }

    [Fact]
    public void TryResolve_UnknownOrEmpty_ReturnsFalse()
    {
        Assert.False(AndroidKeyCodes.TryResolve("Meta", out _));
        Assert.False(AndroidKeyCodes.TryResolve("", out _));
        Assert.False(AndroidKeyCodes.TryResolve("   ", out _));
    }

    [Fact]
    public void Names_AreOrdered_AndEveryNameResolves()
    {
        Assert.Equal("Backspace", AndroidKeyCodes.Names[0]);
        Assert.Equal(12, AndroidKeyCodes.Names.Count);
        Assert.All(AndroidKeyCodes.Names, n => Assert.True(AndroidKeyCodes.TryResolve(n, out _)));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~AndroidKeyCodesTests"`
Expected: FAIL to compile — `AndroidKeyCodes` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `AdbCore/Android/AndroidKeyCodes.cs`:

```csharp
namespace AdbCore.Android;

/// <summary>Resolves friendly key names (e.g. "Backspace", "Enter", "Up") to Android <c>KEYCODE_*</c>
/// values for <c>input keyevent</c>. Distinct from <see cref="AdbCore.Input.VirtualKeys"/>, whose codes
/// are Win32 virtual-key codes and do not match Android. Single source of truth for the Press Key
/// action's dropdown options and its name→code resolution.</summary>
public static class AndroidKeyCodes
{
    // Ordered so the dropdown lists the most-used keys first (Backspace clears a field).
    private static readonly (string Name, int Code)[] Entries =
    [
        ("Backspace", 67),      // KEYCODE_DEL
        ("Delete (Fwd)", 112),  // KEYCODE_FORWARD_DEL
        ("Enter", 66),          // KEYCODE_ENTER
        ("Tab", 61),            // KEYCODE_TAB
        ("Space", 62),          // KEYCODE_SPACE
        ("Up", 19),             // KEYCODE_DPAD_UP
        ("Down", 20),           // KEYCODE_DPAD_DOWN
        ("Left", 21),           // KEYCODE_DPAD_LEFT
        ("Right", 22),          // KEYCODE_DPAD_RIGHT
        ("Home", 122),          // KEYCODE_MOVE_HOME
        ("End", 123),           // KEYCODE_MOVE_END
        ("Escape", 111),        // KEYCODE_ESCAPE
    ];

    private static readonly Dictionary<string, int> ByName =
        Entries.ToDictionary(e => e.Name, e => e.Code, StringComparer.OrdinalIgnoreCase);

    /// <summary>Display names in dropdown order; used verbatim as the Press Key <c>key</c> field options.</summary>
    public static IReadOnlyList<string> Names { get; } = Entries.Select(e => e.Name).ToArray();

    /// <summary>Resolves a key name to its Android keycode. Case-insensitive; false for unknown/blank.</summary>
    public static bool TryResolve(string name, out int keyCode)
    {
        keyCode = 0;
        return !string.IsNullOrWhiteSpace(name) && ByName.TryGetValue(name.Trim(), out keyCode);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~AndroidKeyCodesTests"`
Expected: PASS (16 cases).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Android/AndroidKeyCodes.cs AdbCore.Tests/Android/AndroidKeyCodesTests.cs
git commit -m "Add AndroidKeyCodes name->keycode resolver for Android key events"
```

---

## Task 2: AdbInputCommand (shell command builder)

**Files:**
- Create: `AdbCore/Android/AdbInputCommand.cs`
- Test: `AdbCore.Tests/Android/AdbInputCommandTests.cs`

- [ ] **Step 1: Write the failing test**

Create `AdbCore.Tests/Android/AdbInputCommandTests.cs`:

```csharp
using AdbCore.Android;
using Xunit;

namespace AdbCore.Tests.Android;

public class AdbInputCommandTests
{
    [Fact]
    public void LongPress_IsSamePointSwipeWithDuration()
        => Assert.Equal("input swipe 5 10 5 10 600", AdbInputCommand.LongPress(5, 10, 600));

    [Fact]
    public void Text_SingleQuoteWrapsPlainText()
        => Assert.Equal("input text 'hello world'", AdbInputCommand.Text("hello world"));

    [Fact]
    public void Text_EscapesEmbeddedSingleQuote()
        => Assert.Equal(@"input text 'it'\''s me'", AdbInputCommand.Text("it's me"));

    [Fact]
    public void Text_EmptyStillProducesQuotedEmptyArg()
        => Assert.Equal("input text ''", AdbInputCommand.Text(""));

    [Fact]
    public void KeyEvent_RepeatsCodeCountTimes()
        => Assert.Equal("input keyevent 67 67 67", AdbInputCommand.KeyEvent(67, 3));

    [Fact]
    public void KeyEvent_CountOne_IsSingleCode()
        => Assert.Equal("input keyevent 66", AdbInputCommand.KeyEvent(66, 1));

    [Fact]
    public void KeyEvent_CountBelowOne_ClampsToOne()
        => Assert.Equal("input keyevent 67", AdbInputCommand.KeyEvent(67, 0));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~AdbInputCommandTests"`
Expected: FAIL to compile — `AdbInputCommand` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `AdbCore/Android/AdbInputCommand.cs`:

```csharp
using System.Globalization;

namespace AdbCore.Android;

/// <summary>Builds <c>input …</c> device-shell command strings for gesture/text/key actions. Kept
/// separate from the device so the string construction — especially <c>input text</c> quoting — is
/// unit-testable without a live ADB connection.</summary>
public static class AdbInputCommand
{
    /// <summary>A press-and-hold: a swipe whose start and end are the same point, held for
    /// <paramref name="durationMs"/> — the standard adb long-press idiom.</summary>
    public static string LongPress(int x, int y, int durationMs)
        => $"input swipe {x} {y} {x} {y} {durationMs}";

    /// <summary>Types literal text. The argument is single-quote-wrapped (embedded quotes escaped as
    /// <c>'\''</c>) so spaces and shell metacharacters are passed literally to <c>input text</c>.</summary>
    public static string Text(string text)
        => $"input text {SingleQuote(text ?? string.Empty)}";

    /// <summary>Sends <c>input keyevent</c> with the keycode repeated <paramref name="count"/> times in a
    /// single invocation (min 1), so "Backspace × N" is one shell round-trip.</summary>
    public static string KeyEvent(int keyCode, int count)
    {
        var n = Math.Max(1, count);
        var code = keyCode.ToString(CultureInfo.InvariantCulture);
        return "input keyevent " + string.Join(' ', Enumerable.Repeat(code, n));
    }

    private static string SingleQuote(string text)
        => "'" + text.Replace("'", "'\\''") + "'";
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~AdbInputCommandTests"`
Expected: PASS (7 cases).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Android/AdbInputCommand.cs AdbCore.Tests/Android/AdbInputCommandTests.cs
git commit -m "Add AdbInputCommand builder for long-press/text/keyevent shell strings"
```

---

## Task 3: Extend IAndroidDevice + implement on real & fake devices

**Files:**
- Modify: `AdbCore/Android/IAndroidDevice.cs`
- Modify: `AdbCore/Android/AdvancedSharpAdbDevice.cs`
- Modify: `AdbCore.Tests/Actions/BuiltIn/Android/FakeAndroidDevice.cs`

No new test here — the real device needs a live ADB server (the user verifies it on hardware). Correctness of the emitted strings is covered by Task 2; this task's verification is that the solution builds and existing Android tests stay green with the fake updated.

- [ ] **Step 1: Add the interface members**

In `AdbCore/Android/IAndroidDevice.cs`, add after `void Swipe(...)`:

```csharp
    /// <summary>Presses and holds at (x, y) for the given duration (same-point swipe).</summary>
    void LongPress(int x, int y, int durationMs);

    /// <summary>Types literal text into the focused field (via <c>input text</c>).</summary>
    void SendText(string text);

    /// <summary>Sends an Android keycode <paramref name="count"/> times (via <c>input keyevent</c>).</summary>
    void KeyEvent(int keyCode, int count);
```

- [ ] **Step 2: Implement on the real device**

In `AdbCore/Android/AdvancedSharpAdbDevice.cs`, add after the existing `Swipe` implementation (line ~52):

```csharp
    public void LongPress(int x, int y, int durationMs) => Shell(AdbInputCommand.LongPress(x, y, durationMs));

    public void SendText(string text) => Shell(AdbInputCommand.Text(text));

    public void KeyEvent(int keyCode, int count) => Shell(AdbInputCommand.KeyEvent(keyCode, count));
```

- [ ] **Step 3: Implement on the fake device**

In `AdbCore.Tests/Actions/BuiltIn/Android/FakeAndroidDevice.cs`, add after the `PressBack` line. Record the **raw** arguments (escaping is Task 2's concern, exercised through `AdbInputCommand`):

```csharp
    public void LongPress(int x, int y, int durationMs) => Calls.Add($"longpress {x} {y} {durationMs}");
    public void SendText(string text) => Calls.Add($"text {text}");
    public void KeyEvent(int keyCode, int count) => Calls.Add($"keyevent {keyCode} {count}");
```

- [ ] **Step 4: Build and run existing Android tests**

Run: `dotnet build ADB.slnx -v q -clp:NoSummary`
Expected: Build succeeded, 0 errors. (Confirms every `IAndroidDevice` implementer compiles with the new members.)

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~AndroidInputActionTests"`
Expected: PASS (existing Tap/Swipe/PressBack tests unaffected).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Android/IAndroidDevice.cs AdbCore/Android/AdvancedSharpAdbDevice.cs AdbCore.Tests/Actions/BuiltIn/Android/FakeAndroidDevice.cs
git commit -m "Add LongPress/SendText/KeyEvent to IAndroidDevice and implementations"
```

---

## Task 4: LongPressAction

**Files:**
- Create: `AdbCore/Actions/BuiltIn/Android/LongPressAction.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/Android/AndroidInputActionTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `AndroidInputActionTests` (uses the existing `WithDevice` helper):

```csharp
    [Fact]
    public async Task LongPress_CallsDeviceWithCoordsAndDuration()
    {
        var action = new BotAction { Config = { ["x"] = 50, ["y"] = 60, ["durationMs"] = 900 } };
        var (ctx, dev) = WithDevice(action);

        var r = await new LongPressAction().ExecuteAsync(ctx, default);

        Assert.True(r.Success);
        Assert.Equal("onSuccess", r.OutputPort);
        Assert.Equal("longpress 50 60 900", dev.Calls.Single());
    }

    [Fact]
    public async Task LongPress_DefaultsDurationTo600()
    {
        var action = new BotAction { Config = { ["x"] = 1, ["y"] = 2 } };
        var (ctx, dev) = WithDevice(action);

        await new LongPressAction().ExecuteAsync(ctx, default);

        Assert.Equal("longpress 1 2 600", dev.Calls.Single());
    }

    [Fact]
    public async Task LongPress_NoDeviceBound_Fails()
    {
        var ctx = new BotExecutionContext();
        var exec = new ActionExecutionContext(new BotAction { Config = { ["x"] = 1, ["y"] = 1 } }, ctx, _ => { });

        var r = await new LongPressAction().ExecuteAsync(exec, default);

        Assert.False(r.Success);
        Assert.Contains("Android", r.ErrorMessage);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~AndroidInputActionTests.LongPress"`
Expected: FAIL to compile — `LongPressAction` does not exist.

- [ ] **Step 3: Write the implementation**

Create `AdbCore/Actions/BuiltIn/Android/LongPressAction.cs`:

```csharp
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Presses and holds the Android screen at (x, y) for a duration.</summary>
public sealed class LongPressAction : AndroidActionBase
{
    public override string TypeKey => "android.longPress";
    public override string DisplayName => "Long Press";
    public override string Description => "Presses and holds the device screen at the given coordinates.";

    public override List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = "x", Label = "X", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField { Key = "y", Label = "Y", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField { Key = "durationMs", Label = "Duration (ms)", Type = ConfigFieldType.Number, DefaultValue = 600 },
    };

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return Task.FromResult(RequiresDevice());
        }

        var c = context.Action.Config;
        device.LongPress(
            ConfigValues.GetInt(c, "x"),
            ConfigValues.GetInt(c, "y"),
            ConfigValues.GetInt(c, "durationMs", 600));
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~AndroidInputActionTests.LongPress"`
Expected: PASS (3 cases).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Actions/BuiltIn/Android/LongPressAction.cs AdbCore.Tests/Actions/BuiltIn/Android/AndroidInputActionTests.cs
git commit -m "Add Long Press Android action (android.longPress)"
```

---

## Task 5: SendTextAction

**Files:**
- Create: `AdbCore/Actions/BuiltIn/Android/SendTextAction.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/Android/AndroidInputActionTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `AndroidInputActionTests`:

```csharp
    [Fact]
    public async Task SendText_CallsDeviceWithRawText()
    {
        var action = new BotAction { Config = { ["text"] = "hello world" } };
        var (ctx, dev) = WithDevice(action);

        var r = await new SendTextAction().ExecuteAsync(ctx, default);

        Assert.True(r.Success);
        Assert.Equal("text hello world", dev.Calls.Single());
    }

    [Fact]
    public async Task SendText_EmptyText_IsNoOpSuccess()
    {
        var action = new BotAction { Config = { ["text"] = "" } };
        var (ctx, dev) = WithDevice(action);

        var r = await new SendTextAction().ExecuteAsync(ctx, default);

        Assert.True(r.Success);
        Assert.Empty(dev.Calls);
    }

    [Fact]
    public async Task SendText_SingleSpace_IsStillSent()
    {
        var action = new BotAction { Config = { ["text"] = " " } };
        var (ctx, dev) = WithDevice(action);

        await new SendTextAction().ExecuteAsync(ctx, default);

        Assert.Equal("text  ", dev.Calls.Single()); // "text " + the space argument
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~AndroidInputActionTests.SendText"`
Expected: FAIL to compile — `SendTextAction` does not exist.

- [ ] **Step 3: Write the implementation**

Create `AdbCore/Actions/BuiltIn/Android/SendTextAction.cs`. Empty text is an intentional no-op (mirrors the Windows Type Text action); a deliberate single space is still typed, so the guard is `IsNullOrEmpty`, not `IsNullOrWhiteSpace`:

```csharp
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Types literal text into the focused field on the device. Empty text is a no-op.</summary>
public sealed class SendTextAction : AndroidActionBase
{
    public override string TypeKey => "android.sendText";
    public override string DisplayName => "Send Text";
    public override string Description => "Types text into the focused field on the device.";

    public override List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = "text", Label = "Text", Type = ConfigFieldType.MultilineString },
    };

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return Task.FromResult(RequiresDevice());
        }

        var text = ConfigValues.GetString(context.Action.Config, "text");
        if (!string.IsNullOrEmpty(text))
        {
            device.SendText(text);
        }
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~AndroidInputActionTests.SendText"`
Expected: PASS (3 cases).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Actions/BuiltIn/Android/SendTextAction.cs AdbCore.Tests/Actions/BuiltIn/Android/AndroidInputActionTests.cs
git commit -m "Add Send Text Android action (android.sendText)"
```

---

## Task 6: PressKeyAction

**Files:**
- Create: `AdbCore/Actions/BuiltIn/Android/PressKeyAction.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/Android/AndroidInputActionTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `AndroidInputActionTests`:

```csharp
    [Fact]
    public async Task PressKey_ResolvesNameAndRepeatsByCount()
    {
        var action = new BotAction { Config = { ["key"] = "Backspace", ["count"] = 3 } };
        var (ctx, dev) = WithDevice(action);

        var r = await new PressKeyAction().ExecuteAsync(ctx, default);

        Assert.True(r.Success);
        Assert.Equal("keyevent 67 3", dev.Calls.Single());
    }

    [Fact]
    public async Task PressKey_DefaultsToBackspaceCountOne()
    {
        var action = new BotAction(); // no config
        var (ctx, dev) = WithDevice(action);

        await new PressKeyAction().ExecuteAsync(ctx, default);

        Assert.Equal("keyevent 67 1", dev.Calls.Single());
    }

    [Fact]
    public async Task PressKey_CountBelowOne_ClampsToOne()
    {
        var action = new BotAction { Config = { ["key"] = "Enter", ["count"] = 0 } };
        var (ctx, dev) = WithDevice(action);

        await new PressKeyAction().ExecuteAsync(ctx, default);

        Assert.Equal("keyevent 66 1", dev.Calls.Single());
    }

    [Fact]
    public async Task PressKey_UnknownKey_Fails()
    {
        var action = new BotAction { Config = { ["key"] = "Meta", ["count"] = 1 } };
        var (ctx, dev) = WithDevice(action);

        var r = await new PressKeyAction().ExecuteAsync(ctx, default);

        Assert.False(r.Success);
        Assert.Contains("Meta", r.ErrorMessage);
        Assert.Empty(dev.Calls);
    }

    [Fact]
    public void PressKey_KeyField_OptionsComeFromAndroidKeyCodes()
    {
        var key = new PressKeyAction().ConfigFields.Single(f => f.Key == "key");
        Assert.Equal(ConfigFieldType.Enum, key.Type);
        Assert.Equal(AdbCore.Android.AndroidKeyCodes.Names, key.Options);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~AndroidInputActionTests.PressKey"`
Expected: FAIL to compile — `PressKeyAction` does not exist.

- [ ] **Step 3: Write the implementation**

Create `AdbCore/Actions/BuiltIn/Android/PressKeyAction.cs`. The `key` options come from `AndroidKeyCodes.Names` (single source of truth); `count` defaults to 1 and is clamped to ≥1:

```csharp
using AdbCore.Android;
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Sends a named Android key (Backspace, Enter, arrows, …) one or more times — e.g. Backspace
/// with a high count clears the focused field.</summary>
public sealed class PressKeyAction : AndroidActionBase
{
    public override string TypeKey => "android.pressKey";
    public override string DisplayName => "Press Key";
    public override string Description => "Sends a named key to the device, optionally repeated.";

    public override List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField
        {
            Key = "key",
            Label = "Key",
            Type = ConfigFieldType.Enum,
            DefaultValue = "Backspace",
            Options = AndroidKeyCodes.Names.ToList(),
        },
        new ConfigField { Key = "count", Label = "Count", Type = ConfigFieldType.Number, DefaultValue = 1 },
    };

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return Task.FromResult(RequiresDevice());
        }

        var c = context.Action.Config;
        var keyName = ConfigValues.GetString(c, "key", "Backspace");
        if (!AndroidKeyCodes.TryResolve(keyName, out var keyCode))
        {
            return Task.FromResult(ActionResult.Fail($"Press Key: unrecognized key '{keyName}'."));
        }

        var count = Math.Max(1, ConfigValues.GetInt(c, "count", 1));
        device.KeyEvent(keyCode, count);
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test AdbCore.Tests --filter "FullyQualifiedName~AndroidInputActionTests.PressKey"`
Expected: PASS (5 cases).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Actions/BuiltIn/Android/PressKeyAction.cs AdbCore.Tests/Actions/BuiltIn/Android/AndroidInputActionTests.cs
git commit -m "Add Press Key Android action (android.pressKey) with named-key dropdown"
```

---

## Task 7: Register actions, wire coordinate picker, fix palette counts

**Files:**
- Modify: `AdbCore/Actions/BuiltIn/BuiltInActions.cs`
- Modify: `BotBuilder.Core/Picker/CoordinateFieldMap.cs`
- Modify: `BotBuilder.Core.Tests/Picker/CoordinateFieldMapTests.cs`
- Modify: `BotBuilder.Core.Tests/PaletteViewModelTests.cs`
- Create test: `AdbCore.Tests/Actions/BuiltIn/Android/AndroidInputRegistrationTests.cs`

- [ ] **Step 1: Write the failing registration + picker tests**

Create `AdbCore.Tests/Actions/BuiltIn/Android/AndroidInputRegistrationTests.cs`:

```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn.Android;

public class AndroidInputRegistrationTests
{
    [Theory]
    [InlineData("android.longPress")]
    [InlineData("android.sendText")]
    [InlineData("android.pressKey")]
    public void AndroidInputAction_IsRegistered_AsDefinitionAndExecutor(string typeKey)
    {
        var defs = new ActionRegistry();
        var execs = new ActionExecutorRegistry();
        BuiltInActions.Register(defs, execs);

        Assert.True(defs.TryGet(typeKey, out _));
        Assert.True(execs.TryGet(typeKey, out var exec) && exec is not null);
    }
}
```

Add to `CoordinateFieldMapTests` the new single-point case by extending the existing `[Theory]` at line 10 — add one line:

```csharp
    [InlineData("android.longPress")]
```

(placed alongside `[InlineData("android.tap")]` etc. — Long Press has one x/y point.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~AndroidInputRegistrationTests|FullyQualifiedName~CoordinateFieldMapTests"`
Expected: FAIL — the three type keys are not registered; `android.longPress` not in the map.

- [ ] **Step 3: Register the actions**

In `AdbCore/Actions/BuiltIn/BuiltInActions.cs`, replace the Android input block (lines 57-59) so Long Press follows Swipe and the two text/key actions follow Press Back:

```csharp
        Add(new TapAction(), definitions, executors);
        Add(new SwipeAction(), definitions, executors);
        Add(new LongPressAction(), definitions, executors);
        Add(new PressBackAction(), definitions, executors);
        Add(new SendTextAction(), definitions, executors);
        Add(new PressKeyAction(), definitions, executors);
```

- [ ] **Step 4: Wire the coordinate picker**

In `BotBuilder.Core/Picker/CoordinateFieldMap.cs`, add to the `Map` initializer after the `android.tap` line:

```csharp
            ["android.longPress"] = [new CoordinatePoint("x", "y", "Target")],
```

- [ ] **Step 5: Fix the palette-count assertions**

In `BotBuilder.Core.Tests/PaletteViewModelTests.cs`:

Line 62 — change Android count 13 → 16 and extend the comment:

```csharp
        Assert.Equal(16, android.Items.Count); // Tap, Swipe, Long Press, Press Back, Send Text, Press Key, Launch App, Install APK, Screenshot, Find Image, Wait for Image, Assert Image Absent, Read Text, Find Text, Wait for Text, Assert Text Absent
```

Line 99 — change total 48 → 51 and update the Android term in the comment:

```csharp
        Assert.Equal(51, palette.Categories.SelectMany(c => c.Items).Count()); // 10 Control Flow + 4 Data + 1 Scripting + 6 Input + 8 Screen + 16 Android + 5 Browser + 1 Window
```

- [ ] **Step 6: Run the full suites for the touched projects**

Run: `dotnet test AdbCore.Tests`
Expected: PASS (registration + all Android action tests green).

Run: `dotnet test BotBuilder.Core.Tests`
Expected: PASS (CoordinateFieldMap + palette counts green).

- [ ] **Step 7: Commit**

```bash
git add AdbCore/Actions/BuiltIn/BuiltInActions.cs BotBuilder.Core/Picker/CoordinateFieldMap.cs BotBuilder.Core.Tests/Picker/CoordinateFieldMapTests.cs BotBuilder.Core.Tests/PaletteViewModelTests.cs AdbCore.Tests/Actions/BuiltIn/Android/AndroidInputRegistrationTests.cs
git commit -m "Register Android long-press/send-text/press-key; wire coord picker; fix palette counts"
```

---

## Task 8: Documentation (wiki, README, spec alignment)

**Files:**
- Modify: `ADB.wiki/Actions-Reference.md`
- Modify: `README.md`
- Modify: `Docs/Specs/2026-07-03-android-longpress-sendtext-presskey-design.md`

No automated test — these are doc surfaces. Verify by re-reading each edit against the shipped code.

- [ ] **Step 1: Add the three Android rows to the wiki**

In `ADB.wiki/Actions-Reference.md`, in the **Android** table (after the `Press Back` row, line ~113), insert:

```markdown
| **Long Press** | `android.longPress` | `onSuccess`/`onFailure` | `x` (num, `0`), `y` (num, `0`), `durationMs` (num, `600`) | Press-and-hold (same-point swipe). |
| **Send Text** | `android.sendText` | `onSuccess`/`onFailure` | `text` (multiline) | Types into the focused field; empty text is a no-op. |
| **Press Key** | `android.pressKey` | `onSuccess`/`onFailure` | `key` (enum, `Backspace`), `count` (num, `1`) | Sends a named key `count` times (Backspace ×N clears a field). |
```

Immediately after the Android table, add a key-name note:

```markdown
**Press Key names** — `Backspace`, `Delete (Fwd)`, `Enter`, `Tab`, `Space`, `Up`, `Down`, `Left`,
`Right`, `Home`, `End`, `Escape`. These map to Android `KEYCODE_*` values (distinct from the Windows
Key Press names below).
```

- [ ] **Step 2: Fill the VirtualKeys gap in the wiki**

In `ADB.wiki/Actions-Reference.md`, in the **Input** section right after the Input actions table (after line ~71), add:

```markdown
**Key Press names** (`VirtualKeys`) — single letters `A`–`Z`, digits `0`–`9`, function keys `F1`–`F12`,
and named keys: `Enter`/`Return`, `Esc`/`Escape`, `Tab`, `Space`, `Backspace`, `Delete`/`Del`, `Insert`,
`Home`, `End`, `PageUp`, `PageDown`, `Up`, `Down`, `Left`, `Right`. Names are case-insensitive; an
unrecognized name fails the action.
```

- [ ] **Step 3: Light-touch the README arsenal**

In `README.md`, find the input line (line ~42):

```markdown
- **Input & windows** — mouse/keyboard actions, activate window. The clicky-clicky.
```

Update it to mention Android text/gestures without overclaiming (keep the goblin voice):

```markdown
- **Input & windows** — mouse/keyboard actions, activate window, and on Android: tap, long-press,
  swipe, send text, and hammer a key (Backspace ×50 to nuke a field). The clicky-clicky.
```

- [ ] **Step 4: Align the spec wording**

In `Docs/Specs/2026-07-03-android-longpress-sendtext-presskey-design.md`, change the Send Text no-op sentence from "Empty or whitespace-only text is a no-op…" to:

```markdown
**Empty text is a no-op that returns success** (a deliberate single space is still typed), mirroring
the Windows *Type Text* action.
```

- [ ] **Step 5: Verify docs against code**

Re-read each edited row/line against the implemented `TypeKey`s, config keys, and defaults from Tasks 4-7. Confirm every default (`600`, `Backspace`, `1`) and key name matches the code.

- [ ] **Step 6: Commit (main repo)**

```bash
git add README.md Docs/Specs/2026-07-03-android-longpress-sendtext-presskey-design.md
git commit -m "Docs: document Long Press/Send Text/Press Key and VirtualKeys names"
```

- [ ] **Step 7: Commit + push the wiki submodule, then bump the pointer**

The wiki is a separate git repo mounted at `ADB.wiki/` (default branch `master`).

```bash
cd ADB.wiki
git add Actions-Reference.md
git commit -m "Document Android Long Press/Send Text/Press Key and list VirtualKeys names"
git push origin master
cd ..
git add ADB.wiki
git commit -m "Bump ADB.wiki pointer for Android input action docs"
```

(If the `ADB.wiki` submodule is not initialized in this worktree, note it and defer the wiki push — the main-repo doc edits still stand.)

---

## Final verification

- [ ] **Full build + test sweep**

Run: `dotnet build ADB.slnx -v q -clp:NoSummary`
Expected: Build succeeded, 0 warnings, 0 errors.

Run: `dotnet test ADB.slnx`
Expected: All tests pass (new: AndroidKeyCodes 16, AdbInputCommand 7, LongPress 3, SendText 3, PressKey 5, registration 3, plus updated palette/coord tests).
