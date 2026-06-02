# M5c2a — Mouse Input Actions (Right Click, Double Click, Mouse Move) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the remaining *mouse* Input actions — **Right Click**, **Double Click**, **Mouse Move** — reusing the M5c1 window-targeted, per-node-method pipeline, and factor the shared logic out of `ClickAction` into a `PointerActionBase`.

**Architecture:** Extend `IInputSender` with `RightClick`/`DoubleClick`/`MoveTo` (both Win32 adapters implement them). Introduce an abstract `PointerActionBase` holding the common HWND resolution, X/Y/method config fields, ports, and `ExecuteAsync` template; the four pointer actions (Click + 3 new) become thin subclasses that only name themselves and pick the sender call. Keyboard actions (Type Text, Key Press) are the separate **M5c2b** slice.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`, Win32 P/Invoke), xUnit. Projects: `AdbCore`, `AdbCore.Tests`. Solution: `ADB.slnx`.

**Design reference:** `Docs/Specs/2026-06-01-m5-built-in-actions-design.md` §4.2 (Input). Builds directly on M5c1 (merged): `IInputSender`, `Win32SendInputSender`, `Win32PostMessageSender`, `InputSenderResolver`, `ClickAction`.

---

## Background the implementer needs

- **M5c1 pipeline (in `main`):** `ClickAction` takes an `InputSenderResolver`, reads `x`/`y`/`method` config, resolves the target HWND from `Context.Targets` (explicit `TargetId`, or the sole target if unset), and calls `sender.Click(hwnd, x, y)`. The method config field (Enum, default `SendInput`) selects `Win32SendInputSender` (foreground, reliable) vs `Win32PostMessageSender` (background). `IInputSender` currently has only `Click(IntPtr, int, int)`.
- **The four pointer actions share everything except their name and which sender method they call** — that's the `PointerActionBase` refactor.
- **`ConfigValues.GetInt/GetString`** read config. `ActionResult.Ok("onSuccess")` / `ActionResult.Fail(msg)`.
- **Win32 adapters are build-only (no unit tests)** — verified by the manual checklist. The pure logic (base class, subclass dispatch, method selection) IS unit-tested via a recording fake sender.
- **Current registry counts:** 11 definitions / 8 executors; Input palette category has 1 item (Click). After M5c2a: **14 / 11**, Input category = 4.
- Strict TDD for pure logic.

## Build / test commands (from the worktree root)

- Single class: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Actions.BuiltIn.MouseActionsTests"`
- Full suite: `dotnet test ADB.slnx`
- Zero-warning build (hard gate): `dotnet build ADB.slnx`

---

## File Structure

- **Modify** `AdbCore/Input/IInputSender.cs` — add `RightClick`, `DoubleClick`, `MoveTo`.
- **Modify** `AdbCore/Input/Win32SendInputSender.cs` — implement the three (refactor a shared cursor-positioning + event-injection helper).
- **Modify** `AdbCore/Input/Win32PostMessageSender.cs` — implement the three (R-button, dbl-click, mouse-move messages).
- **Create** `AdbCore/Actions/BuiltIn/PointerActionBase.cs` — abstract base for the four pointer actions.
- **Modify** `AdbCore/Actions/BuiltIn/ClickAction.cs` — reduce to a `PointerActionBase` subclass.
- **Create** `AdbCore/Actions/BuiltIn/RightClickAction.cs`, `DoubleClickAction.cs`, `MouseMoveAction.cs`.
- **Modify** `AdbCore/Actions/BuiltIn/BuiltInActions.cs` — register the three new actions (share one resolver).
- **Create** `AdbCore.Tests/Input/RecordingInputSender.cs` — shared test double recording the operation + args.
- **Create** `AdbCore.Tests/Actions/BuiltIn/MouseActionsTests.cs`.
- **Modify** `AdbCore.Tests/Actions/BuiltIn/ClickActionTests.cs` — its private fake must implement the grown interface (Task 1).
- **Modify** `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs`, `BotBuilder.Core.Tests/PaletteViewModelTests.cs` — counts.

---

## Task 1: Extend `IInputSender` + both Win32 adapters

Additive interface growth + adapter implementations (build-only) + keep existing tests compiling.

