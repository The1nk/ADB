# M5c2b — Keyboard Input Actions (Type Text, Key Press) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the keyboard Input actions — **Type Text** (types a string) and **Key Press** (a key with optional Ctrl/Alt/Shift/Win modifiers) — completing the Input category.

**Architecture:** Extend `IInputSender` with `TypeText` and `KeyPress(vk, KeyModifiers)`; the SendInput adapter's `INPUT` struct becomes a proper mouse/keyboard union (explicit layout) so keyboard events can be injected. A pure `VirtualKeys` table maps friendly key names → virtual-key codes. Extract `InputActionBase` (HWND resolution + input-method selection + ports) so the keyboard actions and the existing pointer actions share that plumbing; `PointerActionBase` becomes a subclass. Key Press uses modifier *toggle* config fields (Ctrl/Alt/Shift/Win booleans), per the user's choice.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`, Win32 P/Invoke — runs x64), xUnit. Projects: `AdbCore`, `AdbCore.Tests`. Solution: `ADB.slnx`.

**Design reference:** `Docs/Specs/2026-06-01-m5-built-in-actions-design.md` §4.2 (Input). Builds on M5c1/M5c2a (merged): `IInputSender`, `Win32SendInputSender`, `Win32PostMessageSender`, `InputSenderResolver`, `PointerActionBase`, `RecordingInputSender`.

---

## Background the implementer needs

- **Input pipeline (in `main`):** an action resolves the target HWND from `Context.Targets` (explicit `TargetId`, or the sole target if unset), reads its config, reads a `method` config field (Enum, default `SendInput`), and calls `InputSenderResolver.Resolve(method)` → `Win32SendInputSender` (foreground, reliable) or `Win32PostMessageSender` (background). `PointerActionBase` currently owns this for the mouse actions.
- **`IInputSender`** has Click/RightClick/DoubleClick/MoveTo. This slice adds `TypeText(IntPtr, string)` and `KeyPress(IntPtr, ushort virtualKey, KeyModifiers modifiers)`.
- **`Win32SendInputSender.INPUT`** is currently a Sequential struct holding only `MOUSEINPUT`. To inject keyboard events it must become an explicit-layout union of `MOUSEINPUT`/`KEYBDINPUT`. **The project runs x64**, so the union members sit at `FieldOffset(8)` (4-byte `type` + 4 pad, then 8-aligned union) and `Marshal.SizeOf<INPUT>()` must remain 40.
- **`ConfigValues.GetString/GetInt/GetBool`** read config (handle JsonElement). `ActionResult.Ok("onSuccess")` / `Fail(msg)`.
- **Win32 adapters are build-only (no unit tests)** — verified by the manual checklist. Pure logic (VirtualKeys, the actions' dispatch/selection via a recording fake) IS unit-tested.
- **Current registry counts:** 14 definitions / 11 executors; Input palette = 4. After M5c2b: **16 / 13**, Input = 6.
- Strict TDD for pure logic.

## Build / test commands (from worktree root)

- Single class: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Input.VirtualKeysTests"`
- Full suite: `dotnet test ADB.slnx`
- Zero-warning build (hard gate): `dotnet build ADB.slnx`

---

## File Structure

- **Create** `AdbCore/Input/KeyModifiers.cs` — `[Flags]` enum (None/Control/Alt/Shift/Win).
- **Create** `AdbCore/Input/VirtualKeys.cs` — pure key-name → VK resolver.
- **Modify** `AdbCore/Input/IInputSender.cs` — add `TypeText`, `KeyPress`.
- **Modify** `AdbCore/Input/Win32SendInputSender.cs` — INPUT union + `TypeText`/`KeyPress`.
- **Modify** `AdbCore/Input/Win32PostMessageSender.cs` — `TypeText` (WM_CHAR) / `KeyPress` (WM_KEYDOWN/UP).
- **Create** `AdbCore/Actions/BuiltIn/InputActionBase.cs` — abstract base (HWND + method + ports + ExecuteAsync template).
- **Modify** `AdbCore/Actions/BuiltIn/PointerActionBase.cs` — becomes `: InputActionBase`.
- **Create** `AdbCore/Actions/BuiltIn/TypeTextAction.cs`, `KeyPressAction.cs`.
- **Modify** `AdbCore/Actions/BuiltIn/BuiltInActions.cs` — register the two.
- **Modify** `AdbCore.Tests/Input/RecordingInputSender.cs` — record TypeText/KeyPress.
- **Create** `AdbCore.Tests/Input/VirtualKeysTests.cs`, `AdbCore.Tests/Actions/BuiltIn/KeyboardActionsTests.cs`.
- **Modify** `AdbCore.Tests/Actions/BuiltIn/ClickActionTests.cs` (its fake gains the 2 methods), `BuiltInActionsTests.cs`, `BotBuilder.Core.Tests/PaletteViewModelTests.cs` — counts.

---

## Task 1: `VirtualKeys` key-name → virtual-key resolver (pure)

**Files:**
- Create: `AdbCore/Input/VirtualKeys.cs`
- Test: `AdbCore.Tests/Input/VirtualKeysTests.cs`

- [ ] **Step 1: Write the failing tests** — `AdbCore.Tests/Input/VirtualKeysTests.cs`:

