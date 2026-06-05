# Type-Aware Target Defaulting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** An unassigned (no `TargetId`) action defaults to the single run target whose live handle matches the action's type — fixing multi-target bots (e.g. 1 Android + 1 Window) where type-specific nodes currently fail.

**Architecture:** Replace five copy-pasted `targets.Count == 1` resolution blocks with one shared `TargetResolution.ResolveHandle<T>(context)` that resolves the explicit `TargetId` (if its handle is a `T`) or the single target whose handle is a `T`.

**Tech Stack:** C# / .NET 10, AdbCore, xUnit.

**Reference spec:** `Docs/Specs/2026-06-05-type-aware-target-defaulting-design.md`.

**Merge handling:** backend-only but changes runtime resolution semantics → opened as a PR and **user-verified + merged** (NOT self-merged). Independent of PR #34 (Scripting).

**`<WT>` = `C:\git\ADB\.claude\worktrees\type-aware-target`. Windows: PowerShell for `dotnet`/`git`; NEVER redirect to `/dev/null`/`NUL`.**

---

## File Structure

- Create `AdbCore/Execution/TargetResolution.cs` — the shared `ResolveHandle<T>` helper.
- Create `AdbCore.Tests/Execution/TargetResolutionTests.cs` — unit tests for the helper.
- Modify `AdbCore/Actions/BuiltIn/Android/AndroidActionBase.cs` — `ResolveDevice` delegates to the helper.
- Modify `AdbCore/Actions/BuiltIn/ScreenActionBase.cs` — `ResolveWindow` delegates.
- Modify `AdbCore/Actions/BuiltIn/InputActionBase.cs` — `ResolveWindow` delegates.
- Modify `AdbCore/Actions/BuiltIn/ScreenOcrActionBase.cs` — `ResolveWindow` delegates.
- Modify `AdbCore/Actions/BuiltIn/Browser/BrowserActionBase.cs` — `ResolvePage` delegates.

---

## Task 1: `TargetResolution.ResolveHandle<T>` + tests

**Files:** Create `AdbCore/Execution/TargetResolution.cs`, `AdbCore.Tests/Execution/TargetResolutionTests.cs`.

