# Configurable Key Delay (Input Timing Fix) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop synthetic keyboard input from being injected at machine speed (which floods the target's input queue and corrupts output with dropped/repeated keys) by introducing a configurable, cancellable inter-key delay on Type Text and Key Press.

**Architecture:** The keyboard methods on `IInputSender` become `async` and take a `keyDelayMs` plus a `CancellationToken`. The Win32 SendInput sender stops batching all key events into one `SendInput` call — it sends each key-down/up event individually and `await`s a pace delay after each. A new `KeyboardActionBase` (sibling of `PointerActionBase`) adds a per-node **"Key Delay (ms)"** config field (default **20**) and threads it to the sender. The whole Input action pipeline (`InputActionBase.Perform`) is converted to `PerformAsync(..., CancellationToken)` so a long type can be stopped mid-string.

**Tech Stack:** C# / .NET 10, xUnit. Projects: `AdbCore` (actions + Win32 input), `AdbCore.Tests`.

---

## File Structure

- `AdbCore/Actions/BuiltIn/InputActionBase.cs` — change `Perform` → `async PerformAsync(..., CancellationToken)`; `ExecuteAsync` awaits it.
- `AdbCore/Actions/BuiltIn/PointerActionBase.cs` — implement `PerformAsync` wrapping the synchronous mouse dispatch in `Task.FromResult`.
- `AdbCore/Actions/BuiltIn/KeyboardActionBase.cs` — **new**: Key Delay field + `KeyDelayMs(context)` reader.
- `AdbCore/Actions/BuiltIn/TypeTextAction.cs` / `KeyPressAction.cs` — derive from `KeyboardActionBase`; `PerformAsync` awaits the async sender call.
- `AdbCore/Input/IInputSender.cs` — keyboard methods become `Task` + `keyDelayMs` + `CancellationToken`; mouse methods stay synchronous `void`.
- `AdbCore/Input/Win32SendInputSender.cs` — de-batch keyboard sends; pace each event.
- `AdbCore/Input/Win32PostMessageSender.cs` — async keyboard sends; pace each message.
- `AdbCore.Tests/Input/RecordingInputSender.cs` — async keyboard methods; record `LastKeyDelayMs`.
- `AdbCore.Tests/Actions/BuiltIn/KeyboardActionsTests.cs` — delay assertions, config-field order, cancellation.

---

## Task 1: Async-ify the Input action pipeline (no behavior change)

Convert the Input action base from a synchronous `Perform` to an async `PerformAsync` that receives the `CancellationToken`. Sender interface is unchanged in this task, so mouse/keyboard calls remain synchronous and behavior is identical — this is a pure plumbing refactor that keeps the build green and all existing tests passing.

**Files:**
- Modify: `AdbCore/Actions/BuiltIn/InputActionBase.cs`
- Modify: `AdbCore/Actions/BuiltIn/PointerActionBase.cs`
- Modify: `AdbCore/Actions/BuiltIn/TypeTextAction.cs`
- Modify: `AdbCore/Actions/BuiltIn/KeyPressAction.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/KeyboardActionsTests.cs` (existing tests must still pass unchanged)

- [ ] **Step 1: Change `InputActionBase` to async `PerformAsync`**

In `InputActionBase.cs`, replace the abstract `Perform` and the `ExecuteAsync` body:

```csharp
    /// <summary>Runs the action's operation against the resolved window and chosen sender; returns the result.</summary>
    protected abstract Task<ActionResult> PerformAsync(IInputSender sender, IntPtr windowHandle, ActionExecutionContext context, CancellationToken ct);

    public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        if (ResolveWindow(context) is not IntPtr hwnd || hwnd == IntPtr.Zero)
        {
            return ActionResult.Fail($"{DisplayName} requires a resolved Window target (HWND).");
        }

        var method = ConfigValues.GetString(context.Action.Config, MethodKey, InputSenderResolver.SendInputMethod);
        return await PerformAsync(_senders.Resolve(method), hwnd, context, ct);
    }
```

