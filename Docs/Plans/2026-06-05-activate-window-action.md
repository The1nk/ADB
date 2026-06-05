# Activate Window Action Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** An **Activate Window** action (`window.activate`, Window category) that brings its target window to the foreground via an injectable `IWindowActivator`.

**Architecture:** `IWindowActivator.Activate(IntPtr)` + a `Win32WindowActivator` (IsIconic→ShowWindow(restore)→SetForegroundWindow). `ActivateWindowAction` resolves the HWND via `TargetResolution.ResolveHandle<IntPtr>` and calls the activator. Editor `NodeTargetType` gets a `"Window"`→Window arm.

**Tech Stack:** C# / .NET 10, AdbCore (+ BotBuilder.Core mapping), xUnit.

**Reference spec:** `Docs/Specs/2026-06-05-activate-window-action-design.md`.

**Merge handling:** logic unit-tested w/ a fake; concrete Win32 impl reuses the proven `SetForegroundWindow` mechanism → **self-merged** via `gh` (user go-ahead). Conflict-free with PRs #37/#39.

**`<WT>` = `C:\git\ADB\.claude\worktrees\activate-window`. Windows: PowerShell for `dotnet`/`git`; NEVER redirect to `/dev/null`/`NUL`.**

---

## Task 1: `IWindowActivator` + Win32 impl + `ActivateWindowAction`

**Files:** Create `AdbCore/Window/IWindowActivator.cs`, `AdbCore/Window/Win32WindowActivator.cs`, `AdbCore/Actions/BuiltIn/ActivateWindowAction.cs`, `AdbCore.Tests/Actions/BuiltIn/ActivateWindowActionTests.cs`.