```csharp
using AdbCore.Input;
using Xunit;

namespace AdbCore.Tests.Input;

public class VirtualKeysTests
{
    [Theory]
    [InlineData("A", 0x41)]
    [InlineData("z", 0x5A)]          // case-insensitive -> Z
    [InlineData("0", 0x30)]
    [InlineData("9", 0x39)]
    [InlineData("Enter", 0x0D)]
    [InlineData("Return", 0x0D)]
    [InlineData("Esc", 0x1B)]
    [InlineData("Escape", 0x1B)]
    [InlineData("Tab", 0x09)]
    [InlineData("Space", 0x20)]
    [InlineData("Backspace", 0x08)]
    [InlineData("Delete", 0x2E)]
    [InlineData("Up", 0x26)]
    [InlineData("Down", 0x28)]
    [InlineData("Left", 0x25)]
    [InlineData("Right", 0x27)]
    [InlineData("F1", 0x70)]
    [InlineData("F12", 0x7B)]
    [InlineData("home", 0x24)]       // case-insensitive named key
    public void TryResolve_Known_ReturnsVk(string name, int expectedVk)
    {
        Assert.True(VirtualKeys.TryResolve(name, out var vk));
        Assert.Equal((ushort)expectedVk, vk);
    }

    [Theory]
    [InlineData("")]
    [InlineData("AB")]               // multi-char non-named
    [InlineData("F13")]              // out of supported F-range
    [InlineData("NotAKey")]
    public void TryResolve_Unknown_ReturnsFalse(string name)
    {
        Assert.False(VirtualKeys.TryResolve(name, out var vk));
        Assert.Equal((ushort)0, vk);
    }
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Input.VirtualKeysTests"` → FAIL to compile.

- [ ] **Step 3: Implement** — `AdbCore/Input/VirtualKeys.cs`:

```csharp
using System.Globalization;

namespace AdbCore.Input;

/// <summary>Resolves friendly key names (e.g. "Enter", "F5", "A", "Up") to Win32 virtual-key codes.
/// Case-insensitive. Single letters A–Z and digits 0–9 map directly; named keys via a table.</summary>
public static class VirtualKeys
{
    private static readonly Dictionary<string, ushort> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Enter"] = 0x0D, ["Return"] = 0x0D,
        ["Esc"] = 0x1B, ["Escape"] = 0x1B,
        ["Tab"] = 0x09, ["Space"] = 0x20, ["Backspace"] = 0x08,
        ["Delete"] = 0x2E, ["Del"] = 0x2E, ["Insert"] = 0x2D,
        ["Home"] = 0x24, ["End"] = 0x23, ["PageUp"] = 0x21, ["PageDown"] = 0x22,
        ["Up"] = 0x26, ["Down"] = 0x28, ["Left"] = 0x25, ["Right"] = 0x27,
    };

    /// <summary>Resolves a key name to its virtual-key code. Returns false (and vk=0) if unrecognized.</summary>
    public static bool TryResolve(string keyName, out ushort virtualKey)
    {
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(keyName))
        {
            return false;
        }

        var key = keyName.Trim();

        if (key.Length == 1)
        {
            var c = char.ToUpperInvariant(key[0]);
            if (c is >= 'A' and <= 'Z')
            {
                virtualKey = (ushort)c;
                return true;
            }

            if (c is >= '0' and <= '9')
            {
                virtualKey = (ushort)c;
                return true;
            }
        }

        if (Named.TryGetValue(key, out var named))
        {
            virtualKey = named;
            return true;
        }

        // Function keys F1–F12 -> 0x70–0x7B
        if ((key[0] is 'F' or 'f') && int.TryParse(key.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 12)
        {
            virtualKey = (ushort)(0x70 + (n - 1));
            return true;
        }

        return false;
    }
}
```

- [ ] **Step 4: Run to verify pass** — same filter → PASS (22 theory cases).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Input/VirtualKeys.cs AdbCore.Tests/Input/VirtualKeysTests.cs
git commit -m "feat(core): add VirtualKeys key-name to virtual-key resolver"
```

---

## Task 2: `KeyModifiers` + extend `IInputSender` + both adapters

**Files:**
- Create: `AdbCore/Input/KeyModifiers.cs`
- Modify: `AdbCore/Input/IInputSender.cs`, `Win32SendInputSender.cs`, `Win32PostMessageSender.cs`
- Modify: `AdbCore.Tests/Input/RecordingInputSender.cs`, `AdbCore.Tests/Actions/BuiltIn/ClickActionTests.cs`

- [ ] **Step 1: Create `AdbCore/Input/KeyModifiers.cs`:**

```csharp
namespace AdbCore.Input;

/// <summary>Modifier keys that may be held while a Key Press fires.</summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Win = 8,
}
```

- [ ] **Step 2: Extend `AdbCore/Input/IInputSender.cs`** — add these two members after `MoveTo`:

```csharp
    /// <summary>Types the given text into <paramref name="windowHandle"/> as a sequence of characters.</summary>
    void TypeText(IntPtr windowHandle, string text);

    /// <summary>Presses <paramref name="virtualKey"/> while holding <paramref name="modifiers"/>, then releases.</summary>
    void KeyPress(IntPtr windowHandle, ushort virtualKey, KeyModifiers modifiers);
```

- [ ] **Step 3: Replace `AdbCore/Input/Win32SendInputSender.cs`** with (INPUT becomes a mouse/keyboard union; keyboard methods added):

```csharp
using System.Runtime.InteropServices;

namespace AdbCore.Input;

/// <summary>Foreground <see cref="IInputSender"/>: activates the target window and injects real OS input
/// via SendInput (mouse at window-relative coordinates; keyboard as Unicode text or virtual-key presses).
/// Reliable across modern apps, but foreground-only — it moves the real cursor and drives one window at a time.</summary>
public sealed class Win32SendInputSender : IInputSender
{
    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_MENU = 0x12;   // Alt
    private const ushort VK_LWIN = 0x5B;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // INPUT is a union: a 4-byte type tag, then (on x64, after 4 bytes padding for 8-byte alignment) the
    // mouse OR keyboard payload overlaid at offset 8. sizeof is 40 on x64, matching the Win32 INPUT struct.
    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public MOUSEINPUT mi;
        [FieldOffset(8)] public KEYBDINPUT ki;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    public void Click(IntPtr windowHandle, int clientX, int clientY)
    {
        PositionCursor(windowHandle, clientX, clientY);
        Send(Mouse(MOUSEEVENTF_LEFTDOWN), Mouse(MOUSEEVENTF_LEFTUP));
    }