**Files:**
- Modify: `AdbCore/Input/IInputSender.cs`
- Modify: `AdbCore/Input/Win32SendInputSender.cs`
- Modify: `AdbCore/Input/Win32PostMessageSender.cs`
- Modify: `AdbCore.Tests/Actions/BuiltIn/ClickActionTests.cs` (its private `FakeInputSender` must implement the new methods)

- [ ] **Step 1: Extend the interface** — replace `AdbCore/Input/IInputSender.cs` with:

```csharp
namespace AdbCore.Input;

/// <summary>Sends synthetic input to a target window, addressed by its HWND with client-relative
/// coordinates. Implementations choose the delivery mechanism (foreground SendInput, or background
/// PostMessage) — see the concrete senders for their trade-offs.</summary>
public interface IInputSender
{
    /// <summary>Delivers a left click at the given client-relative coordinates of <paramref name="windowHandle"/>.</summary>
    void Click(IntPtr windowHandle, int clientX, int clientY);

    /// <summary>Delivers a right click at the given client-relative coordinates.</summary>
    void RightClick(IntPtr windowHandle, int clientX, int clientY);

    /// <summary>Delivers a left double-click at the given client-relative coordinates.</summary>
    void DoubleClick(IntPtr windowHandle, int clientX, int clientY);

    /// <summary>Moves the pointer to the given client-relative coordinates (no button press).</summary>
    void MoveTo(IntPtr windowHandle, int clientX, int clientY);
}
```

- [ ] **Step 2: Implement them in the SendInput adapter** — replace `AdbCore/Input/Win32SendInputSender.cs` with:

```csharp
using System.Runtime.InteropServices;

namespace AdbCore.Input;

/// <summary>Foreground <see cref="IInputSender"/>: brings the target window to the front, moves the cursor
/// to the window-relative point (converted to screen coordinates), and injects real OS mouse input via
/// SendInput. Reliable across modern apps, but foreground-only — it moves the real cursor and drives one
/// window at a time.</summary>
public sealed class Win32SendInputSender : IInputSender
{
    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;

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

    // INPUT is a union of mouse/keyboard/hardware input. MOUSEINPUT is the largest relevant member,
    // so this layout marshals to the correct sizeof(INPUT) on x64 for mouse events.
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
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
        InjectMouse(MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);
    }

    public void RightClick(IntPtr windowHandle, int clientX, int clientY)
    {
        PositionCursor(windowHandle, clientX, clientY);
        InjectMouse(MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP);
    }

    public void DoubleClick(IntPtr windowHandle, int clientX, int clientY)
    {
        PositionCursor(windowHandle, clientX, clientY);
        InjectMouse(MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP, MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);
    }

    public void MoveTo(IntPtr windowHandle, int clientX, int clientY)
        => PositionCursor(windowHandle, clientX, clientY);

    private static void PositionCursor(IntPtr windowHandle, int clientX, int clientY)
    {
        SetForegroundWindow(windowHandle);
        var point = new POINT { X = clientX, Y = clientY };
        ClientToScreen(windowHandle, ref point);
        SetCursorPos(point.X, point.Y);
    }

    private static void InjectMouse(params uint[] flags)
    {
        var inputs = new INPUT[flags.Length];
        for (var i = 0; i < flags.Length; i++)
        {
            inputs[i] = new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = flags[i] } };
        }

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }
}
```

- [ ] **Step 3: Implement them in the PostMessage adapter** — replace `AdbCore/Input/Win32PostMessageSender.cs` with:

```csharp
using System.Runtime.InteropServices;

namespace AdbCore.Input;

/// <summary>PostMessage implementation of <see cref="IInputSender"/>: posts messages so a window need not
/// be foreground. Coordinates are client-relative and packed into the message lParam. Note: some apps and
/// most games ignore synthesized messages — for those, the foreground SendInput sender is needed.</summary>
public sealed class Win32PostMessageSender : IInputSender
{
    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONDOWN = 0x0204;
    private const uint WM_RBUTTONUP = 0x0205;
    private const int MK_LBUTTON = 0x0001;
    private const int MK_RBUTTON = 0x0002;

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

    // Cast through uint so a y >= 32768 does not sign-extend into the high 32 bits of the IntPtr
    // (matches the Win32 MAKELPARAM macro's unsigned semantics).
    private static IntPtr MakeLParam(int x, int y)
        => (IntPtr)(uint)((y << 16) | (x & 0xFFFF));
}
```

