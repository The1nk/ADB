# M5c1 — Input Infrastructure + Click Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the Win32 input pipeline — window-target HWND resolution + an `IInputSender` abstraction — and prove it end-to-end with one action (**Click**) that posts a click to a target window at client-relative coordinates.

**Architecture:** Window-targeted, HWND-relative input via `PostMessage` (the multi-client/background path chosen during M5 scoping), kept behind interfaces so the OS-touching code is a thin, swappable adapter. Pure logic is TDD'd (selector parsing, the Click executor against a fake `IInputSender`, the HWND-binding step against a fake `IWindowResolver`); two small Win32 adapters (`Win32InputSender`, `Win32WindowResolver`) are build-only and validated by a manual checklist.

**Tech Stack:** C# / .NET 10 (`net10.0-windows` — Win32 P/Invoke to `user32.dll` is available; no extra packages). xUnit. Projects: `AdbCore`, `AdbCore.Tests`, `BotRunner`, `BotRunner.Tests`. Solution: `ADB.slnx`.

**Design reference:** `Docs/Specs/2026-06-01-m5-built-in-actions-design.md` §4.2 (Input, window-targeted HWND-relative), §5 (target resolution / HWND pulled forward), slice M5c in §8. This is the **c1** sub-slice (infra + Click); remaining Input actions are **c2**.

---

## Background the implementer needs

- **Leaf-action pattern** (`AdbCore/Actions/BuiltIn/LogAction.cs`, `SetVariableAction.cs`): one `sealed` class implements `IActionDefinition` (TypeKey/DisplayName/Category/Description/InputPorts/OutputPorts/ConfigFields/SupportsRetry) + `IActionExecutor` (`Task<ActionResult> ExecuteAsync(ActionExecutionContext, CancellationToken)`). **Click differs in one way: it takes an `IInputSender` constructor dependency** (no parameterless default), so it cannot use the `BuiltInActions.Add<T>` helper blindly — registration constructs it explicitly (Task 5).
- **Targets:** `TargetResolver.Resolve(bot, selectors)` (BotRunner, pure) produces `Dictionary<Guid, ResolvedTarget>` where `ResolvedTarget { BotTargetType Type; string Selector; object? Handle }` (Handle currently always null). These flow via `ExecutionOptions.ResolvedTargets` into `BotExecutionContext.Targets` (keyed by `BotTarget.Id`). An executor reaches its target via `context.Context.Targets[...]`. `BotTargetType` enum: `Window, AndroidDevice, Browser`.
- **`BotAction.TargetId`** is `Guid?` — null means "the (single) default target".
- **`ConfigValues.GetInt(IReadOnlyDictionary<string,object>, key, fallback=0)`** reads numeric config (handles boxed + JsonElement). Accessible from `BuiltIn` without a using.
- **CLI target flow (already works, M2):** `BotRunner.exe --bot x.bot --target "Win=process:Notepad"`. The selector string (`process:Notepad`) lands in `ResolvedTarget.Selector`. M5c1 resolves that string to an HWND at run start.
- **`CommandLineException`** (BotRunner) signals usage errors (mapped to exit code 2).
- **Engine failure handling:** if an executor returns `ActionResult.Fail(...)` or throws, the engine follows a wired `onFailure` port, else halts. So Click returns `Ok("onSuccess")` on success and `Fail(...)` when it can't run.
- Strict TDD for pure logic. The two Win32 adapter classes (`Win32InputSender`, `Win32WindowResolver`) **cannot be unit-tested** (they hit the OS); they are implemented minimally, verified by `dotnet build` (0 warnings) and the manual checklist.

## Build / test commands (run from the worktree root)

- Single class: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Targets.WindowSelectorTests"`
- Full suite: `dotnet test ADB.slnx`
- Zero-warning build (hard gate): `dotnet build ADB.slnx`

---

## File Structure