- [ ] **Step 1: Write the failing tests.** Create `AdbCore.Tests/Execution/TargetResolutionTests.cs`:
```csharp
using System;
using AdbCore.Android;
using AdbCore.Browser;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Execution;

public class TargetResolutionTests
{
    private static ActionExecutionContext Make(BotExecutionContext ctx, Guid? targetId)
    {
        var action = new BotAction { TargetId = targetId };
        return new ActionExecutionContext(action, ctx, _ => { });
    }

    private static (BotExecutionContext ctx, Guid androidId, Guid windowId) MixedContext()
    {
        var ctx = new BotExecutionContext();
        var androidId = Guid.NewGuid();
        var windowId = Guid.NewGuid();
        ctx.Targets[androidId] = new ResolvedTarget { Handle = new FakeAndroidDevice() };
        ctx.Targets[windowId] = new ResolvedTarget { Handle = (IntPtr)0x1234 };
        return (ctx, androidId, windowId);
    }

    [Fact]
    public void Unassigned_PicksSingleTargetOfType_Android()
    {
        var (ctx, _, _) = MixedContext();
        var handle = TargetResolution.ResolveHandle<IAndroidDevice>(Make(ctx, null));
        Assert.NotNull(handle);
    }

    [Fact]
    public void Unassigned_PicksSingleTargetOfType_Window()
    {
        var (ctx, _, _) = MixedContext();
        var hwnd = TargetResolution.ResolveHandle<IntPtr>(Make(ctx, null));
        Assert.Equal((IntPtr)0x1234, hwnd);
    }

    [Fact]
    public void Unassigned_PicksSingleTargetOfType_Browser()
    {
        var ctx = new BotExecutionContext();
        ctx.Targets[Guid.NewGuid()] = new ResolvedTarget { Handle = new FakeAndroidDevice() };
        ctx.Targets[Guid.NewGuid()] = new ResolvedTarget { Handle = new FakeBrowserPage() };
        var page = TargetResolution.ResolveHandle<IBrowserPage>(Make(ctx, null));
        Assert.NotNull(page);
    }

    [Fact]
    public void Unassigned_TwoOfType_IsAmbiguous_ReturnsDefault()
    {
        var ctx = new BotExecutionContext();
        ctx.Targets[Guid.NewGuid()] = new ResolvedTarget { Handle = new FakeAndroidDevice() };
        ctx.Targets[Guid.NewGuid()] = new ResolvedTarget { Handle = new FakeAndroidDevice() };
        Assert.Null(TargetResolution.ResolveHandle<IAndroidDevice>(Make(ctx, null)));
    }

    [Fact]
    public void Unassigned_NoneOfType_ReturnsDefault()
    {
        var (ctx, _, _) = MixedContext(); // android + window, no browser
        Assert.Null(TargetResolution.ResolveHandle<IBrowserPage>(Make(ctx, null)));
    }

    [Fact]
    public void Explicit_RightType_ResolvesThatTarget()
    {
        var (ctx, androidId, _) = MixedContext();
        Assert.NotNull(TargetResolution.ResolveHandle<IAndroidDevice>(Make(ctx, androidId)));
    }

    [Fact]
    public void Explicit_WrongType_ReturnsDefault()
    {
        var (ctx, _, windowId) = MixedContext();
        // windowId's handle is an IntPtr, not an IAndroidDevice
        Assert.Null(TargetResolution.ResolveHandle<IAndroidDevice>(Make(ctx, windowId)));
    }

    [Fact]
    public void Explicit_MissingId_ReturnsDefault()
    {
        var (ctx, _, _) = MixedContext();
        Assert.Null(TargetResolution.ResolveHandle<IAndroidDevice>(Make(ctx, Guid.NewGuid())));
    }

    [Fact]
    public void Unassigned_SingleTargetTotal_BackCompat()
    {
        var ctx = new BotExecutionContext();
        ctx.Targets[Guid.NewGuid()] = new ResolvedTarget { Handle = new FakeAndroidDevice() };
        Assert.NotNull(TargetResolution.ResolveHandle<IAndroidDevice>(Make(ctx, null)));
    }

    private sealed class FakeAndroidDevice : IAndroidDevice
    {
        // Implement the IAndroidDevice surface as minimal stubs. ADAPT to the real interface members.
        public string Serial => "fake";
        public System.Threading.Tasks.Task<System.Drawing.Bitmap> Screenshot(System.Threading.CancellationToken ct)
            => throw new NotImplementedException();
        public System.Threading.Tasks.Task Tap(int x, int y, System.Threading.CancellationToken ct)
            => throw new NotImplementedException();
        // ...add the remaining IAndroidDevice members as no-op/NotImplementedException stubs.
    }

    private sealed class FakeBrowserPage : IBrowserPage
    {
        // Implement the IBrowserPage surface as minimal stubs. ADAPT to the real interface members.
    }
}
```
**ADAPT the fakes to the REAL interfaces.** Read `AdbCore/Android/IAndroidDevice.cs` and `AdbCore/Browser/IBrowserPage.cs` for their exact members, and **prefer reusing existing test doubles** if the Android/Browser action tests already define a fake device/page (search `AdbCore.Tests` for `IAndroidDevice`/`IBrowserPage` implementations — e.g. a `FakeAndroidDevice`/`StubBrowserPage`). Reuse beats re-stubbing. The fakes only need to *be* the interface; their methods are never called by `ResolveHandle`. Also confirm `ActionExecutionContext(BotAction, BotExecutionContext, Action<string>)`, that `BotAction.TargetId` is a `Guid?`, and that `BotExecutionContext.Targets` is the mutable `Dictionary<Guid, ResolvedTarget>` and `ResolvedTarget.Handle` is settable.

- [ ] **Step 2: Run to verify it fails** — `dotnet test "<WT>\AdbCore.Tests" --filter "FullyQualifiedName~TargetResolutionTests"` → compile FAIL (`TargetResolution` missing).

- [ ] **Step 3: Create `AdbCore/Execution/TargetResolution.cs`:**
```csharp
namespace AdbCore.Execution;

/// <summary>Resolves the live handle an action should act on from the run's targets: the explicit
/// <c>TargetId</c> (when set and its handle is of the requested type), or — when the action has no
/// <c>TargetId</c> — the single target whose live handle is of that type. Returns the default (null/none)
/// when there is no match, or when the type-default is ambiguous (zero, or more than one, target of that
/// type). Null/unbound handles never match, so an unbound target is ignored.</summary>
public static class TargetResolution
{
    public static T? ResolveHandle<T>(ActionExecutionContext context)
    {
        var targets = context.Context.Targets;

        if (context.Action.TargetId is Guid id)
        {
            return targets.TryGetValue(id, out var t) && t.Handle is T match ? match : default;
        }

        T? found = default;
        var count = 0;
        foreach (var target in targets.Values)
        {
            if (target.Handle is T handle)
            {
                found = handle;
                count++;
            }
        }

        return count == 1 ? found : default;
    }
}
```