    public void RightClick(IntPtr windowHandle, int clientX, int clientY)
    {
        PositionCursor(windowHandle, clientX, clientY);
        Send(Mouse(MOUSEEVENTF_RIGHTDOWN), Mouse(MOUSEEVENTF_RIGHTUP));
    }

    public void DoubleClick(IntPtr windowHandle, int clientX, int clientY)
    {
        PositionCursor(windowHandle, clientX, clientY);
        // Two down/up pairs in one batch; the OS treats simultaneous injection as within the double-click threshold.
        Send(Mouse(MOUSEEVENTF_LEFTDOWN), Mouse(MOUSEEVENTF_LEFTUP), Mouse(MOUSEEVENTF_LEFTDOWN), Mouse(MOUSEEVENTF_LEFTUP));
    }

    // A bare move must NOT activate the window — a hover shouldn't steal focus; only the clicks activate.
    public void MoveTo(IntPtr windowHandle, int clientX, int clientY)
        => MoveCursor(windowHandle, clientX, clientY);

    public void TypeText(IntPtr windowHandle, string text)
    {
        SetForegroundWindow(windowHandle);
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var inputs = new List<INPUT>(text.Length * 2);
        foreach (var ch in text)
        {
            inputs.Add(Key(0, ch, KEYEVENTF_UNICODE));
            inputs.Add(Key(0, ch, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
        }

        Send(inputs.ToArray());
    }

    public void KeyPress(IntPtr windowHandle, ushort virtualKey, KeyModifiers modifiers)
    {
        SetForegroundWindow(windowHandle);

        var inputs = new List<INPUT>();
        if (modifiers.HasFlag(KeyModifiers.Control)) inputs.Add(Key(VK_CONTROL, 0, 0));
        if (modifiers.HasFlag(KeyModifiers.Alt)) inputs.Add(Key(VK_MENU, 0, 0));
        if (modifiers.HasFlag(KeyModifiers.Shift)) inputs.Add(Key(VK_SHIFT, 0, 0));
        if (modifiers.HasFlag(KeyModifiers.Win)) inputs.Add(Key(VK_LWIN, 0, 0));

        inputs.Add(Key(virtualKey, 0, 0));
        inputs.Add(Key(virtualKey, 0, KEYEVENTF_KEYUP));

        if (modifiers.HasFlag(KeyModifiers.Win)) inputs.Add(Key(VK_LWIN, 0, KEYEVENTF_KEYUP));
        if (modifiers.HasFlag(KeyModifiers.Shift)) inputs.Add(Key(VK_SHIFT, 0, KEYEVENTF_KEYUP));
        if (modifiers.HasFlag(KeyModifiers.Alt)) inputs.Add(Key(VK_MENU, 0, KEYEVENTF_KEYUP));
        if (modifiers.HasFlag(KeyModifiers.Control)) inputs.Add(Key(VK_CONTROL, 0, KEYEVENTF_KEYUP));

        Send(inputs.ToArray());
    }

    private static INPUT Mouse(uint flags)
        => new() { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = flags } };

    private static INPUT Key(ushort vk, ushort scan, uint flags)
        => new() { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } };

    private static void Send(params INPUT[] inputs)
        => SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());

    private static void PositionCursor(IntPtr windowHandle, int clientX, int clientY)
    {
        SetForegroundWindow(windowHandle); // activate the target so the injected click lands on it
        MoveCursor(windowHandle, clientX, clientY);
    }

    private static void MoveCursor(IntPtr windowHandle, int clientX, int clientY)
    {
        var point = new POINT { X = clientX, Y = clientY };
        ClientToScreen(windowHandle, ref point);
        SetCursorPos(point.X, point.Y);
    }
}
```

- [ ] **Step 4: Replace `AdbCore/Input/Win32PostMessageSender.cs`** with (keyboard via WM_CHAR / WM_KEYDOWN/UP):

```csharp
using System.Runtime.InteropServices;

namespace AdbCore.Input;

/// <summary>PostMessage implementation of <see cref="IInputSender"/>: posts messages so a window need not
/// be foreground. Note: some apps and most games ignore synthesized messages, and modifier state (Ctrl/Alt/
/// Shift) set via posted key messages is not seen by GetKeyState — so chords are unreliable here. The
/// foreground SendInput sender is the dependable path.</summary>
public sealed class Win32PostMessageSender : IInputSender
{
    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONDOWN = 0x0204;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const uint WM_CHAR = 0x0102;
    private const int MK_LBUTTON = 0x0001;
    private const int MK_RBUTTON = 0x0002;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_MENU = 0x12;
    private const ushort VK_LWIN = 0x5B;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public void Click(IntPtr windowHandle, int clientX, int clientY)
    {
        var lParam = MakeLParam(clientX, clientY);
        PostMessage(windowHandle, WM_LBUTTONDOWN, (IntPtr)MK_LBUTTON, lParam);
        PostMessage(windowHandle, WM_LBUTTONUP, IntPtr.Zero, lParam);
    }

    public void RightClick(IntPtr windowHandle, int clientX, int clientY)
    {
        var lParam = MakeLParam(clientX, clientY);
        PostMessage(windowHandle, WM_RBUTTONDOWN, (IntPtr)MK_RBUTTON, lParam);
        PostMessage(windowHandle, WM_RBUTTONUP, IntPtr.Zero, lParam);
    }