- [ ] **Step 2: Update `PointerActionBase` to implement `PerformAsync`**

In `PointerActionBase.cs`, replace the `Perform` override:

```csharp
    protected override Task<ActionResult> PerformAsync(IInputSender sender, IntPtr windowHandle, ActionExecutionContext context, CancellationToken ct)
    {
        var x = ConfigValues.GetInt(context.Action.Config, XKey);
        var y = ConfigValues.GetInt(context.Action.Config, YKey);
        Dispatch(sender, windowHandle, x, y);
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
```

- [ ] **Step 3: Update `TypeTextAction` and `KeyPressAction` to `PerformAsync` (still synchronous sender calls)**

`TypeTextAction.cs`:

```csharp
    protected override Task<ActionResult> PerformAsync(IInputSender sender, IntPtr windowHandle, ActionExecutionContext context, CancellationToken ct)
    {
        // Empty text is an intentional no-op (consistent with Log / Set Variable); the sender skips it.
        var text = ConfigValues.GetString(context.Action.Config, TextKey);
        sender.TypeText(windowHandle, text);
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
```

`KeyPressAction.cs` — keep the existing validation/modifier-building body, change the signature and the final two lines:

```csharp
    protected override Task<ActionResult> PerformAsync(IInputSender sender, IntPtr windowHandle, ActionExecutionContext context, CancellationToken ct)
    {
        var keyName = ConfigValues.GetString(context.Action.Config, KeyKey);
        if (string.IsNullOrWhiteSpace(keyName))
        {
            return Task.FromResult(ActionResult.Fail("Key Press: a key is required."));
        }

        if (!VirtualKeys.TryResolve(keyName, out var vk))
        {
            return Task.FromResult(ActionResult.Fail($"Key Press: unrecognized key '{keyName}'."));
        }

        var modifiers = KeyModifiers.None;
        if (ConfigValues.GetBool(context.Action.Config, CtrlKey)) modifiers |= KeyModifiers.Control;
        if (ConfigValues.GetBool(context.Action.Config, AltKey)) modifiers |= KeyModifiers.Alt;
        if (ConfigValues.GetBool(context.Action.Config, ShiftKey)) modifiers |= KeyModifiers.Shift;
        if (ConfigValues.GetBool(context.Action.Config, WinKey)) modifiers |= KeyModifiers.Win;

        sender.KeyPress(windowHandle, vk, modifiers);
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
```

- [ ] **Step 4: Build and run the existing input tests — expect all green**