- [ ] **Step 4: Run to verify it passes** — same filter → all green.

- [ ] **Step 5: Commit:**
```
git -C "<WT>" add AdbCore/Execution/TargetResolution.cs AdbCore.Tests/Execution/TargetResolutionTests.cs
git -C "<WT>" commit -m "feat(execution): TargetResolution.ResolveHandle<T> (type-aware target defaulting)"
```

---

## Task 2: Delegate the five resolution sites to the helper

**Files:** Modify `AndroidActionBase.cs`, `ScreenActionBase.cs`, `InputActionBase.cs`, `ScreenOcrActionBase.cs`, `Browser/BrowserActionBase.cs`.

For EACH site, replace the body of its resolve method with a delegation to `TargetResolution.ResolveHandle<T>` (keep the method name/signature/visibility so callers are unchanged), and remove now-unused `using System.Linq;` if it was only there for `.First()` (build with `-warnaserror` will catch an unused using only if the analyzer is on; otherwise just leave imports that are still used — verify the file still uses Linq elsewhere before removing).

- [ ] **Step 1:** `AndroidActionBase.ResolveDevice`:
```csharp
    protected static IAndroidDevice? ResolveDevice(ActionExecutionContext context)
        => TargetResolution.ResolveHandle<IAndroidDevice>(context);
```
(Add `using AdbCore.Execution;` if not present — it likely already is.)

- [ ] **Step 2:** `ScreenActionBase.ResolveWindow`:
```csharp
    protected static IntPtr? ResolveWindow(ActionExecutionContext context)
        => TargetResolution.ResolveHandle<IntPtr>(context);
```

- [ ] **Step 3:** `InputActionBase.ResolveWindow` (private):
```csharp
    private static IntPtr? ResolveWindow(ActionExecutionContext context)
        => TargetResolution.ResolveHandle<IntPtr>(context);
```

- [ ] **Step 4:** `ScreenOcrActionBase.ResolveWindow`:
```csharp
    protected static IntPtr? ResolveWindow(ActionExecutionContext context)
        => TargetResolution.ResolveHandle<IntPtr>(context);
```

- [ ] **Step 5:** `BrowserActionBase.ResolvePage`:
```csharp
    protected static IBrowserPage? ResolvePage(ActionExecutionContext context)
        => TargetResolution.ResolveHandle<IBrowserPage>(context);
```

- [ ] **Step 6: Build + full test sweep.** `dotnet build "<WT>\ADB.slnx" -warnaserror -v q --nologo` → 0 warnings (fix any now-unused `using System.Linq;` the analyzer flags). `dotnet test "<WT>\ADB.slnx"` → all green (the existing Android/Screen/Input/OCR/Browser action tests must still pass — they construct single-target contexts which resolve identically). Report totals.

- [ ] **Step 7: Commit:**
```
git -C "<WT>" add AdbCore/Actions/BuiltIn/Android/AndroidActionBase.cs AdbCore/Actions/BuiltIn/ScreenActionBase.cs AdbCore/Actions/BuiltIn/InputActionBase.cs AdbCore/Actions/BuiltIn/ScreenOcrActionBase.cs AdbCore/Actions/BuiltIn/Browser/BrowserActionBase.cs
git -C "<WT>" commit -m "refactor(actions): resolve targets via type-aware TargetResolution helper"
```

---

## Self-Review Notes (addressed)

- **Spec coverage:** the shared `ResolveHandle<T>` (Task 1) with explicit/typed-default/ambiguous/none behaviors; all five sites delegate (Task 2); existing single-target behavior preserved (back-compat test + the existing action tests). ✓
- **No regression:** any bot that resolved under `Count == 1` had one target = the single target of its type; type mismatches failed before and still fail. ✓
- **Type consistency:** `TargetResolution.ResolveHandle<T>(ActionExecutionContext) → T?`; sites pass `IAndroidDevice` / `IntPtr` / `IBrowserPage`. ✓
- **No new external deps; no UI.** Backend; but a runtime-semantics change → user-verified PR, not self-merge.