- **Create** `AdbCore/Targets/WindowSelector.cs` — parses a selector string (`process:` / `title:` / `hwnd:`) into a typed value. Pure.
- **Create** `AdbCore/Targets/IWindowResolver.cs` — `IntPtr Resolve(string selector)` abstraction.
- **Create** `AdbCore/Targets/Win32WindowResolver.cs` — Win32 adapter (process/title enumeration, hwnd parse). Build-only.
- **Create** `AdbCore/Input/IInputSender.cs` — input abstraction (Click only in c1).
- **Create** `AdbCore/Input/Win32InputSender.cs` — Win32 adapter (PostMessage). Build-only.
- **Create** `AdbCore/Actions/BuiltIn/ClickAction.cs` — `input.click` leaf action (takes `IInputSender`).
- **Create** `BotRunner/WindowTargetBinder.cs` — binds HWNDs onto Window `ResolvedTarget`s at run start.
- **Modify** `BotRunner/RunnerApp.cs` — call the binder; construct Click with the Win32 sender.
- **Modify** `AdbCore/Actions/BuiltIn/BuiltInActions.cs` — register Click with a `Win32InputSender`.
- **Create** tests: `AdbCore.Tests/Targets/WindowSelectorTests.cs`, `AdbCore.Tests/Actions/BuiltIn/ClickActionTests.cs`, `BotRunner.Tests/WindowTargetBinderTests.cs`.
- **Modify** tests: `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs`, `BotBuilder.Core.Tests/PaletteViewModelTests.cs` (counts + new Input category).

---

## Task 1: `WindowSelector` parsing (pure)

**Files:**
- Create: `AdbCore/Targets/WindowSelector.cs`
- Test: `AdbCore.Tests/Targets/WindowSelectorTests.cs`

- [ ] **Step 1: Write the failing tests** — create `AdbCore.Tests/Targets/WindowSelectorTests.cs`:

```csharp
using AdbCore.Targets;
using Xunit;

namespace AdbCore.Tests.Targets;

public class WindowSelectorTests
{
    [Fact]
    public void Parse_Process()
    {
        var s = WindowSelector.Parse("process:Notepad");
        Assert.Equal(WindowSelectorKind.Process, s.Kind);
        Assert.Equal("Notepad", s.Value);
    }

    [Fact]
    public void Parse_Title_KeepsValueVerbatimIncludingColons()
    {
        var s = WindowSelector.Parse("title:My App: Beta");
        Assert.Equal(WindowSelectorKind.Title, s.Kind);
        Assert.Equal("My App: Beta", s.Value); // only the first colon is the delimiter
    }

    [Fact]
    public void Parse_Hwnd()
    {
        var s = WindowSelector.Parse("hwnd:0x1A2B");
        Assert.Equal(WindowSelectorKind.Handle, s.Kind);
        Assert.Equal("0x1A2B", s.Value);
    }

    [Fact]
    public void Parse_IsCaseInsensitiveOnPrefix()
        => Assert.Equal(WindowSelectorKind.Process, WindowSelector.Parse("PROCESS:x").Kind);

    [Fact]
    public void Parse_UnknownPrefix_Throws()
        => Assert.Throws<FormatException>(() => WindowSelector.Parse("serial:emulator-5554"));

    [Fact]
    public void Parse_NoColon_Throws()
        => Assert.Throws<FormatException>(() => WindowSelector.Parse("Notepad"));

    [Fact]
    public void Parse_EmptyValue_Throws()
        => Assert.Throws<FormatException>(() => WindowSelector.Parse("process:"));
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Targets.WindowSelectorTests"` → FAIL to compile.

- [ ] **Step 3: Write the implementation** — create `AdbCore/Targets/WindowSelector.cs`:

```csharp
namespace AdbCore.Targets;

/// <summary>The kind of window a selector identifies.</summary>
public enum WindowSelectorKind
{
    Process,
    Title,
    Handle,
}

/// <summary>A parsed Window target selector, e.g. <c>process:Notepad</c>, <c>title:Untitled</c>, <c>hwnd:0x1A2B</c>.</summary>
public readonly record struct WindowSelector(WindowSelectorKind Kind, string Value)
{
    /// <summary>Parses a <c>kind:value</c> selector. The value keeps everything after the first colon
    /// verbatim (so titles may contain colons). Throws <see cref="FormatException"/> for an unknown
    /// prefix, a missing colon, or an empty value.</summary>
    public static WindowSelector Parse(string selector)
    {
        var colon = selector.IndexOf(':');
        if (colon <= 0 || colon == selector.Length - 1)
        {
            throw new FormatException($"Invalid window selector '{selector}'. Expected 'process:|title:|hwnd:<value>'.");
        }

        var prefix = selector[..colon];
        var value = selector[(colon + 1)..];

        var kind = prefix.ToLowerInvariant() switch
        {
            "process" => WindowSelectorKind.Process,
            "title" => WindowSelectorKind.Title,
            "hwnd" => WindowSelectorKind.Handle,
            _ => throw new FormatException($"Unknown window selector prefix '{prefix}'. Use process:, title:, or hwnd:."),
        };

        return new WindowSelector(kind, value);
    }
}
```

- [ ] **Step 4: Run to verify pass** — `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Targets.WindowSelectorTests"` → PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Targets/WindowSelector.cs AdbCore.Tests/Targets/WindowSelectorTests.cs
git commit -m "feat(core): add Window target selector parsing"
```

---

## Task 2: `IInputSender` + `ClickAction` (pure logic, fake sender)

**Files:**
- Create: `AdbCore/Input/IInputSender.cs`
- Create: `AdbCore/Actions/BuiltIn/ClickAction.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/ClickActionTests.cs`

- [ ] **Step 1: Write the failing tests** — create `AdbCore.Tests/Actions/BuiltIn/ClickActionTests.cs`:

```csharp
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Input;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class ClickActionTests
{
    private sealed class FakeInputSender : IInputSender
    {
        public int Calls { get; private set; }
        public IntPtr LastWindow { get; private set; }
        public int LastX { get; private set; }
        public int LastY { get; private set; }

        public void Click(IntPtr windowHandle, int clientX, int clientY)
        {
            Calls++;
            LastWindow = windowHandle;
            LastX = clientX;
            LastY = clientY;
        }
    }

    private static (BotAction action, BotExecutionContext ctx, Guid targetId) Setup(IntPtr handle)
    {
        var targetId = Guid.NewGuid();
        var ctx = new BotExecutionContext();
        ctx.Targets[targetId] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "hwnd:1", Handle = handle };
        var action = new BotAction { TypeKey = "input.click", TargetId = targetId };
        action.Config[ClickAction.XKey] = 10;
        action.Config[ClickAction.YKey] = 20;
        return (action, ctx, targetId);
    }

    [Fact]
    public async Task Click_SendsToTargetHwndAtClientCoords_AndFollowsOnSuccess()
    {
        var (action, ctx, _) = Setup((IntPtr)4660);
        var sender = new FakeInputSender();

        var result = await new ClickAction(sender).ExecuteAsync(new ActionExecutionContext(action, ctx, _ => { }), default);

        Assert.True(result.Success);
        Assert.Equal("onSuccess", result.OutputPort);
        Assert.Equal(1, sender.Calls);
        Assert.Equal((IntPtr)4660, sender.LastWindow);
        Assert.Equal(10, sender.LastX);
        Assert.Equal(20, sender.LastY);
    }

    [Fact]
    public async Task Click_DefaultsToSingleTargetWhenTargetIdNull()
    {
        var ctx = new BotExecutionContext();
        ctx.Targets[Guid.NewGuid()] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "hwnd:1", Handle = (IntPtr)99 };
        var action = new BotAction { TypeKey = "input.click", TargetId = null };
        action.Config[ClickAction.XKey] = 1;
        action.Config[ClickAction.YKey] = 2;
        var sender = new FakeInputSender();

        var result = await new ClickAction(sender).ExecuteAsync(new ActionExecutionContext(action, ctx, _ => { }), default);

        Assert.True(result.Success);
        Assert.Equal((IntPtr)99, sender.LastWindow);
    }

    [Fact]
    public async Task Click_NoResolvedTarget_FailsWithoutSending()
    {
        var action = new BotAction { TypeKey = "input.click", TargetId = null };
        action.Config[ClickAction.XKey] = 1;
        action.Config[ClickAction.YKey] = 2;
        var sender = new FakeInputSender();

        var result = await new ClickAction(sender).ExecuteAsync(
            new ActionExecutionContext(action, new BotExecutionContext(), _ => { }), default);

        Assert.False(result.Success);
        Assert.Equal(0, sender.Calls);
        Assert.Contains("Window target", result.ErrorMessage);
    }

    [Fact]
    public async Task Click_TargetWithoutHandle_Fails()
    {
        var targetId = Guid.NewGuid();
        var ctx = new BotExecutionContext();
        ctx.Targets[targetId] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "hwnd:1", Handle = null };
        var action = new BotAction { TypeKey = "input.click", TargetId = targetId };
        var sender = new FakeInputSender();

        var result = await new ClickAction(sender).ExecuteAsync(new ActionExecutionContext(action, ctx, _ => { }), default);

        Assert.False(result.Success);
        Assert.Equal(0, sender.Calls);
    }

    [Fact]
    public void Definition_Metadata()
    {
        var def = new ClickAction(new FakeInputSender());

        Assert.Equal("input.click", def.TypeKey);
        Assert.Equal("Input", def.Category);
        Assert.Equal(new[] { "in" }, def.InputPorts.Select(p => p.Name));
        Assert.Equal(new[] { "onSuccess", "onFailure" }, def.OutputPorts.Select(p => p.Name));
        Assert.Equal(new[] { ClickAction.XKey, ClickAction.YKey }, def.ConfigFields.Select(f => f.Key));
        Assert.False(def.SupportsRetry);
    }
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Actions.BuiltIn.ClickActionTests"` → FAIL to compile.