Run: `dotnet test AdbCore.Tests/AdbCore.Tests.csproj --filter "FullyQualifiedName~Actions.BuiltIn"`
Expected: PASS (no behavior change; this is a pure async refactor).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Actions/BuiltIn/InputActionBase.cs AdbCore/Actions/BuiltIn/PointerActionBase.cs AdbCore/Actions/BuiltIn/TypeTextAction.cs AdbCore/Actions/BuiltIn/KeyPressAction.cs
git commit -m "refactor(actions): async PerformAsync for Input actions (no behavior change)"
```

---

## Task 2: Paced, de-batched keyboard sends + configurable Key Delay

Make the keyboard `IInputSender` methods async with a `keyDelayMs` + `CancellationToken`, stop batching key events (send each individually and pace between them), and add a per-node **"Key Delay (ms)"** field (default 20) via a new `KeyboardActionBase`. This is the actual fix for the corruption.

**Files:**
- Modify: `AdbCore/Input/IInputSender.cs`
- Modify: `AdbCore/Input/Win32SendInputSender.cs`
- Modify: `AdbCore/Input/Win32PostMessageSender.cs`
- Modify: `AdbCore.Tests/Input/RecordingInputSender.cs`
- Create: `AdbCore/Actions/BuiltIn/KeyboardActionBase.cs`
- Modify: `AdbCore/Actions/BuiltIn/TypeTextAction.cs`
- Modify: `AdbCore/Actions/BuiltIn/KeyPressAction.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/KeyboardActionsTests.cs`

- [ ] **Step 1: Write the failing test for default + overridden Key Delay**

Add to `KeyboardActionsTests.cs`. (After Task 2's interface change, `RecordingInputSender` will expose `LastKeyDelayMs`.) Also update the existing `TypeText_SendsTextToTarget_ViaSendInput_Default` test to assert the default delay:

```csharp
    [Fact]
    public async Task TypeText_DefaultKeyDelay_Is20()
    {
        var id = Guid.NewGuid();
        var senders = new Senders();
        var action = new BotAction { TargetId = id };
        action.Config[TypeTextAction.TextKey] = "hi";

        await new TypeTextAction(senders.Resolver()).ExecuteAsync(Exec(action, WindowContext(id, (IntPtr)5)), default);

        Assert.Equal(KeyboardActionBase.DefaultKeyDelayMs, senders.SendInput.LastKeyDelayMs);
    }

    [Fact]
    public async Task TypeText_KeyDelayOverride_IsPassedToSender()
    {
        var id = Guid.NewGuid();
        var senders = new Senders();
        var action = new BotAction { TargetId = id };
        action.Config[TypeTextAction.TextKey] = "hi";
        action.Config[KeyboardActionBase.KeyDelayKey] = 50;

        await new TypeTextAction(senders.Resolver()).ExecuteAsync(Exec(action, WindowContext(id, (IntPtr)5)), default);

        Assert.Equal(50, senders.SendInput.LastKeyDelayMs);
    }

    [Fact]
    public async Task KeyPress_KeyDelayOverride_IsPassedToSender()
    {
        var id = Guid.NewGuid();
        var senders = new Senders();
        var action = new BotAction { TargetId = id };
        action.Config[KeyPressAction.KeyKey] = "Enter";
        action.Config[KeyboardActionBase.KeyDelayKey] = 35;

        await new KeyPressAction(senders.Resolver()).ExecuteAsync(Exec(action, WindowContext(id, (IntPtr)8)), default);

        Assert.Equal(35, senders.SendInput.LastKeyDelayMs);
    }
```

Update the existing config-field order test `KeyPress_ConfigFields_KeyAndModifiersThenMethod` to expect the new field before the method field:

```csharp
        Assert.Equal(
            new[] { KeyPressAction.KeyKey, KeyPressAction.CtrlKey, KeyPressAction.AltKey, KeyPressAction.ShiftKey, KeyPressAction.WinKey, KeyboardActionBase.KeyDelayKey, InputActionBase.MethodKey },
            def.ConfigFields.Select(f => f.Key));
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test AdbCore.Tests/AdbCore.Tests.csproj --filter "FullyQualifiedName~KeyboardActionsTests"`
Expected: FAIL to compile (`LastKeyDelayMs`, `KeyboardActionBase` do not exist yet).

- [ ] **Step 3: Change the `IInputSender` keyboard contract**

In `IInputSender.cs`, replace the two keyboard method signatures (leave the four mouse methods untouched):

```csharp
    /// <summary>Types the given text into <paramref name="windowHandle"/>, pausing <paramref name="keyDelayMs"/>
    /// ms after each synthetic key event so fast targets don't drop or auto-repeat keys.</summary>
    Task TypeText(IntPtr windowHandle, string text, int keyDelayMs, CancellationToken ct);

    /// <summary>Presses <paramref name="virtualKey"/> while holding <paramref name="modifiers"/>, then releases,
    /// pausing <paramref name="keyDelayMs"/> ms after each synthetic key event.</summary>
    Task KeyPress(IntPtr windowHandle, ushort virtualKey, KeyModifiers modifiers, int keyDelayMs, CancellationToken ct);