- [ ] **Step 4: Keep `ClickActionTests` compiling** — in `AdbCore.Tests/Actions/BuiltIn/ClickActionTests.cs`, the private `FakeInputSender` currently implements only `Click`. Add the three new interface methods to it (no-op is fine — these tests only assert on `Click`). Insert into the `FakeInputSender` class body, after the existing `Click` method:

```csharp
        public void RightClick(IntPtr windowHandle, int clientX, int clientY) { }

        public void DoubleClick(IntPtr windowHandle, int clientX, int clientY) { }

        public void MoveTo(IntPtr windowHandle, int clientX, int clientY) { }
```

- [ ] **Step 5: Build (0 warnings) and run the full suite**

Run: `dotnet build ADB.slnx` → Build succeeded, **0 Warning(s), 0 Error(s)**.
Run: `dotnet test ADB.slnx` → all green (the adapters have no unit tests; ClickActionTests still pass).

- [ ] **Step 6: Commit**

```bash
git add AdbCore/Input/IInputSender.cs AdbCore/Input/Win32SendInputSender.cs AdbCore/Input/Win32PostMessageSender.cs AdbCore.Tests/Actions/BuiltIn/ClickActionTests.cs
git commit -m "feat(core): extend IInputSender with RightClick/DoubleClick/MoveTo + Win32 adapters"
```

---

## Task 2: `PointerActionBase` + refactor `ClickAction` onto it

Behavior-preserving refactor. The existing `ClickActionTests` must stay green (they reference `ClickAction.XKey`/`MethodKey`/etc., which remain accessible as inherited consts).

**Files:**
- Create: `AdbCore/Actions/BuiltIn/PointerActionBase.cs`
- Modify: `AdbCore/Actions/BuiltIn/ClickAction.cs`

- [ ] **Step 1: Create the base** — `AdbCore/Actions/BuiltIn/PointerActionBase.cs`:

```csharp
using AdbCore.Execution;
using AdbCore.Input;
using AdbCore.Models;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Shared base for Input pointer actions (Click / Right Click / Double Click / Mouse Move):
/// resolves the target window HWND, reads X/Y and the input method, and dispatches to the chosen
/// <see cref="IInputSender"/>. Subclasses only name themselves and pick which sender call to make.</summary>
public abstract class PointerActionBase : IActionDefinition, IActionExecutor
{
    public const string XKey = "x";
    public const string YKey = "y";
    public const string MethodKey = "method";
    public const string SuccessPort = "onSuccess";
    public const string FailurePort = "onFailure";

    private readonly InputSenderResolver _senders;

    protected PointerActionBase(InputSenderResolver senders)
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
    public List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = XKey, Label = "X", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField { Key = YKey, Label = "Y", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField
        {
            Key = MethodKey,
            Label = "Input Method",
            Type = ConfigFieldType.Enum,
            DefaultValue = InputSenderResolver.SendInputMethod,
            Options = new() { InputSenderResolver.SendInputMethod, InputSenderResolver.PostMessageMethod },
        },
    };
    public bool SupportsRetry => false;

    /// <summary>Dispatches the specific pointer operation (click/right-click/double-click/move) to the sender.</summary>
    protected abstract void Dispatch(IInputSender sender, IntPtr windowHandle, int x, int y);

    public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        if (ResolveWindow(context) is not IntPtr hwnd || hwnd == IntPtr.Zero)
        {
            return Task.FromResult(ActionResult.Fail($"{DisplayName} requires a resolved Window target (HWND)."));
        }

        var x = ConfigValues.GetInt(context.Action.Config, XKey);
        var y = ConfigValues.GetInt(context.Action.Config, YKey);
        var method = ConfigValues.GetString(context.Action.Config, MethodKey, InputSenderResolver.SendInputMethod);
        Dispatch(_senders.Resolve(method), hwnd, x, y);

        return Task.FromResult(ActionResult.Ok(SuccessPort));
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

- [ ] **Step 2: Reduce `ClickAction` to a subclass** — replace `AdbCore/Actions/BuiltIn/ClickAction.cs` with:

```csharp
using AdbCore.Input;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Performs a left click at client-relative coordinates of the action's Window target.</summary>
public sealed class ClickAction : PointerActionBase
{
    public ClickAction(InputSenderResolver senders) : base(senders)
    {
    }