    public void DoubleClick(IntPtr windowHandle, int clientX, int clientY)
    {
        var lParam = MakeLParam(clientX, clientY);
        PostMessage(windowHandle, WM_LBUTTONDOWN, (IntPtr)MK_LBUTTON, lParam);
        PostMessage(windowHandle, WM_LBUTTONUP, IntPtr.Zero, lParam);
        PostMessage(windowHandle, WM_LBUTTONDBLCLK, (IntPtr)MK_LBUTTON, lParam);
        PostMessage(windowHandle, WM_LBUTTONUP, IntPtr.Zero, lParam);
    }

    public void MoveTo(IntPtr windowHandle, int clientX, int clientY)
        => PostMessage(windowHandle, WM_MOUSEMOVE, IntPtr.Zero, MakeLParam(clientX, clientY));

    public void TypeText(IntPtr windowHandle, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        foreach (var ch in text)
        {
            PostMessage(windowHandle, WM_CHAR, (IntPtr)ch, IntPtr.Zero);
        }
    }

    public void KeyPress(IntPtr windowHandle, ushort virtualKey, KeyModifiers modifiers)
    {
        if (modifiers.HasFlag(KeyModifiers.Control)) PostMessage(windowHandle, WM_KEYDOWN, (IntPtr)VK_CONTROL, IntPtr.Zero);
        if (modifiers.HasFlag(KeyModifiers.Alt)) PostMessage(windowHandle, WM_KEYDOWN, (IntPtr)VK_MENU, IntPtr.Zero);
        if (modifiers.HasFlag(KeyModifiers.Shift)) PostMessage(windowHandle, WM_KEYDOWN, (IntPtr)VK_SHIFT, IntPtr.Zero);
        if (modifiers.HasFlag(KeyModifiers.Win)) PostMessage(windowHandle, WM_KEYDOWN, (IntPtr)VK_LWIN, IntPtr.Zero);

        PostMessage(windowHandle, WM_KEYDOWN, (IntPtr)virtualKey, IntPtr.Zero);
        PostMessage(windowHandle, WM_KEYUP, (IntPtr)virtualKey, IntPtr.Zero);

        if (modifiers.HasFlag(KeyModifiers.Win)) PostMessage(windowHandle, WM_KEYUP, (IntPtr)VK_LWIN, IntPtr.Zero);
        if (modifiers.HasFlag(KeyModifiers.Shift)) PostMessage(windowHandle, WM_KEYUP, (IntPtr)VK_SHIFT, IntPtr.Zero);
        if (modifiers.HasFlag(KeyModifiers.Alt)) PostMessage(windowHandle, WM_KEYUP, (IntPtr)VK_MENU, IntPtr.Zero);
        if (modifiers.HasFlag(KeyModifiers.Control)) PostMessage(windowHandle, WM_KEYUP, (IntPtr)VK_CONTROL, IntPtr.Zero);
    }

    // Cast through uint so a y >= 32768 does not sign-extend into the high 32 bits of the IntPtr
    // (matches the Win32 MAKELPARAM macro's unsigned semantics).
    private static IntPtr MakeLParam(int x, int y)
        => (IntPtr)(uint)((y << 16) | (x & 0xFFFF));
}
```

- [ ] **Step 5: Update the test doubles to implement the grown interface.**

In `AdbCore.Tests/Input/RecordingInputSender.cs`, add fields + the two methods (records text / vk+modifiers). Replace the file with:

```csharp
using AdbCore.Input;

namespace AdbCore.Tests.Input;

/// <summary>Test double that records the last input operation and its arguments.</summary>
internal sealed class RecordingInputSender : IInputSender
{
    public string? LastOp { get; private set; }
    public IntPtr LastWindow { get; private set; }
    public int LastX { get; private set; }
    public int LastY { get; private set; }
    public string? LastText { get; private set; }
    public ushort LastVk { get; private set; }
    public KeyModifiers LastModifiers { get; private set; }
    public int Calls { get; private set; }

    public void Click(IntPtr windowHandle, int clientX, int clientY) => RecordMouse("Click", windowHandle, clientX, clientY);
    public void RightClick(IntPtr windowHandle, int clientX, int clientY) => RecordMouse("RightClick", windowHandle, clientX, clientY);
    public void DoubleClick(IntPtr windowHandle, int clientX, int clientY) => RecordMouse("DoubleClick", windowHandle, clientX, clientY);
    public void MoveTo(IntPtr windowHandle, int clientX, int clientY) => RecordMouse("MoveTo", windowHandle, clientX, clientY);

    public void TypeText(IntPtr windowHandle, string text)
    {
        LastOp = "TypeText";
        LastWindow = windowHandle;
        LastText = text;
        Calls++;
    }

    public void KeyPress(IntPtr windowHandle, ushort virtualKey, KeyModifiers modifiers)
    {
        LastOp = "KeyPress";
        LastWindow = windowHandle;
        LastVk = virtualKey;
        LastModifiers = modifiers;
        Calls++;
    }

    private void RecordMouse(string op, IntPtr windowHandle, int x, int y)
    {
        LastOp = op;
        LastWindow = windowHandle;
        LastX = x;
        LastY = y;
        Calls++;
    }
}
```

In `AdbCore.Tests/Actions/BuiltIn/ClickActionTests.cs`, the private `FakeInputSender` must implement the two new members — add after its existing `MoveTo` no-op:

```csharp
        public void TypeText(IntPtr windowHandle, string text) { }

        public void KeyPress(IntPtr windowHandle, ushort virtualKey, KeyModifiers modifiers) { }