- [ ] **Step 1: Read** `AdbCore/Input/Win32SendInputSender.cs` (the `SetForegroundWindow` P/Invoke declaration + namespace conventions), `AdbCore/Actions/BuiltIn/ScreenActionBase.cs` (how it resolves the HWND — now via `TargetResolution.ResolveHandle<IntPtr>` after #36; confirm the exact call + the `ActionResult.Ok/Fail` + `ActionExecutionContext` shapes), and a sibling action with onSuccess/onFailure (e.g. `FindImageAction` or `BranchAction`) for the `IActionDefinition`/`IActionExecutor` member shapes.

- [ ] **Step 2: Create `AdbCore/Window/IWindowActivator.cs`:**
```csharp
namespace AdbCore.Window;

/// <summary>Brings a window to the foreground. Injectable so the Activate Window action is unit-testable
/// without a real window.</summary>
public interface IWindowActivator
{
    /// <summary>Restores the window if minimized and brings it to the foreground.</summary>
    void Activate(IntPtr handle);
}
```

- [ ] **Step 3: Create `AdbCore/Window/Win32WindowActivator.cs`** (mirror the P/Invoke style in `Win32SendInputSender`):
```csharp
using System.Runtime.InteropServices;

namespace AdbCore.Window;

/// <summary>The live <see cref="IWindowActivator"/>: restores a minimized window then sets it foreground —
/// the same <c>SetForegroundWindow</c> mechanism the input senders use so injected clicks/keys land on it.</summary>
public sealed class Win32WindowActivator : IWindowActivator
{
    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);

    public void Activate(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return;
        if (IsIconic(handle)) { ShowWindow(handle, SW_RESTORE); }
        SetForegroundWindow(handle);
    }
}
```

- [ ] **Step 4: Write the failing tests.** Create `AdbCore.Tests/Actions/BuiltIn/ActivateWindowActionTests.cs`:
```csharp
using System;
using System.Linq;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Window;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class ActivateWindowActionTests
{
    private sealed class FakeActivator : IWindowActivator
    {
        public IntPtr? Activated { get; private set; }
        public void Activate(IntPtr handle) => Activated = handle;
    }

    private static ActionExecutionContext Exec(BotAction a, BotExecutionContext c) => new(a, c, _ => { });

    [Fact]
    public async Task Activates_TheLoneWindowTarget_AndSucceeds()
    {
        var fake = new FakeActivator();
        var ctx = new BotExecutionContext();
        ctx.Targets[Guid.NewGuid()] = new ResolvedTarget { Handle = (IntPtr)0x4321 };
        var action = new BotAction();   // no explicit TargetId -> resolves the lone Window-handle target

        var r = await new ActivateWindowAction(fake).ExecuteAsync(Exec(action, ctx), default);

        Assert.True(r.Success);
        Assert.Equal("onSuccess", r.OutputPort);
        Assert.Equal((IntPtr)0x4321, fake.Activated);
    }

    [Fact]
    public async Task NoWindowTarget_RoutesOnFailure_AndDoesNotActivate()
    {
        var fake = new FakeActivator();
        var r = await new ActivateWindowAction(fake).ExecuteAsync(Exec(new BotAction(), new BotExecutionContext()), default);
        Assert.False(r.Success);
        Assert.Null(fake.Activated);
    }

    [Fact]
    public void Definition_Metadata()
    {
        var def = new ActivateWindowAction(new FakeActivator());
        Assert.Equal("window.activate", def.TypeKey);
        Assert.Equal("Activate Window", def.DisplayName);
        Assert.Equal("Window", def.Category);
        Assert.Equal(new[] { "onSuccess", "onFailure" }, def.OutputPorts.Select(p => p.Name));
        Assert.Empty(def.ConfigFields);
    }
}
```
(Confirm `ActionResult.OutputPort`/`.Success`, `ActionExecutionContext(BotAction, BotExecutionContext, Action<string>)`, `ResolvedTarget { Handle }`, `BotExecutionContext.Targets` against the real API — all used across the suite + the #36 `TargetResolutionTests`. Boxing `(IntPtr)0x4321` into `object? Handle` is how a Window target's handle is stored.)

- [ ] **Step 5: Run to verify it fails** — `dotnet test "<WT>\AdbCore.Tests" --filter "FullyQualifiedName~ActivateWindowActionTests"` → compile FAIL.

- [ ] **Step 6: Create `AdbCore/Actions/BuiltIn/ActivateWindowAction.cs`:**
```csharp
using AdbCore.Execution;
using AdbCore.Window;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Brings the target window to the foreground (restoring it if minimized). Resolves the window the
/// same way the Screen/Input actions do (explicit TargetId or the lone Window target). No window -> onFailure.</summary>
public sealed class ActivateWindowAction : IActionDefinition, IActionExecutor
{
    public const string SuccessPort = "onSuccess";
    public const string FailurePort = "onFailure";

    private readonly IWindowActivator _activator;
    public ActivateWindowAction(IWindowActivator activator)
    {
        ArgumentNullException.ThrowIfNull(activator);
        _activator = activator;
    }

    public string TypeKey => "window.activate";
    public string DisplayName => "Activate Window";
    public string Category => "Window";
    public string Description => "Brings the target window to the foreground (restoring it if minimized).";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public List<PortDefinition> OutputPorts { get; } = new()
    {
        new PortDefinition { Name = SuccessPort, Label = "On Success" },
        new PortDefinition { Name = FailurePort, Label = "On Failure" },
    };
    public List<ConfigField> ConfigFields { get; } = new();
    public bool SupportsRetry => false;

    public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (TargetResolution.ResolveHandle<IntPtr>(context) is not IntPtr handle || handle == IntPtr.Zero)
        {
            return Task.FromResult(ActionResult.Fail("Activate Window requires a window target."));
        }

        _activator.Activate(handle);
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
```
**Adapt** to the real `IActionDefinition`/`IActionExecutor` members + `TargetResolution.ResolveHandle<IntPtr>` signature (confirm against `ScreenActionBase.ResolveWindow` which now delegates to it). The `Definition_Metadata`/behavior tests are the spec.

- [ ] **Step 7: Run to verify it passes** — green (build 0 warnings: `dotnet build "<WT>\AdbCore" -warnaserror -v q --nologo`).

- [ ] **Step 8: Commit:**
```
git -C "<WT>" add AdbCore/Window/IWindowActivator.cs AdbCore/Window/Win32WindowActivator.cs AdbCore/Actions/BuiltIn/ActivateWindowAction.cs AdbCore.Tests/Actions/BuiltIn/ActivateWindowActionTests.cs
git -C "<WT>" commit -m "feat(window): Activate Window action + injectable IWindowActivator (Win32 SetForegroundWindow)"
```

---

## Task 2: Register + NodeTargetType + counts + sweep + self-merge

**Files:** modify `AdbCore/Actions/BuiltIn/BuiltInActions.cs`, `BotBuilder.Core/Targets/NodeTargetType.cs`; create `AdbCore.Tests/Actions/BuiltIn/ActivateWindowRegistrationTests.cs`; modify `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs`, `BotBuilder.Core.Tests/Targets/NodeTargetTypeTests.cs`, `BotBuilder.Core.Tests/PaletteViewModelTests.cs`.

- [ ] **Step 1: Register** in `BuiltInActions.cs`, near the Input/Screen registrations (it's a window-acting action with an injected dep):
```csharp
        Add(new ActivateWindowAction(new Win32WindowActivator()), definitions, executors);
```
(Add `using AdbCore.Window;` if needed. Match the real `Add(...)` signature.)

- [ ] **Step 2: Registration test.** Create `AdbCore.Tests/Actions/BuiltIn/ActivateWindowRegistrationTests.cs`:
```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class ActivateWindowRegistrationTests
{
    [Fact]
    public void ActivateWindow_IsRegistered()
    {
        var defs = new ActionRegistry();
        var execs = new ActionExecutorRegistry();
        BuiltInActions.Register(defs, execs);
        Assert.True(defs.TryGet("window.activate", out _));
        Assert.True(execs.TryGet("window.activate", out var e) && e is not null);
    }
}
```

- [ ] **Step 3: Extend `NodeTargetType.For`** in `BotBuilder.Core/Targets/NodeTargetType.cs` — add the `"Window"` arm:
```csharp
        "Screen" => BotTargetType.Window,
        "Input" => BotTargetType.Window,
        "Window" => BotTargetType.Window,
```
Add a `NodeTargetTypeTests` case: `[InlineData("Window", BotTargetType.Window)]` to the `For_KnownCategories_MapToTargetType` theory.

- [ ] **Step 4: Bump counts.** Read CURRENT counts in `BuiltInActionsTests.cs` and bump def/exec each +1. In `BotBuilder.Core.Tests/PaletteViewModelTests.cs`, bump the total +1 and add a **Window** category assertion (new category, 1 item) — mirror how the M12a slice added the `Scripting` category assertion (use `Assert.Single(window.Items)` per the analyzer-clean style). Read the file to match its structure.

- [ ] **Step 5: Full sweep.** `dotnet build "<WT>\ADB.slnx" -warnaserror -v q --nologo` → 0 warnings; `dotnet test "<WT>\ADB.slnx"` → all green. Report totals.

- [ ] **Step 6: Commit:**
```
git -C "<WT>" add AdbCore/Actions/BuiltIn/BuiltInActions.cs BotBuilder.Core/Targets/NodeTargetType.cs AdbCore.Tests/Actions/BuiltIn/ActivateWindowRegistrationTests.cs AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs BotBuilder.Core.Tests/Targets/NodeTargetTypeTests.cs BotBuilder.Core.Tests/PaletteViewModelTests.cs
git -C "<WT>" commit -m "feat(window): register Activate Window (Window category) + auto-target mapping"
```

---

## Self-Review Notes (addressed)

- **Spec coverage:** `IWindowActivator` + Win32 impl (Task 1); action resolving HWND via `TargetResolution` with onSuccess/onFailure + no config + Window category (Task 1); registration + `NodeTargetType` "Window" arm + counts/palette (Task 2). ✓
- **Testability:** fake activator unit-tests the action logic; concrete Win32 impl is a thin proven wrapper (not unit-tested). ✓
- **Type consistency:** `IWindowActivator.Activate(IntPtr)`, `ActivateWindowAction(IWindowActivator)`, TypeKey `window.activate`, ports onSuccess/onFailure, `NodeTargetType.For("Window")→Window`. ✓
- **Conflict-free** with #37/#39 (touches AdbCore + `NodeTargetType.cs`/`BuiltInActionsTests.cs`/`PaletteViewModelTests.cs` — none in those PRs). Self-merge per user go-ahead.