    public override string TypeKey => "input.click";
    public override string DisplayName => "Click";
    public override string Description => "Clicks at coordinates within the target window.";

    protected override void Dispatch(IInputSender sender, IntPtr windowHandle, int x, int y)
        => sender.Click(windowHandle, x, y);
}
```

- [ ] **Step 3: Run ClickAction tests + full suite to confirm behavior preserved**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Actions.BuiltIn.ClickActionTests"` → PASS (9 tests, unchanged).
Run: `dotnet test ADB.slnx` → all green.
Run: `dotnet build ADB.slnx` → 0 warnings.

- [ ] **Step 4: Commit**

```bash
git add AdbCore/Actions/BuiltIn/PointerActionBase.cs AdbCore/Actions/BuiltIn/ClickAction.cs
git commit -m "refactor(actions): extract PointerActionBase; ClickAction becomes a subclass"
```

---

## Task 3: Right Click, Double Click, Mouse Move actions

**Files:**
- Create: `AdbCore/Actions/BuiltIn/RightClickAction.cs`, `DoubleClickAction.cs`, `MouseMoveAction.cs`
- Create: `AdbCore.Tests/Input/RecordingInputSender.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/MouseActionsTests.cs`

- [ ] **Step 1: Write the failing tests** — first the shared recording fake, `AdbCore.Tests/Input/RecordingInputSender.cs`:

```csharp
using AdbCore.Input;

namespace AdbCore.Tests.Input;

/// <summary>Test double that records the last pointer operation and its arguments.</summary>
internal sealed class RecordingInputSender : IInputSender
{
    public string? LastOp { get; private set; }
    public IntPtr LastWindow { get; private set; }
    public int LastX { get; private set; }
    public int LastY { get; private set; }
    public int Calls { get; private set; }

    public void Click(IntPtr windowHandle, int clientX, int clientY) => Record("Click", windowHandle, clientX, clientY);
    public void RightClick(IntPtr windowHandle, int clientX, int clientY) => Record("RightClick", windowHandle, clientX, clientY);
    public void DoubleClick(IntPtr windowHandle, int clientX, int clientY) => Record("DoubleClick", windowHandle, clientX, clientY);
    public void MoveTo(IntPtr windowHandle, int clientX, int clientY) => Record("MoveTo", windowHandle, clientX, clientY);

    private void Record(string op, IntPtr windowHandle, int x, int y)
    {
        LastOp = op;
        LastWindow = windowHandle;
        LastX = x;
        LastY = y;
        Calls++;
    }
}
```

Then `AdbCore.Tests/Actions/BuiltIn/MouseActionsTests.cs`:

```csharp
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Input;
using AdbCore.Models;
using AdbCore.Tests.Input;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class MouseActionsTests
{
    private sealed class Senders
    {
        public RecordingInputSender SendInput { get; } = new();
        public RecordingInputSender PostMessage { get; } = new();
        public InputSenderResolver Resolver() => new(SendInput, PostMessage);
    }

    private static (BotAction action, BotExecutionContext ctx) Setup(IntPtr handle, int x = 30, int y = 40, string? method = null)
    {
        var id = Guid.NewGuid();
        var ctx = new BotExecutionContext();
        ctx.Targets[id] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "hwnd:1", Handle = handle };
        var action = new BotAction { TargetId = id };
        action.Config[PointerActionBase.XKey] = x;
        action.Config[PointerActionBase.YKey] = y;
        if (method is not null)
        {
            action.Config[PointerActionBase.MethodKey] = method;
        }

        return (action, ctx);
    }

    private static ActionExecutionContext Exec(BotAction action, BotExecutionContext ctx) => new(action, ctx, _ => { });

    [Fact]
    public async Task RightClick_DispatchesRightClick_ViaSendInput_Default()
    {
        var senders = new Senders();
        var (action, ctx) = Setup((IntPtr)11);

        var result = await new RightClickAction(senders.Resolver()).ExecuteAsync(Exec(action, ctx), default);

        Assert.True(result.Success);
        Assert.Equal("onSuccess", result.OutputPort);
        Assert.Equal("RightClick", senders.SendInput.LastOp);
        Assert.Equal((IntPtr)11, senders.SendInput.LastWindow);
        Assert.Equal(30, senders.SendInput.LastX);
        Assert.Equal(40, senders.SendInput.LastY);
        Assert.Equal(0, senders.PostMessage.Calls);
    }

    [Fact]
    public async Task DoubleClick_DispatchesDoubleClick()
    {
        var senders = new Senders();
        var (action, ctx) = Setup((IntPtr)12);

        await new DoubleClickAction(senders.Resolver()).ExecuteAsync(Exec(action, ctx), default);

        Assert.Equal("DoubleClick", senders.SendInput.LastOp);
        Assert.Equal((IntPtr)12, senders.SendInput.LastWindow);
    }

    [Fact]
    public async Task MouseMove_DispatchesMoveTo()
    {
        var senders = new Senders();
        var (action, ctx) = Setup((IntPtr)13, x: 5, y: 6);

        await new MouseMoveAction(senders.Resolver()).ExecuteAsync(Exec(action, ctx), default);

        Assert.Equal("MoveTo", senders.SendInput.LastOp);
        Assert.Equal(5, senders.SendInput.LastX);
        Assert.Equal(6, senders.SendInput.LastY);
    }

    [Fact]
    public async Task PointerAction_PostMessageMethod_RoutesToPostMessageSender()
    {
        var senders = new Senders();
        var (action, ctx) = Setup((IntPtr)14, method: InputSenderResolver.PostMessageMethod);

        await new RightClickAction(senders.Resolver()).ExecuteAsync(Exec(action, ctx), default);

        Assert.Equal("RightClick", senders.PostMessage.LastOp);
        Assert.Equal(0, senders.SendInput.Calls);
    }

    [Fact]
    public async Task PointerAction_NoResolvedTarget_FailsWithoutSending()
    {
        var senders = new Senders();
        var action = new BotAction { TargetId = null };

        var result = await new MouseMoveAction(senders.Resolver()).ExecuteAsync(
            Exec(action, new BotExecutionContext()), default);

        Assert.False(result.Success);
        Assert.Equal(0, senders.SendInput.Calls);
        Assert.Equal(0, senders.PostMessage.Calls);
        Assert.Contains("Window target", result.ErrorMessage);
    }

    [Theory]
    [InlineData(typeof(RightClickAction), "input.rightClick", "Right Click")]
    [InlineData(typeof(DoubleClickAction), "input.doubleClick", "Double Click")]
    [InlineData(typeof(MouseMoveAction), "input.mouseMove", "Mouse Move")]
    public void Definition_Metadata(Type actionType, string expectedTypeKey, string expectedDisplayName)
    {
        var def = (IActionDefinition)Activator.CreateInstance(actionType, new Senders().Resolver())!;

        Assert.Equal(expectedTypeKey, def.TypeKey);
        Assert.Equal(expectedDisplayName, def.DisplayName);
        Assert.Equal("Input", def.Category);
        Assert.Equal(new[] { "in" }, def.InputPorts.Select(p => p.Name));
        Assert.Equal(new[] { "onSuccess", "onFailure" }, def.OutputPorts.Select(p => p.Name));
        Assert.Equal(new[] { PointerActionBase.XKey, PointerActionBase.YKey, PointerActionBase.MethodKey }, def.ConfigFields.Select(f => f.Key));
        Assert.False(def.SupportsRetry);
    }
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Actions.BuiltIn.MouseActionsTests"` → FAIL to compile (the three action types don't exist).

- [ ] **Step 3: Create the three actions.**

`AdbCore/Actions/BuiltIn/RightClickAction.cs`:

```csharp
using AdbCore.Input;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Performs a right click at client-relative coordinates of the action's Window target.</summary>
public sealed class RightClickAction : PointerActionBase
{
    public RightClickAction(InputSenderResolver senders) : base(senders)
    {
    }

    public override string TypeKey => "input.rightClick";
    public override string DisplayName => "Right Click";
    public override string Description => "Right-clicks at coordinates within the target window.";

    protected override void Dispatch(IInputSender sender, IntPtr windowHandle, int x, int y)
        => sender.RightClick(windowHandle, x, y);
}
```

`AdbCore/Actions/BuiltIn/DoubleClickAction.cs`:

```csharp
using AdbCore.Input;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Performs a left double-click at client-relative coordinates of the action's Window target.</summary>
public sealed class DoubleClickAction : PointerActionBase
{
    public DoubleClickAction(InputSenderResolver senders) : base(senders)
    {
    }

    public override string TypeKey => "input.doubleClick";
    public override string DisplayName => "Double Click";
    public override string Description => "Double-clicks at coordinates within the target window.";

    protected override void Dispatch(IInputSender sender, IntPtr windowHandle, int x, int y)
        => sender.DoubleClick(windowHandle, x, y);
}
```

`AdbCore/Actions/BuiltIn/MouseMoveAction.cs`:

```csharp
using AdbCore.Input;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Moves the pointer to client-relative coordinates of the action's Window target (no click).</summary>
public sealed class MouseMoveAction : PointerActionBase
{
    public MouseMoveAction(InputSenderResolver senders) : base(senders)
    {
    }

    public override string TypeKey => "input.mouseMove";
    public override string DisplayName => "Mouse Move";
    public override string Description => "Moves the pointer to coordinates within the target window.";

    protected override void Dispatch(IInputSender sender, IntPtr windowHandle, int x, int y)
        => sender.MoveTo(windowHandle, x, y);
}
```

- [ ] **Step 4: Run to verify pass** — `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Actions.BuiltIn.MouseActionsTests"` → PASS (7 tests: 3 dispatch + 1 method-routing + 1 no-target + 2 metadata theory cases... note the `[Theory]` counts as 3 cases, so 8 total test cases).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Actions/BuiltIn/RightClickAction.cs AdbCore/Actions/BuiltIn/DoubleClickAction.cs AdbCore/Actions/BuiltIn/MouseMoveAction.cs AdbCore.Tests/Input/RecordingInputSender.cs AdbCore.Tests/Actions/BuiltIn/MouseActionsTests.cs
git commit -m "feat(actions): add Right Click, Double Click, Mouse Move input actions"
```

---

## Task 4: Register the actions, update counts, gate

**Files:**
- Modify: `AdbCore/Actions/BuiltIn/BuiltInActions.cs`
- Modify: `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs`
- Modify: `BotBuilder.Core.Tests/PaletteViewModelTests.cs`
- Modify: `Docs/Specs/2026-06-01-m5-built-in-actions-design.md`

After this: definitions = 14, executors = 11; Input palette category = 4.

- [ ] **Step 1: Update the registration assertions (failing first)**

In `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs`, in `Register_AddsAllBuiltInsToBothRegistries`, change the dual-registry key list to add the three new keys and update the counts. Replace:

```csharp
        foreach (var key in new[] { "control.start", "control.end", "data.log", "control.delay", "control.branch", "data.setVariable", "data.comment", "input.click" })
        {
            Assert.True(defs.TryGet(key, out _));
            Assert.True(execs.TryGet(key, out _));
        }

        // Engine-native nodes: definitions only, no executors.
        foreach (var key in new[] { "control.loop", "control.runParallel", "control.join" })
        {
            Assert.True(defs.TryGet(key, out _));
            Assert.False(execs.TryGet(key, out _));
        }

        Assert.Equal(11, defs.Count);
        Assert.Equal(8, execs.Count);
```

with:

```csharp
        foreach (var key in new[]
        {
            "control.start", "control.end", "data.log", "control.delay", "control.branch",
            "data.setVariable", "data.comment",
            "input.click", "input.rightClick", "input.doubleClick", "input.mouseMove",
        })
        {
            Assert.True(defs.TryGet(key, out _));
            Assert.True(execs.TryGet(key, out _));
        }

        // Engine-native nodes: definitions only, no executors.
        foreach (var key in new[] { "control.loop", "control.runParallel", "control.join" })
        {
            Assert.True(defs.TryGet(key, out _));
            Assert.False(execs.TryGet(key, out _));
        }

        Assert.Equal(14, defs.Count);
        Assert.Equal(11, execs.Count);
```

- [ ] **Step 2: Update the palette counts**

In `BotBuilder.Core.Tests/PaletteViewModelTests.cs`:
- In `Categories_GroupBuiltInsByCategory`, change the Input-category assertion `Assert.Single(input.Items);` to:

```csharp
        Assert.Equal(4, input.Items.Count); // Click, Right Click, Double Click, Mouse Move
```

- In `ClearingSearch_RestoresAll`, change the total to:

```csharp
        Assert.Equal(14, palette.Categories.SelectMany(c => c.Items).Count()); // 7 Control Flow + 3 Data + 4 Input
```

- [ ] **Step 3: Run those tests to verify they FAIL**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Actions.BuiltIn.BuiltInActionsTests|FullyQualifiedName~BotBuilder.Core.Tests.PaletteViewModelTests"`
Expected: FAIL (new actions not registered).

- [ ] **Step 4: Register the actions** — in `AdbCore/Actions/BuiltIn/BuiltInActions.cs`, replace the single Click registration line:

```csharp
        // Input actions need an IInputSender; the real app uses the Win32 (PostMessage) implementation.
        Add(new ClickAction(new InputSenderResolver(new Win32SendInputSender(), new Win32PostMessageSender())), definitions, executors);
```

with (one shared resolver for all pointer actions):

```csharp
        // Input actions share one resolver: SendInput (foreground, default) + PostMessage (background, opt-in per node).
        var inputSenders = new InputSenderResolver(new Win32SendInputSender(), new Win32PostMessageSender());
        Add(new ClickAction(inputSenders), definitions, executors);
        Add(new RightClickAction(inputSenders), definitions, executors);
        Add(new DoubleClickAction(inputSenders), definitions, executors);
        Add(new MouseMoveAction(inputSenders), definitions, executors);
```

- [ ] **Step 5: Run the full suite**

Run: `dotnet test ADB.slnx` → ALL green. If any other test asserts a built-in count, update it for the three added Input actions.

- [ ] **Step 6: Zero-warning build gate**

Run: `dotnet build ADB.slnx` → Build succeeded, **0 Warning(s), 0 Error(s)**.

- [ ] **Step 7: Update the spec status line**

In `Docs/Specs/2026-06-01-m5-built-in-actions-design.md`, change the status line to exactly:

```
**Status:** Approved — M5a1 + M5a2 + M5b + M5c1 + M5c2a (engine + Data + Input click/mouse) implemented
```

- [ ] **Step 8: Commit**

```bash
git add AdbCore/Actions/BuiltIn/BuiltInActions.cs AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs BotBuilder.Core.Tests/PaletteViewModelTests.cs Docs/Specs/2026-06-01-m5-built-in-actions-design.md
git commit -m "feat(actions): register Right Click, Double Click, Mouse Move"
```

---

## Manual Verification Checklist (for the user — the Win32 adapters run live)

After merge, against a standard window (e.g. Notepad), with default method (SendInput):
- [ ] Palette **Input** category now has Click, Right Click, Double Click, Mouse Move.
- [ ] **Right Click** at a text position → the context menu appears.
- [ ] **Double Click** on a word → the word is selected.
- [ ] **Mouse Move** to an X/Y → the cursor moves there (no click).
- [ ] Each shows the Input Method field (SendInput default / PostMessage) in the Properties Panel.

> PostMessage caveat from M5c1 still applies (ignored by modern apps/games); SendInput (default) is the reliable path.

---

## Self-Review (completed by plan author)

**Spec coverage (design §4.2 Input, mouse subset):** Right Click — Task 3 (`RightClickAction`). Double Click — Task 3. Mouse Move — Task 3. Shared pipeline/method-selection reused from M5c1 via `PointerActionBase` (Task 2). Adapters extended (Task 1). Registration/palette (Task 4). Keyboard (Type Text / Key Press) = M5c2b, out of scope. ✓

**Placeholder scan:** none — complete code/commands throughout. Adapter tasks are build-only by design (OS code), verified via the manual checklist.

**Type consistency:** `IInputSender.{Click,RightClick,DoubleClick,MoveTo}`, `PointerActionBase.{XKey,YKey,MethodKey,SuccessPort,FailurePort,Dispatch}`, the four subclasses, `InputSenderResolver`, and `RecordingInputSender` are consistent across tasks. `ClickAction.XKey` references in the existing ClickActionTests still resolve as inherited consts after the Task 2 refactor. Counts: 14 defs / 11 execs; palette 4 Input / 14 total.

**Out of scope:** keyboard input (M5c2b: Type Text, Key Press — needs KEYBDINPUT union + key-name→VK mapping + the combos design question); the activate-window action (separate future convo).