- [ ] **Step 3: Write `AdbCore/Input/IInputSender.cs`:**

```csharp
namespace AdbCore.Input;

/// <summary>Sends synthetic input to a target window, addressed by its HWND with client-relative
/// coordinates. The Win32 implementation uses PostMessage so input can target a window without it
/// being foreground. (Run-time only; not all windows honour synthesized messages.)</summary>
public interface IInputSender
{
    /// <summary>Posts a left click at the given client-relative coordinates of <paramref name="windowHandle"/>.</summary>
    void Click(IntPtr windowHandle, int clientX, int clientY);
}
```

- [ ] **Step 4: Write `AdbCore/Actions/BuiltIn/ClickAction.cs`:**

```csharp
using AdbCore.Execution;
using AdbCore.Input;
using AdbCore.Models;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Posts a left click at client-relative coordinates of the action's Window target.</summary>
public sealed class ClickAction : IActionDefinition, IActionExecutor
{
    public const string XKey = "x";
    public const string YKey = "y";
    public const string SuccessPort = "onSuccess";
    public const string FailurePort = "onFailure";

    private readonly IInputSender _sender;

    public ClickAction(IInputSender sender)
    {
        ArgumentNullException.ThrowIfNull(sender);
        _sender = sender;
    }

    public string TypeKey => "input.click";
    public string DisplayName => "Click";
    public string Category => "Input";
    public string Description => "Clicks at coordinates within the target window.";
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
    };
    public bool SupportsRetry => false;

    public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        if (ResolveWindow(context) is not IntPtr hwnd || hwnd == IntPtr.Zero)
        {
            return Task.FromResult(ActionResult.Fail("Click requires a resolved Window target (HWND)."));
        }

        var x = ConfigValues.GetInt(context.Action.Config, XKey);
        var y = ConfigValues.GetInt(context.Action.Config, YKey);
        _sender.Click(hwnd, x, y);

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

- [ ] **Step 5: Run to verify pass** — `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Actions.BuiltIn.ClickActionTests"` → PASS (5 tests).