```

(If `KeyModifiers` is unresolved in that file, add `using AdbCore.Input;` — it is likely already imported.)

- [ ] **Step 6: Build (0 warnings) + full suite.**

Run: `dotnet build ADB.slnx` → 0 Warning(s), 0 Error(s). (If the explicit-layout INPUT triggers any analyzer warning, report it; the layout is intentional.)
Run: `dotnet test ADB.slnx` → all green.

- [ ] **Step 7: Commit**

```bash
git add AdbCore/Input/KeyModifiers.cs AdbCore/Input/IInputSender.cs AdbCore/Input/Win32SendInputSender.cs AdbCore/Input/Win32PostMessageSender.cs AdbCore.Tests/Input/RecordingInputSender.cs AdbCore.Tests/Actions/BuiltIn/ClickActionTests.cs
git commit -m "feat(core): extend IInputSender with TypeText/KeyPress + Win32 keyboard adapters"
```

---

## Task 3: Extract `InputActionBase` (refactor `PointerActionBase` onto it)

Behavior-preserving. The 9 ClickActionTests + 8 MouseActionsTests must stay green (they reference `PointerActionBase.XKey`/`MethodKey` etc., which remain accessible — `XKey`/`YKey` stay on `PointerActionBase`; `MethodKey`/`SuccessPort`/`FailurePort` move to `InputActionBase` and are inherited).

**Files:**
- Create: `AdbCore/Actions/BuiltIn/InputActionBase.cs`
- Modify: `AdbCore/Actions/BuiltIn/PointerActionBase.cs`

- [ ] **Step 1: Create `AdbCore/Actions/BuiltIn/InputActionBase.cs`:**

```csharp
using AdbCore.Execution;
using AdbCore.Input;
using AdbCore.Models;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Shared base for Input actions: resolves the target window HWND, exposes the input-method
/// config field + ports, and runs the chosen <see cref="IInputSender"/>. Subclasses contribute their own
/// config fields (via <see cref="ActionConfigFields"/>) and the actual operation (via <see cref="Perform"/>).</summary>
public abstract class InputActionBase : IActionDefinition, IActionExecutor
{
    public const string MethodKey = "method";
    public const string SuccessPort = "onSuccess";
    public const string FailurePort = "onFailure";

    private readonly InputSenderResolver _senders;
    private List<ConfigField>? _configFields;

    protected InputActionBase(InputSenderResolver senders)
    {
        ArgumentNullException.ThrowIfNull(senders);
        _senders = senders;
    }

    public abstract string TypeKey { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }
    public string Category => "Input";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public List<PortDefinition> OutputPorts { get; } = new()
    {
        new PortDefinition { Name = SuccessPort, Label = "On Success" },
        new PortDefinition { Name = FailurePort, Label = "On Failure" },
    };

    /// <summary>The subclass's own config fields, shown before the shared Input Method field.</summary>
    protected abstract IEnumerable<ConfigField> ActionConfigFields { get; }

    public List<ConfigField> ConfigFields => _configFields ??=
    [
        .. ActionConfigFields,
        new ConfigField
        {
            Key = MethodKey,
            Label = "Input Method",
            Type = ConfigFieldType.Enum,
            DefaultValue = InputSenderResolver.SendInputMethod,
            Options = new() { InputSenderResolver.SendInputMethod, InputSenderResolver.PostMessageMethod },
        },
    ];

    public bool SupportsRetry => false;

    /// <summary>Runs the action's operation against the resolved window and chosen sender; returns the result.</summary>
    protected abstract ActionResult Perform(IInputSender sender, IntPtr windowHandle, ActionExecutionContext context);

    public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        if (ResolveWindow(context) is not IntPtr hwnd || hwnd == IntPtr.Zero)
        {
            return Task.FromResult(ActionResult.Fail($"{DisplayName} requires a resolved Window target (HWND)."));
        }

        var method = ConfigValues.GetString(context.Action.Config, MethodKey, InputSenderResolver.SendInputMethod);
        return Task.FromResult(Perform(_senders.Resolve(method), hwnd, context));
    }

    /// <summary>Resolves the action's target HWND: the explicit TargetId, or the sole target if unset.</summary>
    private static IntPtr? ResolveWindow(ActionExecutionContext context)
    {
        var targets = context.Context.Targets;
        ResolvedTarget? target = context.Action.TargetId is Guid id
            ? targets.TryGetValue(id, out var t) ? t : null
            : targets.Count == 1 ? targets.Values.First() : null;

        return target?.Handle as IntPtr?;
    }
}
```

- [ ] **Step 2: Replace `AdbCore/Actions/BuiltIn/PointerActionBase.cs`** with (now a subclass of `InputActionBase`):

```csharp
using AdbCore.Execution;
using AdbCore.Input;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Shared base for Input pointer actions (Click / Right Click / Double Click / Mouse Move):
/// adds the X/Y coordinate fields and dispatches the specific pointer operation to the sender.</summary>
public abstract class PointerActionBase : InputActionBase
{
    public const string XKey = "x";
    public const string YKey = "y";

    protected PointerActionBase(InputSenderResolver senders) : base(senders)
    {
    }