```

- [ ] **Step 4: De-batch and pace `Win32SendInputSender`**

In `Win32SendInputSender.cs`, replace `TypeText` and `KeyPress`, and add a `PaceAsync` helper:

```csharp
    public async Task TypeText(IntPtr windowHandle, string text, int keyDelayMs, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(text))
        {
            return; // nothing to type — don't steal focus
        }

        SetForegroundWindow(windowHandle);

        // Send each key event in its own SendInput call and pace between them. Batching every
        // down/up into one call floods the target's input queue, dropping KEYUPs and triggering
        // OS auto-repeat (the source of the garbled, repeated-character output).
        foreach (var ch in text)
        {
            Send(Key(0, ch, KEYEVENTF_UNICODE));
            await PaceAsync(keyDelayMs, ct);
            Send(Key(0, ch, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
            await PaceAsync(keyDelayMs, ct);
        }
    }

    public async Task KeyPress(IntPtr windowHandle, ushort virtualKey, KeyModifiers modifiers, int keyDelayMs, CancellationToken ct)
    {
        SetForegroundWindow(windowHandle);

        if (modifiers.HasFlag(KeyModifiers.Control)) { Send(Key(VK_CONTROL, 0, 0)); await PaceAsync(keyDelayMs, ct); }
        if (modifiers.HasFlag(KeyModifiers.Alt)) { Send(Key(VK_MENU, 0, 0)); await PaceAsync(keyDelayMs, ct); }
        if (modifiers.HasFlag(KeyModifiers.Shift)) { Send(Key(VK_SHIFT, 0, 0)); await PaceAsync(keyDelayMs, ct); }
        if (modifiers.HasFlag(KeyModifiers.Win)) { Send(Key(VK_LWIN, 0, 0)); await PaceAsync(keyDelayMs, ct); }

        Send(Key(virtualKey, 0, 0));
        await PaceAsync(keyDelayMs, ct);
        Send(Key(virtualKey, 0, KEYEVENTF_KEYUP));
        await PaceAsync(keyDelayMs, ct);

        if (modifiers.HasFlag(KeyModifiers.Win)) { Send(Key(VK_LWIN, 0, KEYEVENTF_KEYUP)); await PaceAsync(keyDelayMs, ct); }
        if (modifiers.HasFlag(KeyModifiers.Shift)) { Send(Key(VK_SHIFT, 0, KEYEVENTF_KEYUP)); await PaceAsync(keyDelayMs, ct); }
        if (modifiers.HasFlag(KeyModifiers.Alt)) { Send(Key(VK_MENU, 0, KEYEVENTF_KEYUP)); await PaceAsync(keyDelayMs, ct); }
        if (modifiers.HasFlag(KeyModifiers.Control)) { Send(Key(VK_CONTROL, 0, KEYEVENTF_KEYUP)); await PaceAsync(keyDelayMs, ct); }
    }

    private static Task PaceAsync(int delayMs, CancellationToken ct)
        => delayMs > 0 ? Task.Delay(delayMs, ct) : Task.CompletedTask;
```

- [ ] **Step 5: Make `Win32PostMessageSender` keyboard methods async + paced**

In `Win32PostMessageSender.cs`, replace `TypeText` and `KeyPress` and add the same `PaceAsync` helper:

```csharp
    public async Task TypeText(IntPtr windowHandle, string text, int keyDelayMs, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        foreach (var ch in text)
        {
            PostMessage(windowHandle, WM_CHAR, (IntPtr)ch, IntPtr.Zero);
            await PaceAsync(keyDelayMs, ct);
        }
    }

    public async Task KeyPress(IntPtr windowHandle, ushort virtualKey, KeyModifiers modifiers, int keyDelayMs, CancellationToken ct)
    {
        if (modifiers.HasFlag(KeyModifiers.Control)) { PostMessage(windowHandle, WM_KEYDOWN, (IntPtr)VK_CONTROL, IntPtr.Zero); await PaceAsync(keyDelayMs, ct); }
        if (modifiers.HasFlag(KeyModifiers.Alt)) { PostMessage(windowHandle, WM_KEYDOWN, (IntPtr)VK_MENU, IntPtr.Zero); await PaceAsync(keyDelayMs, ct); }
        if (modifiers.HasFlag(KeyModifiers.Shift)) { PostMessage(windowHandle, WM_KEYDOWN, (IntPtr)VK_SHIFT, IntPtr.Zero); await PaceAsync(keyDelayMs, ct); }
        if (modifiers.HasFlag(KeyModifiers.Win)) { PostMessage(windowHandle, WM_KEYDOWN, (IntPtr)VK_LWIN, IntPtr.Zero); await PaceAsync(keyDelayMs, ct); }

        PostMessage(windowHandle, WM_KEYDOWN, (IntPtr)virtualKey, IntPtr.Zero);
        await PaceAsync(keyDelayMs, ct);
        PostMessage(windowHandle, WM_KEYUP, (IntPtr)virtualKey, IntPtr.Zero);
        await PaceAsync(keyDelayMs, ct);

        if (modifiers.HasFlag(KeyModifiers.Win)) { PostMessage(windowHandle, WM_KEYUP, (IntPtr)VK_LWIN, IntPtr.Zero); await PaceAsync(keyDelayMs, ct); }
        if (modifiers.HasFlag(KeyModifiers.Shift)) { PostMessage(windowHandle, WM_KEYUP, (IntPtr)VK_SHIFT, IntPtr.Zero); await PaceAsync(keyDelayMs, ct); }
        if (modifiers.HasFlag(KeyModifiers.Alt)) { PostMessage(windowHandle, WM_KEYUP, (IntPtr)VK_MENU, IntPtr.Zero); await PaceAsync(keyDelayMs, ct); }
        if (modifiers.HasFlag(KeyModifiers.Control)) { PostMessage(windowHandle, WM_KEYUP, (IntPtr)VK_CONTROL, IntPtr.Zero); await PaceAsync(keyDelayMs, ct); }
    }

    private static Task PaceAsync(int delayMs, CancellationToken ct)
        => delayMs > 0 ? Task.Delay(delayMs, ct) : Task.CompletedTask;
```

- [ ] **Step 6: Update the `RecordingInputSender` test double**

In `RecordingInputSender.cs`, add a `LastKeyDelayMs` property and make the keyboard methods async:

```csharp
    public int LastKeyDelayMs { get; private set; }
```

```csharp
    public Task TypeText(IntPtr windowHandle, string text, int keyDelayMs, CancellationToken ct)
    {
        LastOp = "TypeText";
        LastWindow = windowHandle;
        LastText = text;
        LastKeyDelayMs = keyDelayMs;
        Calls++;
        return Task.CompletedTask;
    }

    public Task KeyPress(IntPtr windowHandle, ushort virtualKey, KeyModifiers modifiers, int keyDelayMs, CancellationToken ct)
    {
        LastOp = "KeyPress";
        LastWindow = windowHandle;
        LastVk = virtualKey;
        LastModifiers = modifiers;
        LastKeyDelayMs = keyDelayMs;
        Calls++;
        return Task.CompletedTask;
    }
```

- [ ] **Step 7: Create `KeyboardActionBase`**

Create `AdbCore/Actions/BuiltIn/KeyboardActionBase.cs`:

```csharp
using AdbCore.Execution;
using AdbCore.Input;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Shared base for keyboard Input actions (Type Text / Key Press). Adds the per-node
/// "Key Delay (ms)" field that paces synthetic key events so fast targets don't drop or auto-repeat
/// keys, and exposes the resolved delay to subclasses.</summary>
public abstract class KeyboardActionBase : InputActionBase
{
    public const string KeyDelayKey = "keyDelayMs";

    /// <summary>Default inter-key delay (ms) applied after each synthetic key down/up event. Reliable
    /// for normal desktop apps out of the box; user-overridable per node.</summary>
    public const int DefaultKeyDelayMs = 20;

    protected KeyboardActionBase(InputSenderResolver senders) : base(senders)
    {
    }

    /// <summary>The keyboard action's own config fields, shown before the shared Key Delay + Input Method fields.</summary>
    protected abstract IEnumerable<ConfigField> KeyboardConfigFields { get; }

    protected override IEnumerable<ConfigField> ActionConfigFields =>
    [
        .. KeyboardConfigFields,
        new ConfigField
        {
            Key = KeyDelayKey,
            Label = "Key Delay (ms)",
            Type = ConfigFieldType.Number,
            DefaultValue = DefaultKeyDelayMs,
        },
    ];

    /// <summary>Resolves the configured inter-key delay (ms), defaulting to <see cref="DefaultKeyDelayMs"/>.</summary>
    protected int KeyDelayMs(ActionExecutionContext context)
        => ConfigValues.GetInt(context.Action.Config, KeyDelayKey, DefaultKeyDelayMs);
}
```

- [ ] **Step 8: Re-point `TypeTextAction` and `KeyPressAction` at `KeyboardActionBase`**

`TypeTextAction.cs`: change base to `KeyboardActionBase`, rename its config-field property to `KeyboardConfigFields`, and await the async sender:

```csharp
public sealed class TypeTextAction : KeyboardActionBase
{
    public const string TextKey = "text";

    public TypeTextAction(InputSenderResolver senders) : base(senders)
    {
    }

    public override string TypeKey => "input.typeText";
    public override string DisplayName => "Type Text";
    public override string Description => "Types text into the target window.";

    protected override IEnumerable<ConfigField> KeyboardConfigFields =>
    [
        new ConfigField { Key = TextKey, Label = "Text", Type = ConfigFieldType.MultilineString },
    ];

    protected override async Task<ActionResult> PerformAsync(IInputSender sender, IntPtr windowHandle, ActionExecutionContext context, CancellationToken ct)
    {
        // Empty text is an intentional no-op (consistent with Log / Set Variable); the sender skips it.
        var text = ConfigValues.GetString(context.Action.Config, TextKey);
        await sender.TypeText(windowHandle, text, KeyDelayMs(context), ct);
        return ActionResult.Ok(SuccessPort);
    }
}
```

`KeyPressAction.cs`: change base to `KeyboardActionBase`, rename `ActionConfigFields` → `KeyboardConfigFields`, and await the async sender:

```csharp
public sealed class KeyPressAction : KeyboardActionBase
{
    public const string KeyKey = "key";
    public const string CtrlKey = "ctrl";
    public const string AltKey = "alt";
    public const string ShiftKey = "shift";
    public const string WinKey = "win";

    public KeyPressAction(InputSenderResolver senders) : base(senders)
    {
    }

    public override string TypeKey => "input.keyPress";
    public override string DisplayName => "Key Press";
    public override string Description => "Presses a key, with optional Ctrl/Alt/Shift/Win modifiers.";

    protected override IEnumerable<ConfigField> KeyboardConfigFields =>
    [
        new ConfigField { Key = KeyKey, Label = "Key", Type = ConfigFieldType.String },
        new ConfigField { Key = CtrlKey, Label = "Ctrl", Type = ConfigFieldType.Boolean, DefaultValue = false },
        new ConfigField { Key = AltKey, Label = "Alt", Type = ConfigFieldType.Boolean, DefaultValue = false },
        new ConfigField { Key = ShiftKey, Label = "Shift", Type = ConfigFieldType.Boolean, DefaultValue = false },
        new ConfigField { Key = WinKey, Label = "Win", Type = ConfigFieldType.Boolean, DefaultValue = false },
    ];

    protected override async Task<ActionResult> PerformAsync(IInputSender sender, IntPtr windowHandle, ActionExecutionContext context, CancellationToken ct)
    {
        var keyName = ConfigValues.GetString(context.Action.Config, KeyKey);
        if (string.IsNullOrWhiteSpace(keyName))
        {
            return ActionResult.Fail("Key Press: a key is required.");
        }

        if (!VirtualKeys.TryResolve(keyName, out var vk))
        {
            return ActionResult.Fail($"Key Press: unrecognized key '{keyName}'.");
        }

        var modifiers = KeyModifiers.None;
        if (ConfigValues.GetBool(context.Action.Config, CtrlKey)) modifiers |= KeyModifiers.Control;
        if (ConfigValues.GetBool(context.Action.Config, AltKey)) modifiers |= KeyModifiers.Alt;
        if (ConfigValues.GetBool(context.Action.Config, ShiftKey)) modifiers |= KeyModifiers.Shift;
        if (ConfigValues.GetBool(context.Action.Config, WinKey)) modifiers |= KeyModifiers.Win;

        await sender.KeyPress(windowHandle, vk, modifiers, KeyDelayMs(context), ct);
        return ActionResult.Ok(SuccessPort);
    }
}
```

- [ ] **Step 9: Run tests — expect green**

Run: `dotnet test AdbCore.Tests/AdbCore.Tests.csproj --filter "FullyQualifiedName~Actions.BuiltIn"`
Expected: PASS (default-delay, override, and config-order tests pass; all prior input tests still pass).

- [ ] **Step 10: Commit**

```bash
git add AdbCore/Input/IInputSender.cs AdbCore/Input/Win32SendInputSender.cs AdbCore/Input/Win32PostMessageSender.cs AdbCore.Tests/Input/RecordingInputSender.cs AdbCore/Actions/BuiltIn/KeyboardActionBase.cs AdbCore/Actions/BuiltIn/TypeTextAction.cs AdbCore/Actions/BuiltIn/KeyPressAction.cs AdbCore.Tests/Actions/BuiltIn/KeyboardActionsTests.cs
git commit -m "fix(input): configurable, paced key delay to stop fast-injection corruption"
```

---

## Task 3: Prove cancellation propagates through a long type

A 300-iteration typing loop must be stoppable mid-string. With the pipeline now async and the senders awaiting `Task.Delay(..., ct)`, the action should surface `OperationCanceledException` when the token cancels. This test uses a sender that blocks on the token to prove the action awaits and propagates it.

**Files:**
- Test: `AdbCore.Tests/Actions/BuiltIn/KeyboardActionsTests.cs`

- [ ] **Step 1: Write the cancellation test**

Add a private blocking sender and a test to `KeyboardActionsTests.cs`:

```csharp
    private sealed class BlockingSender : IInputSender
    {
        public void Click(IntPtr windowHandle, int clientX, int clientY) { }
        public void RightClick(IntPtr windowHandle, int clientX, int clientY) { }
        public void DoubleClick(IntPtr windowHandle, int clientX, int clientY) { }
        public void MoveTo(IntPtr windowHandle, int clientX, int clientY) { }
        public Task TypeText(IntPtr windowHandle, string text, int keyDelayMs, CancellationToken ct) => Task.Delay(Timeout.Infinite, ct);
        public Task KeyPress(IntPtr windowHandle, ushort virtualKey, KeyModifiers modifiers, int keyDelayMs, CancellationToken ct) => Task.Delay(Timeout.Infinite, ct);
    }

    [Fact]
    public async Task TypeText_HonorsCancellation()
    {
        var id = Guid.NewGuid();
        var blocking = new BlockingSender();
        var resolver = new InputSenderResolver(blocking, blocking);
        var action = new BotAction { TargetId = id };
        action.Config[TypeTextAction.TextKey] = "hello";

        using var cts = new CancellationTokenSource();
        var task = new TypeTextAction(resolver).ExecuteAsync(Exec(action, WindowContext(id, (IntPtr)5)), cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }
```

- [ ] **Step 2: Run the test — expect green**

Run: `dotnet test AdbCore.Tests/AdbCore.Tests.csproj --filter "FullyQualifiedName~TypeText_HonorsCancellation"`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add AdbCore.Tests/Actions/BuiltIn/KeyboardActionsTests.cs
git commit -m "test(input): assert Type Text propagates cancellation mid-type"
```

---

## Final verification

- [ ] Run the full suite: `dotnet test ADB.slnx`
- [ ] Expected: all tests pass (prior count + the new delay/cancellation tests).
- [ ] Hands-on (user): run the `Untitled.bot` (Loop 300 → Type "Hello, World!" → Enter) against Notepad; output should be clean lines of `Hello, World!` with no repeated/dropped characters. The default 20 ms applies automatically; the "Key Delay (ms)" field on Type Text / Key Press lets it be tuned per node.