- [ ] **Step 6: Commit**

```bash
git add AdbCore/Input/IInputSender.cs AdbCore/Actions/BuiltIn/ClickAction.cs AdbCore.Tests/Actions/BuiltIn/ClickActionTests.cs
git commit -m "feat(actions): add Click action over IInputSender abstraction"
```

---

## Task 3: `IWindowResolver` + `WindowTargetBinder` (pure wiring, fake resolver)

**Files:**
- Create: `AdbCore/Targets/IWindowResolver.cs`
- Create: `BotRunner/WindowTargetBinder.cs`
- Test: `BotRunner.Tests/WindowTargetBinderTests.cs`

- [ ] **Step 1: Write the failing tests** — create `BotRunner.Tests/WindowTargetBinderTests.cs`:

```csharp
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Targets;
using BotRunner;
using Xunit;

namespace BotRunner.Tests;

public class WindowTargetBinderTests
{
    private sealed class FakeWindowResolver : IWindowResolver
    {
        private readonly IntPtr _result;
        public string? LastSelector { get; private set; }
        public FakeWindowResolver(IntPtr result) => _result = result;
        public IntPtr Resolve(string selector) { LastSelector = selector; return _result; }
    }

    [Fact]
    public void Bind_SetsHandleOnWindowTargets()
    {
        var id = Guid.NewGuid();
        var targets = new Dictionary<Guid, ResolvedTarget>
        {
            [id] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "process:Notepad" },
        };
        var resolver = new FakeWindowResolver((IntPtr)777);

        WindowTargetBinder.Bind(targets, resolver);

        Assert.Equal((IntPtr)777, targets[id].Handle);
        Assert.Equal("process:Notepad", resolver.LastSelector);
    }

    [Fact]
    public void Bind_LeavesNonWindowTargetsUntouched()
    {
        var id = Guid.NewGuid();
        var targets = new Dictionary<Guid, ResolvedTarget>
        {
            [id] = new ResolvedTarget { Type = BotTargetType.AndroidDevice, Selector = "serial:emulator-5554" },
        };

        WindowTargetBinder.Bind(targets, new FakeWindowResolver((IntPtr)1));

        Assert.Null(targets[id].Handle);
    }

    [Fact]
    public void Bind_UnresolvableWindow_ThrowsCommandLineException()
    {
        var id = Guid.NewGuid();
        var targets = new Dictionary<Guid, ResolvedTarget>
        {
            [id] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "process:Ghost" },
        };

        var ex = Assert.Throws<CommandLineException>(
            () => WindowTargetBinder.Bind(targets, new FakeWindowResolver(IntPtr.Zero)));
        Assert.Contains("process:Ghost", ex.Message);
    }
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test ADB.slnx --filter "FullyQualifiedName~BotRunner.Tests.WindowTargetBinderTests"` → FAIL to compile.

- [ ] **Step 3: Write `AdbCore/Targets/IWindowResolver.cs`:**

```csharp
namespace AdbCore.Targets;

/// <summary>Resolves a Window target selector (e.g. <c>process:Notepad</c>) to a live window handle (HWND).
/// Returns <see cref="IntPtr.Zero"/> when no matching window is found.</summary>
public interface IWindowResolver
{
    IntPtr Resolve(string selector);
}
```

- [ ] **Step 4: Write `BotRunner/WindowTargetBinder.cs`:**