    protected override IEnumerable<ConfigField> ActionConfigFields =>
    [
        new ConfigField { Key = XKey, Label = "X", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField { Key = YKey, Label = "Y", Type = ConfigFieldType.Number, DefaultValue = 0 },
    ];

    /// <summary>Dispatches the specific pointer operation (click/right-click/double-click/move) to the sender.</summary>
    protected abstract void Dispatch(IInputSender sender, IntPtr windowHandle, int x, int y);

    protected override ActionResult Perform(IInputSender sender, IntPtr windowHandle, ActionExecutionContext context)
    {
        var x = ConfigValues.GetInt(context.Action.Config, XKey);
        var y = ConfigValues.GetInt(context.Action.Config, YKey);
        Dispatch(sender, windowHandle, x, y);
        return ActionResult.Ok(SuccessPort);
    }
}
```

- [ ] **Step 3: Run the existing input tests (must stay green, unedited)**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Actions.BuiltIn.ClickActionTests|FullyQualifiedName~AdbCore.Tests.Actions.BuiltIn.MouseActionsTests"` → PASS (9 + 8 = 17). ConfigFields order is still [x, y, method] (ActionConfigFields=[x,y] + method), so `Definition_Metadata` assertions of `[XKey, YKey, MethodKey]` still hold.

- [ ] **Step 4: Full suite + build** — `dotnet test ADB.slnx` (all green) + `dotnet build ADB.slnx` (0 warnings).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Actions/BuiltIn/InputActionBase.cs AdbCore/Actions/BuiltIn/PointerActionBase.cs
git commit -m "refactor(actions): extract InputActionBase; PointerActionBase becomes a subclass"
```

---

## Task 4: Type Text + Key Press actions

**Files:**
- Create: `AdbCore/Actions/BuiltIn/TypeTextAction.cs`, `KeyPressAction.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/KeyboardActionsTests.cs`

- [ ] **Step 1: Write the failing tests** — `AdbCore.Tests/Actions/BuiltIn/KeyboardActionsTests.cs`:

```csharp
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Input;
using AdbCore.Models;
using AdbCore.Tests.Input;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class KeyboardActionsTests
{
    private sealed class Senders
    {
        public RecordingInputSender SendInput { get; } = new();
        public RecordingInputSender PostMessage { get; } = new();
        public InputSenderResolver Resolver() => new(SendInput, PostMessage);
    }

    private static BotExecutionContext WindowContext(Guid id, IntPtr handle)
    {
        var ctx = new BotExecutionContext();
        ctx.Targets[id] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "hwnd:1", Handle = handle };
        return ctx;
    }

    private static ActionExecutionContext Exec(BotAction action, BotExecutionContext ctx) => new(action, ctx, _ => { });

    [Fact]
    public async Task TypeText_SendsTextToTarget_ViaSendInput_Default()
    {
        var id = Guid.NewGuid();
        var senders = new Senders();
        var action = new BotAction { TargetId = id };
        action.Config[TypeTextAction.TextKey] = "hello";

        var result = await new TypeTextAction(senders.Resolver()).ExecuteAsync(Exec(action, WindowContext(id, (IntPtr)5)), default);

        Assert.True(result.Success);
        Assert.Equal("onSuccess", result.OutputPort);
        Assert.Equal("TypeText", senders.SendInput.LastOp);
        Assert.Equal("hello", senders.SendInput.LastText);
        Assert.Equal((IntPtr)5, senders.SendInput.LastWindow);
    }

    [Fact]
    public async Task TypeText_PostMessageMethod_RoutesToPostMessageSender()
    {
        var id = Guid.NewGuid();
        var senders = new Senders();
        var action = new BotAction { TargetId = id };
        action.Config[TypeTextAction.TextKey] = "hi";
        action.Config[InputActionBase.MethodKey] = InputSenderResolver.PostMessageMethod;

        await new TypeTextAction(senders.Resolver()).ExecuteAsync(Exec(action, WindowContext(id, (IntPtr)6)), default);

        Assert.Equal("hi", senders.PostMessage.LastText);
        Assert.Equal(0, senders.SendInput.Calls);
    }

    [Fact]
    public async Task TypeText_NoTarget_FailsWithoutSending()
    {
        var senders = new Senders();
        var action = new BotAction { TargetId = null };
        action.Config[TypeTextAction.TextKey] = "x";

        var result = await new TypeTextAction(senders.Resolver()).ExecuteAsync(Exec(action, new BotExecutionContext()), default);

        Assert.False(result.Success);
        Assert.Equal(0, senders.SendInput.Calls);
        Assert.Contains("Window target", result.ErrorMessage);
    }

    [Fact]
    public async Task KeyPress_ResolvesKeyAndModifiers()
    {
        var id = Guid.NewGuid();
        var senders = new Senders();
        var action = new BotAction { TargetId = id };
        action.Config[KeyPressAction.KeyKey] = "C";
        action.Config[KeyPressAction.CtrlKey] = true;
        action.Config[KeyPressAction.ShiftKey] = true;

        var result = await new KeyPressAction(senders.Resolver()).ExecuteAsync(Exec(action, WindowContext(id, (IntPtr)7)), default);

        Assert.True(result.Success);
        Assert.Equal("KeyPress", senders.SendInput.LastOp);
        Assert.Equal((ushort)0x43, senders.SendInput.LastVk); // 'C'
        Assert.Equal(KeyModifiers.Control | KeyModifiers.Shift, senders.SendInput.LastModifiers);
    }

    [Fact]
    public async Task KeyPress_NamedKey_NoModifiers()
    {
        var id = Guid.NewGuid();
        var senders = new Senders();
        var action = new BotAction { TargetId = id };
        action.Config[KeyPressAction.KeyKey] = "Enter";

        await new KeyPressAction(senders.Resolver()).ExecuteAsync(Exec(action, WindowContext(id, (IntPtr)8)), default);

        Assert.Equal((ushort)0x0D, senders.SendInput.LastVk);
        Assert.Equal(KeyModifiers.None, senders.SendInput.LastModifiers);
    }

    [Fact]
    public async Task KeyPress_UnknownKey_FailsWithoutSending()
    {
        var id = Guid.NewGuid();
        var senders = new Senders();
        var action = new BotAction { TargetId = id };
        action.Config[KeyPressAction.KeyKey] = "NotAKey";

        var result = await new KeyPressAction(senders.Resolver()).ExecuteAsync(Exec(action, WindowContext(id, (IntPtr)9)), default);

        Assert.False(result.Success);
        Assert.Equal(0, senders.SendInput.Calls);
        Assert.Contains("NotAKey", result.ErrorMessage);
    }