```csharp
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Targets;

namespace BotRunner;

/// <summary>At run start, resolves each Window target's selector to a live HWND and stores it on the
/// <see cref="ResolvedTarget.Handle"/>. Non-Window targets are left untouched (handled in later milestones).</summary>
public static class WindowTargetBinder
{
    public static void Bind(IReadOnlyDictionary<Guid, ResolvedTarget> targets, IWindowResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        foreach (var target in targets.Values)
        {
            if (target.Type != BotTargetType.Window)
            {
                continue;
            }

            var handle = resolver.Resolve(target.Selector);
            if (handle == IntPtr.Zero)
            {
                throw new CommandLineException(
                    $"Could not resolve Window target selector '{target.Selector}' to a window.");
            }

            target.Handle = handle;
        }
    }
}
```

- [ ] **Step 5: Run to verify pass** — `dotnet test ADB.slnx --filter "FullyQualifiedName~BotRunner.Tests.WindowTargetBinderTests"` → PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add AdbCore/Targets/IWindowResolver.cs BotRunner/WindowTargetBinder.cs BotRunner.Tests/WindowTargetBinderTests.cs
git commit -m "feat(runner): bind Window target HWNDs at run start via IWindowResolver"
```

---

## Task 4: Win32 adapters (`Win32InputSender`, `Win32WindowResolver`) — build-only

These two classes touch the OS and are not unit-tested; they are validated by the manual checklist. Keep them minimal and focused.

**Files:**
- Create: `AdbCore/Input/Win32InputSender.cs`
- Create: `AdbCore/Targets/Win32WindowResolver.cs`

- [ ] **Step 1: Write `AdbCore/Input/Win32InputSender.cs`:**

```csharp
using System.Runtime.InteropServices;

namespace AdbCore.Input;

/// <summary>Win32 implementation of <see cref="IInputSender"/> using PostMessage so a window need not be
/// foreground. Coordinates are client-relative and packed into the message lParam. Note: some apps and
/// most games ignore synthesized messages — for those, a foreground SendInput sender would be needed.</summary>
public sealed class Win32InputSender : IInputSender
{
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const int MK_LBUTTON = 0x0001;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public void Click(IntPtr windowHandle, int clientX, int clientY)
    {
        var lParam = MakeLParam(clientX, clientY);
        PostMessage(windowHandle, WM_LBUTTONDOWN, (IntPtr)MK_LBUTTON, lParam);
        PostMessage(windowHandle, WM_LBUTTONUP, IntPtr.Zero, lParam);
    }

    private static IntPtr MakeLParam(int x, int y)
        => (IntPtr)((y << 16) | (x & 0xFFFF));
}
```

- [ ] **Step 2: Write `AdbCore/Targets/Win32WindowResolver.cs`:**

```csharp
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace AdbCore.Targets;