    [Theory]
    [InlineData(typeof(TypeTextAction), "input.typeText", "Type Text")]
    [InlineData(typeof(KeyPressAction), "input.keyPress", "Key Press")]
    public void Definition_Metadata(Type actionType, string expectedTypeKey, string expectedDisplayName)
    {
        var def = (IActionDefinition)Activator.CreateInstance(actionType, new Senders().Resolver())!;

        Assert.Equal(expectedTypeKey, def.TypeKey);
        Assert.Equal(expectedDisplayName, def.DisplayName);
        Assert.Equal("Input", def.Category);
        Assert.Equal(new[] { "in" }, def.InputPorts.Select(p => p.Name));
        Assert.Equal(new[] { "onSuccess", "onFailure" }, def.OutputPorts.Select(p => p.Name));
        Assert.Contains(def.ConfigFields, f => f.Key == InputActionBase.MethodKey);
        Assert.False(def.SupportsRetry);
    }

    [Fact]
    public void KeyPress_ConfigFields_KeyAndModifiersThenMethod()
    {
        var def = new KeyPressAction(new Senders().Resolver());

        Assert.Equal(
            new[] { KeyPressAction.KeyKey, KeyPressAction.CtrlKey, KeyPressAction.AltKey, KeyPressAction.ShiftKey, KeyPressAction.WinKey, InputActionBase.MethodKey },
            def.ConfigFields.Select(f => f.Key));
    }
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Actions.BuiltIn.KeyboardActionsTests"` → FAIL to compile.

- [ ] **Step 3: Create `AdbCore/Actions/BuiltIn/TypeTextAction.cs`:**

```csharp
using AdbCore.Execution;
using AdbCore.Input;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Types a configured string into the target window.</summary>
public sealed class TypeTextAction : InputActionBase
{
    public const string TextKey = "text";

    public TypeTextAction(InputSenderResolver senders) : base(senders)
    {
    }

    public override string TypeKey => "input.typeText";
    public override string DisplayName => "Type Text";
    public override string Description => "Types text into the target window.";

    protected override IEnumerable<ConfigField> ActionConfigFields =>
    [
        new ConfigField { Key = TextKey, Label = "Text", Type = ConfigFieldType.MultilineString },
    ];

    protected override ActionResult Perform(IInputSender sender, IntPtr windowHandle, ActionExecutionContext context)
    {
        var text = ConfigValues.GetString(context.Action.Config, TextKey);
        sender.TypeText(windowHandle, text);
        return ActionResult.Ok(SuccessPort);
    }
}
```

- [ ] **Step 4: Create `AdbCore/Actions/BuiltIn/KeyPressAction.cs`:**

```csharp
using AdbCore.Execution;
using AdbCore.Input;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Presses a configured key (by name) with optional Ctrl/Alt/Shift/Win modifiers.</summary>
public sealed class KeyPressAction : InputActionBase
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

    protected override IEnumerable<ConfigField> ActionConfigFields =>
    [
        new ConfigField { Key = KeyKey, Label = "Key", Type = ConfigFieldType.String },
        new ConfigField { Key = CtrlKey, Label = "Ctrl", Type = ConfigFieldType.Boolean, DefaultValue = false },
        new ConfigField { Key = AltKey, Label = "Alt", Type = ConfigFieldType.Boolean, DefaultValue = false },
        new ConfigField { Key = ShiftKey, Label = "Shift", Type = ConfigFieldType.Boolean, DefaultValue = false },
        new ConfigField { Key = WinKey, Label = "Win", Type = ConfigFieldType.Boolean, DefaultValue = false },
    ];

    protected override ActionResult Perform(IInputSender sender, IntPtr windowHandle, ActionExecutionContext context)
    {
        var keyName = ConfigValues.GetString(context.Action.Config, KeyKey);
        if (!VirtualKeys.TryResolve(keyName, out var vk))
        {
            return ActionResult.Fail($"Key Press: unrecognized key '{keyName}'.");
        }

        var modifiers = KeyModifiers.None;
        if (ConfigValues.GetBool(context.Action.Config, CtrlKey)) modifiers |= KeyModifiers.Control;
        if (ConfigValues.GetBool(context.Action.Config, AltKey)) modifiers |= KeyModifiers.Alt;
        if (ConfigValues.GetBool(context.Action.Config, ShiftKey)) modifiers |= KeyModifiers.Shift;
        if (ConfigValues.GetBool(context.Action.Config, WinKey)) modifiers |= KeyModifiers.Win;

        sender.KeyPress(windowHandle, vk, modifiers);
        return ActionResult.Ok(SuccessPort);
    }
}
```

- [ ] **Step 5: Run to verify pass** — `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Actions.BuiltIn.KeyboardActionsTests"` → PASS (8 cases: 3 TypeText + 3 KeyPress + 2 metadata-theory + 1 config-order = 9 cases actually). Then `dotnet test ADB.slnx` + `dotnet build ADB.slnx` (0 warnings).

- [ ] **Step 6: Commit**

```bash
git add AdbCore/Actions/BuiltIn/TypeTextAction.cs AdbCore/Actions/BuiltIn/KeyPressAction.cs AdbCore.Tests/Actions/BuiltIn/KeyboardActionsTests.cs
git commit -m "feat(actions): add Type Text and Key Press input actions"
```

---

## Task 5: Register the actions, update counts, gate

**Files:**
- Modify: `AdbCore/Actions/BuiltIn/BuiltInActions.cs`
- Modify: `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs`
- Modify: `BotBuilder.Core.Tests/PaletteViewModelTests.cs`
- Modify: `Docs/Specs/2026-06-01-m5-built-in-actions-design.md`

After this: definitions = 16, executors = 13; Input palette = 6.

- [ ] **Step 1: Update the registration assertions (failing first)**

In `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs`, in `Register_AddsAllBuiltInsToBothRegistries`, add the two new keys to the dual-registry list and bump the counts. The dual list becomes:

```csharp
        foreach (var key in new[]
        {
            "control.start", "control.end", "data.log", "control.delay", "control.branch",
            "data.setVariable", "data.comment",
            "input.click", "input.rightClick", "input.doubleClick", "input.mouseMove",
            "input.typeText", "input.keyPress",
        })
        {
            Assert.True(defs.TryGet(key, out _));
            Assert.True(execs.TryGet(key, out _));
        }
```

and the counts become:

```csharp
        Assert.Equal(16, defs.Count);
        Assert.Equal(13, execs.Count);
```

(Leave the engine-native def-only loop unchanged.)

- [ ] **Step 2: Update the palette counts** in `BotBuilder.Core.Tests/PaletteViewModelTests.cs`:
- `Categories_GroupBuiltInsByCategory`: change the Input-category count to:
```csharp
        Assert.Equal(6, input.Items.Count); // Click, Right Click, Double Click, Mouse Move, Type Text, Key Press
```
- `ClearingSearch_RestoresAll`: change the total to:
```csharp
        Assert.Equal(16, palette.Categories.SelectMany(c => c.Items).Count()); // 7 Control Flow + 3 Data + 6 Input
```

- [ ] **Step 3: Run those tests to verify they FAIL** — `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Actions.BuiltIn.BuiltInActionsTests|FullyQualifiedName~BotBuilder.Core.Tests.PaletteViewModelTests"` → FAIL.

- [ ] **Step 4: Register the actions** — in `AdbCore/Actions/BuiltIn/BuiltInActions.cs`, after the four pointer-action registrations (`Add(new MouseMoveAction(inputSenders), ...)`), add:

```csharp
        Add(new TypeTextAction(inputSenders), definitions, executors);
        Add(new KeyPressAction(inputSenders), definitions, executors);
```

- [ ] **Step 5: Run the full suite** — `dotnet test ADB.slnx` → ALL green. If any other test asserts a built-in count/category, update it for the two added Input actions.

- [ ] **Step 6: Zero-warning build gate** — `dotnet build ADB.slnx` → 0 Warning(s), 0 Error(s).

- [ ] **Step 7: Update the spec status line** — in `Docs/Specs/2026-06-01-m5-built-in-actions-design.md`, set the status line to exactly:

```
**Status:** Approved — M5a1 + M5a2 + M5b + M5c (engine + Data + full Input) implemented
```

- [ ] **Step 8: Commit**

```bash
git add AdbCore/Actions/BuiltIn/BuiltInActions.cs AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs BotBuilder.Core.Tests/PaletteViewModelTests.cs Docs/Specs/2026-06-01-m5-built-in-actions-design.md
git commit -m "feat(actions): register Type Text and Key Press"
```

---

## Manual Verification Checklist (for the user — the Win32 keyboard adapters run live)

After merge, against Notepad (default method SendInput), focused:
- [ ] Palette **Input** now has Type Text and Key Press (Key Press shows Key + Ctrl/Alt/Shift/Win checkboxes).
- [ ] **Type Text** "hello world" → the text appears in Notepad.
- [ ] **Key Press** Key=`A` → an `a` appears (Shift checkbox → `A`).
- [ ] **Key Press** Key=`Enter` → a newline.
- [ ] **Key Press** Key=`A`, Ctrl=on → selects all (Ctrl+A). Then Key Press Key=`Delete` clears it.
- [ ] Key Press with an unknown Key (e.g. "Splat") → run fails with "unrecognized key" (exit 1, action `success:false`).

> SendInput is the reliable path; PostMessage modifiers (chords) are unreliable as noted (GetKeyState isn't updated by posted messages).

---

## Self-Review (completed by plan author)

**Spec coverage (design §4.2 Input, keyboard subset):** Type Text — Task 4 (`TypeTextAction` → `IInputSender.TypeText`). Key Press with modifiers — Task 4 (`KeyPressAction` → `VirtualKeys` + `KeyModifiers` → `IInputSender.KeyPress`). Adapters extended (Task 2: SendInput INPUT-union + Unicode typing + modifier sequences; PostMessage WM_CHAR/WM_KEYDOWN-UP). Key mapping (Task 1). Shared HWND/method plumbing via `InputActionBase` (Task 3). Registration/palette (Task 5). This completes the full Input category (6 actions). ✓

**Placeholder scan:** none — complete code/commands. Win32 adapters build-only by design, verified via the manual checklist.

**Type consistency:** `IInputSender.{TypeText,KeyPress}`, `KeyModifiers.{None,Control,Alt,Shift,Win}`, `VirtualKeys.TryResolve`, `InputActionBase.{MethodKey,SuccessPort,FailurePort,ActionConfigFields,Perform}`, `PointerActionBase.{XKey,YKey,Dispatch}`, `TypeTextAction.TextKey`, `KeyPressAction.{KeyKey,CtrlKey,AltKey,ShiftKey,WinKey}` consistent across tasks. ClickAction/mouse tests reference `PointerActionBase.XKey/YKey` (stay on PointerActionBase) and `MethodKey` (inherited from InputActionBase) — all resolve. KeyPress ConfigFields order = [key, ctrl, alt, shift, win, method]. Counts: 16 defs / 13 execs; palette 6 Input / 16 total.

**Out of scope:** the activate-window action (separate future convo, [[adb-activate-window-action-idea]]); M5d Screen (OpenCvSharp) is the next milestone slice after M5c.