/// <summary>Win32 implementation of <see cref="IWindowResolver"/>. Resolves <c>process:</c> (main window of
/// a named process), <c>title:</c> (first visible top-level window whose title contains the value), and
/// <c>hwnd:</c> (a literal handle, decimal or 0x-hex). Returns <see cref="IntPtr.Zero"/> if not found.</summary>
public sealed class Win32WindowResolver : IWindowResolver
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    public IntPtr Resolve(string selector)
    {
        var parsed = WindowSelector.Parse(selector);
        return parsed.Kind switch
        {
            WindowSelectorKind.Handle => ParseHandle(parsed.Value),
            WindowSelectorKind.Process => ResolveProcess(parsed.Value),
            WindowSelectorKind.Title => ResolveTitle(parsed.Value),
            _ => IntPtr.Zero,
        };
    }

    private static IntPtr ParseHandle(string value)
    {
        var isHex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        var style = isHex ? NumberStyles.HexNumber : NumberStyles.Integer;
        var text = isHex ? value[2..] : value;
        return long.TryParse(text, style, CultureInfo.InvariantCulture, out var n) ? (IntPtr)n : IntPtr.Zero;
    }

    private static IntPtr ResolveProcess(string name)
    {
        foreach (var process in Process.GetProcessesByName(name))
        {
            try
            {
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    return process.MainWindowHandle;
                }
            }
            catch (InvalidOperationException)
            {
                // process exited between enumeration and access; ignore
            }
        }

        return IntPtr.Zero;
    }

    private static IntPtr ResolveTitle(string titleSubstring)
    {
        var found = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
            {
                return true;
            }

            var length = GetWindowTextLength(hWnd);
            if (length == 0)
            {
                return true;
            }

            var sb = new StringBuilder(length + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            if (sb.ToString().Contains(titleSubstring, StringComparison.OrdinalIgnoreCase))
            {
                found = hWnd;
                return false; // stop enumerating
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }
}
```

- [ ] **Step 3: Build to verify it compiles with zero warnings**

Run: `dotnet build ADB.slnx`
Expected: Build succeeded, **0 Warning(s), 0 Error(s)**. (These classes have no unit tests; they are exercised by the manual checklist in Task 5.)

- [ ] **Step 4: Commit**

```bash
git add AdbCore/Input/Win32InputSender.cs AdbCore/Targets/Win32WindowResolver.cs
git commit -m "feat(core): add Win32 input sender (PostMessage) and window resolver adapters"
```

---

## Task 5: Wire into the runner, register Click, counts, gate, manual checklist

**Files:**
- Modify: `BotRunner/RunnerApp.cs`
- Modify: `AdbCore/Actions/BuiltIn/BuiltInActions.cs`
- Modify: `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs`
- Modify: `BotBuilder.Core.Tests/PaletteViewModelTests.cs`
- Modify: `Docs/Specs/2026-06-01-m5-built-in-actions-design.md`

After this: definitions = 11, executors = 8. A new **Input** palette category with 1 item (Click).

- [ ] **Step 1: Update the registration assertions (failing first)**

In `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs`, in `Register_AddsAllBuiltInsToBothRegistries`, change the dual-registry key list to include `"input.click"` and update the counts. Replace:

```csharp
        foreach (var key in new[] { "control.start", "control.end", "data.log", "control.delay", "control.branch", "data.setVariable", "data.comment" })
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

        Assert.Equal(10, defs.Count);
        Assert.Equal(7, execs.Count);
```

with:

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

- [ ] **Step 2: Update the palette tests**

In `BotBuilder.Core.Tests/PaletteViewModelTests.cs`:
- In `Categories_GroupBuiltInsByCategory`, add an Input-category assertion after the `data` assertion:

```csharp
        var input = palette.Categories.Single(c => c.Name == "Input");
        Assert.Single(input.Items); // Click
```

- In `ClearingSearch_RestoresAll`, change the total to:

```csharp
        Assert.Equal(11, palette.Categories.SelectMany(c => c.Items).Count()); // 7 Control Flow + 3 Data + 1 Input
```

- [ ] **Step 3: Run those tests to verify they FAIL**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AdbCore.Tests.Actions.BuiltIn.BuiltInActionsTests|FullyQualifiedName~BotBuilder.Core.Tests.PaletteViewModelTests"`
Expected: FAIL (Click not registered; no Input category yet).

- [ ] **Step 4: Register Click**

In `AdbCore/Actions/BuiltIn/BuiltInActions.cs`, add the using at the top:

```csharp
using AdbCore.Input;
```

Then in `Register`, after `Add(new CommentAction(), definitions, executors);`, add:

```csharp
        // Input actions need an IInputSender; the real app uses the Win32 (PostMessage) implementation.
        Add(new ClickAction(new Win32InputSender()), definitions, executors);
```

- [ ] **Step 5: Wire HWND binding into the runner**

In `BotRunner/RunnerApp.cs`, add the using:

```csharp
using AdbCore.Targets;
```

Then immediately after the line `var resolvedTargets = TargetResolver.Resolve(bot, args.Targets);`, add:

```csharp
        // Resolve Window target selectors to live HWNDs before execution (Input/Screen need them).
        WindowTargetBinder.Bind(resolvedTargets, new Win32WindowResolver());
```

- [ ] **Step 6: Run the full suite**

Run: `dotnet test ADB.slnx`
Expected: ALL green. If any other test asserts a built-in count or category, update it for the added Input action.

- [ ] **Step 7: Zero-warning build gate**

Run: `dotnet build ADB.slnx`
Expected: Build succeeded, **0 Warning(s), 0 Error(s)**.

- [ ] **Step 8: Update the spec status line**

In `Docs/Specs/2026-06-01-m5-built-in-actions-design.md`, change the status line to exactly:

```
**Status:** Approved — M5a1 + M5a2 + M5b + M5c1 (control-flow engine + Data actions + Input infra/Click) implemented
```

- [ ] **Step 9: Commit**

```bash
git add AdbCore/Actions/BuiltIn/BuiltInActions.cs BotRunner/RunnerApp.cs AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs BotBuilder.Core.Tests/PaletteViewModelTests.cs Docs/Specs/2026-06-01-m5-built-in-actions-design.md
git commit -m "feat(actions): register Click and bind Window HWNDs in the runner"
```

---

## Manual Verification Checklist (for the user — this is the real test of the OS adapters)

The unit tests cover the logic; the Win32 adapters (`Win32InputSender`, `Win32WindowResolver`) can only be verified live. Suggested check:

- [ ] Open **Notepad** (or any standard window).
- [ ] In BotBuilder, the palette now has an **Input** category with **Click** (config: X, Y; ports In → On Success / On Failure). Build a bot with **one Window target** and: Start → Click (X/Y over a visible spot in the target) → End. Save it.
- [ ] Run it: `BotRunner.exe --bot yourbot.bot --target "<TargetName>=process:Notepad"` (or `title:Untitled - Notepad`, or `hwnd:0x...`).
- [ ] Expect: run succeeds (exit 0), log shows the `input.click` action `success: true`. Because this uses `PostMessage` (not foreground SendInput), a standard window receives the click without being focused.
- [ ] Try an unresolvable target (`process:DoesNotExist`) → the runner should exit with a clear "Could not resolve Window target selector" message (exit code 2).

> **Known limitation (by design, from M5 scoping):** `PostMessage` works for standard Win32 windows but **many games / GPU-rendered apps ignore synthesized messages**. If your target ignores the click, that's the expected `PostMessage` constraint — the `IInputSender` seam lets us add a foreground `SendInput` adapter later without touching the Click action or tests. Flag it and we'll add that adapter.

---

## Self-Review (completed by plan author)

**Spec coverage (design §4.2 / §5 / slice M5c, c1 portion):**
- Window-targeted, HWND-relative input — `ClickAction` + `IInputSender` + client coords. ✓
- Window→HWND resolution pulled forward — `WindowSelector` + `IWindowResolver` + `Win32WindowResolver` + `WindowTargetBinder` wired at run start. ✓
- PostMessage (background-capable) per scoping choice; foreground SendInput noted as a swappable future adapter. ✓
- Click action (first of the Input set). ✓ Remaining Input actions (Type Text, Key Press, Mouse Move, Right/Double Click) are **c2**.
- OS code behind narrow interfaces so logic is headlessly testable; only `Win32InputSender`/`Win32WindowResolver` are untested adapters. ✓

**Placeholder scan:** none — complete code/commands in every step. The two adapter tasks are explicitly build-only + manually verified (not a placeholder; an inherent property of OS code).

**Type consistency:** `WindowSelector`/`WindowSelectorKind`, `IInputSender.Click(IntPtr,int,int)`, `IWindowResolver.Resolve(string)→IntPtr`, `ClickAction.{XKey,YKey,SuccessPort,FailurePort}`, `WindowTargetBinder.Bind`, and `ResolvedTarget.Handle` (object?, set to boxed IntPtr; read via `as IntPtr?`) are used consistently. Counts: 11 defs / 8 execs; palette 7 Control + 3 Data + 1 Input = 11.

**Out of scope (c2 / later):** remaining Input actions; foreground SendInput adapter; Screen (M5d); multi-target default-selection beyond "single target when TargetId null" (the current rule fails clearly when ambiguous).
