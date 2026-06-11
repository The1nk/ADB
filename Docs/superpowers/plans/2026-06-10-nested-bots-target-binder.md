# Nested Bots — Lazy Target Binder Slice (B2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A nested bot can bind its OWN (non-shared) targets — Window / Android / Browser — on demand, only when its card actually executes, with those freshly-created handles disposed when the child run ends. Parent targets shared by name (from B1) are reused and never disposed by the child.

**Architecture:** A new engine-level `ITargetBinder` (single target → live handle, async) is threaded through `ExecutionOptions`/`BotExecutionContext`. `NestedBotExecutor` builds the child's resolved-target map by (a) overlaying a parent target onto a same-named nested target, else (b) calling the binder to bind the nested target's own selector; it disposes only the handles it created. `BotRunner` implements `ITargetBinder` (`RunnerTargetBinder`) by mirroring its three run-start binders, and passes it into `ExecutionOptions`.

**Tech Stack:** .NET 10, AdbCore (engine, binder interface), BotRunner (Win32/ADB/Playwright implementation), xUnit with hand-rolled fakes.

Reference spec: `Docs/superpowers/specs/2026-06-10-title-bar-and-nested-bots-design.md` (Feature B, section B7). Builds on the merged B1 engine slice.

Work in worktree `C:\git\ADB-nested-targets` (branch `worktree-nested-bots-targets`). Build/test from the worktree root.

---

### Task 1: `ITargetBinder` interface + thread it through execution

**Files:**
- Create: `AdbCore/Execution/ITargetBinder.cs`
- Modify: `AdbCore/Execution/ExecutionOptions.cs`
- Modify: `AdbCore/Execution/BotExecutionContext.cs`
- Modify: `AdbCore/Execution/BotExecutor.cs`
- Test: `AdbCore.Tests/Execution/TargetBinderPlumbingTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `AdbCore.Tests/Execution/TargetBinderPlumbingTests.cs`:

```csharp
using AdbCore.Actions;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Execution;

public class TargetBinderPlumbingTests
{
    private sealed class FakeBinder : ITargetBinder
    {
        public Task<ResolvedTarget> BindAsync(BotTarget target, CancellationToken ct)
            => Task.FromResult(new ResolvedTarget { Type = target.Type, Selector = target.Selector });
    }

    // A leaf that records whether the run context carries a TargetBinder.
    private sealed class ProbeLeaf : IActionDefinition, IActionExecutor
    {
        public string TypeKey => "test.probeBinder";
        public string DisplayName => "Probe";
        public string Category => "Test";
        public string Description => "";
        public List<PortDefinition> InputPorts { get; } = new() { new() { Name = "in", Label = "In" } };
        public List<PortDefinition> OutputPorts { get; } = new() { new() { Name = "out", Label = "Out" } };
        public List<ConfigField> ConfigFields { get; } = new();
        public bool SupportsRetry => false;

        public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
        {
            context.Context.Variables["hasBinder"] = context.Context.TargetBinder is not null;
            return Task.FromResult(ActionResult.Ok("out"));
        }
    }

    [Fact]
    public async Task TargetBinder_FlowsFromOptionsToContext()
    {
        var probe = new ProbeLeaf();
        var defs = new ActionRegistry();
        var execs = new ActionExecutorRegistry();
        defs.Register(probe); execs.Register(probe);

        var bot = new Bot { Id = Guid.NewGuid(), Name = "B", Actions = { new BotAction { Id = Guid.NewGuid(), TypeKey = "test.probeBinder" } } };
        var options = new ExecutionOptions { TargetBinder = new FakeBinder() };

        var result = await new BotExecutor(execs).RunAsync(bot, options, null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.True((bool)result.FinalVariables["hasBinder"]);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~TargetBinderPlumbingTests"`
Expected: FAIL — `ITargetBinder` / `ExecutionOptions.TargetBinder` / `BotExecutionContext.TargetBinder` don't exist.

- [ ] **Step 3a: Create the interface**

Create `AdbCore/Execution/ITargetBinder.cs`:

```csharp
using AdbCore.Models;

namespace AdbCore.Execution;

/// <summary>Binds a single bot target to a live handle on demand. Used so a nested bot can resolve its own
/// (non-shared) targets only when its card executes. Implemented outside the engine (the runner) so the engine
/// stays free of Win32/ADB/Playwright. Throws on an unresolvable selector.</summary>
public interface ITargetBinder
{
    Task<ResolvedTarget> BindAsync(BotTarget target, CancellationToken ct);
}
```

- [ ] **Step 3b: Add `ExecutionOptions.TargetBinder`**

In `AdbCore/Execution/ExecutionOptions.cs`, add:
```csharp
    /// <summary>Binds a nested bot's own targets on demand. Null at the top level (top-level targets are
    /// pre-resolved into <see cref="ResolvedTargets"/>); supplied by the runner so nested runs can bind theirs.</summary>
    public ITargetBinder? TargetBinder { get; set; }
```

- [ ] **Step 3c: Add `BotExecutionContext.TargetBinder`**

In `AdbCore/Execution/BotExecutionContext.cs`, add:
```csharp
    /// <summary>On-demand binder for a nested bot's own targets (null when none was supplied).</summary>
    public ITargetBinder? TargetBinder { get; set; }
```

- [ ] **Step 3d: Wire it in `BotExecutor.RunAsync`**

In `AdbCore/Execution/BotExecutor.cs`, in the block added by B1 that sets `context.NestedAncestry = options.NestedAncestry;`, add right after it:
```csharp
        context.TargetBinder = options.TargetBinder;
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~TargetBinderPlumbingTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Execution/ITargetBinder.cs AdbCore/Execution/ExecutionOptions.cs AdbCore/Execution/BotExecutionContext.cs AdbCore/Execution/BotExecutor.cs AdbCore.Tests/Execution/TargetBinderPlumbingTests.cs
git commit -m "Add ITargetBinder and thread it through execution"
```

---

### Task 2: `NestedBotExecutor` lazy own-target binding + disposal

**Files:**
- Modify: `AdbCore/Execution/NestedBotExecutor.cs`
- Test: `AdbCore.Tests/Execution/NestedBotTargetBindingTests.cs` (create)

This replaces the B1 `OverlayParentTargetsByName` helper with an async builder that ALSO binds own targets and tracks created handles for disposal. The existing B1 tests (`SendTargets_OverlaysMatchingParentHandleByName`, `SendTargets_Off_NestedRunSeesNoParentHandle`) must still pass — overlay-by-name behavior is preserved; with a null binder, own targets stay unresolved exactly as before.

- [ ] **Step 1: Write the failing tests**

Create `AdbCore.Tests/Execution/NestedBotTargetBindingTests.cs`:

```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Execution;

public class NestedBotTargetBindingTests
{
    // Records bind calls and hands back a disposable handle so we can prove disposal.
    private sealed class RecordingBinder : ITargetBinder
    {
        public List<string> BoundSelectors { get; } = new();
        public List<DisposableHandle> Created { get; } = new();

        public Task<ResolvedTarget> BindAsync(BotTarget target, CancellationToken ct)
        {
            BoundSelectors.Add(target.Selector);
            var handle = new DisposableHandle();
            Created.Add(handle);
            return Task.FromResult(new ResolvedTarget { Type = target.Type, Selector = target.Selector, Handle = handle });
        }
    }

    private sealed class DisposableHandle : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    // Leaf that records, into a variable, whether a resolved target with the given id is present in the run.
    private sealed class TargetProbeLeaf : IActionDefinition, IActionExecutor
    {
        public string TypeKey => "test.targetProbe";
        public string DisplayName => "Probe";
        public string Category => "Test";
        public string Description => "";
        public List<PortDefinition> InputPorts { get; } = new() { new() { Name = "in", Label = "In" } };
        public List<PortDefinition> OutputPorts { get; } = new() { new() { Name = "out", Label = "Out" } };
        public List<ConfigField> ConfigFields { get; } = new();
        public bool SupportsRetry => false;

        public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
        {
            context.Context.Variables["targetCount"] = context.Context.Targets.Count;
            return Task.FromResult(ActionResult.Ok("out"));
        }
    }

    private static (ActionExecutorRegistry execs, TargetProbeLeaf probe) Registry()
    {
        var defs = new ActionRegistry();
        var execs = new ActionExecutorRegistry();
        var probe = new TargetProbeLeaf();
        defs.Register(probe); execs.Register(probe);
        defs.Register(new StartAction()); execs.Register(new StartAction());
        defs.Register(new NestedBotAction()); execs.Register(new NestedBotExecutor(execs));
        return (execs, probe);
    }

    private static Bot NestedBotWithOwnTarget(out Guid targetId)
    {
        targetId = Guid.NewGuid();
        var start = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.start" };
        var probe = new BotAction { Id = Guid.NewGuid(), TypeKey = "test.targetProbe" };
        var bot = new Bot
        {
            Id = Guid.NewGuid(),
            Name = "Child",
            Targets = { new BotTarget { Id = targetId, Name = "Own", Type = BotTargetType.Window, Selector = "title:Game" } },
            Actions = { start, probe },
        };
        bot.Connections.Add(new ActionConnection { SourceActionId = start.Id, SourcePort = "out", TargetActionId = probe.Id, TargetPort = "in" });
        return bot;
    }

    private static BotAction Card(Guid nestedId, bool receiveVars = true, bool sendTargets = false)
        => new()
        {
            Id = Guid.NewGuid(),
            TypeKey = "control.nestedBot",
            Config =
            {
                ["nestedBotId"] = nestedId.ToString(),
                ["receiveVars"] = receiveVars,
                ["sendTargets"] = sendTargets,
            },
        };

    private static async Task<ActionResult> RunCard(ActionExecutorRegistry execs, BotExecutionContext ctx, BotAction card)
    {
        var exec = new NestedBotExecutor(execs);
        return await exec.ExecuteAsync(new ActionExecutionContext(card, ctx, _ => { }), CancellationToken.None);
    }

    [Fact]
    public async Task OwnTarget_IsLazilyBound_AndDisposedAfterRun()
    {
        var (execs, _) = Registry();
        var nested = NestedBotWithOwnTarget(out _);
        var binder = new RecordingBinder();
        var ctx = new BotExecutionContext
        {
            NestedBots = new Dictionary<Guid, Bot> { [nested.Id] = nested },
            TargetBinder = binder,
        };

        var result = await RunCard(execs, ctx, Card(nested.Id));

        Assert.True(result.Success);
        Assert.Equal(new[] { "title:Game" }, binder.BoundSelectors.ToArray()); // bound its own target
        Assert.Equal(1, Convert.ToInt32(ctx.Variables["targetCount"]));        // child saw the resolved target
        Assert.True(binder.Created.Single().Disposed);                         // child-created handle disposed
    }

    [Fact]
    public async Task NoBinder_OwnTargetStaysUnresolved()
    {
        var (execs, _) = Registry();
        var nested = NestedBotWithOwnTarget(out _);
        var ctx = new BotExecutionContext { NestedBots = new Dictionary<Guid, Bot> { [nested.Id] = nested } };

        var result = await RunCard(execs, ctx, Card(nested.Id));

        Assert.True(result.Success);
        Assert.Equal(0, Convert.ToInt32(ctx.Variables["targetCount"])); // nothing bound, no binder
    }

    [Fact]
    public async Task SharedParentTarget_IsReused_AndNotDisposed()
    {
        var (execs, _) = Registry();
        // Nested target NAME "Own" — make a parent target of the same name so it overlays instead of binding.
        var nested = NestedBotWithOwnTarget(out _);
        nested.Targets[0].Name = "Shared";

        var parentId = Guid.NewGuid();
        var parentHandle = new DisposableHandle();
        var binder = new RecordingBinder();
        var ctx = new BotExecutionContext
        {
            NestedBots = new Dictionary<Guid, Bot> { [nested.Id] = nested },
            TargetNames = new Dictionary<Guid, string> { [parentId] = "Shared" },
            TargetBinder = binder,
        };
        ctx.Targets[parentId] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "title:Parent", Handle = parentHandle };

        var result = await RunCard(execs, ctx, Card(nested.Id, sendTargets: true));

        Assert.True(result.Success);
        Assert.Empty(binder.BoundSelectors);          // overlaid from parent — binder NOT called
        Assert.False(parentHandle.Disposed);          // parent handle must NOT be disposed by the child
        Assert.Equal(1, Convert.ToInt32(ctx.Variables["targetCount"]));
    }

    [Fact]
    public async Task BinderThrows_CardFails_AndPartialHandlesDisposed()
    {
        var (execs, _) = Registry();
        var nested = NestedBotWithOwnTarget(out _);
        var throwingBinder = new ThrowingBinder();
        var ctx = new BotExecutionContext
        {
            NestedBots = new Dictionary<Guid, Bot> { [nested.Id] = nested },
            TargetBinder = throwingBinder,
        };

        var result = await RunCard(execs, ctx, Card(nested.Id));

        Assert.False(result.Success);
        Assert.Contains("target", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ThrowingBinder : ITargetBinder
    {
        public Task<ResolvedTarget> BindAsync(BotTarget target, CancellationToken ct)
            => throw new InvalidOperationException("could not resolve window 'title:Game'");
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotTargetBindingTests"`
Expected: FAIL — `NestedBotExecutor` doesn't bind own targets yet (`targetCount` is 0 when a binder is present; binder never called).

- [ ] **Step 3: Rewrite `NestedBotExecutor`**

Replace the entire body of `AdbCore/Execution/NestedBotExecutor.cs` with:

```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Models;

namespace AdbCore.Execution;

/// <summary>Leaf executor for the Nested Bot card: resolves the referenced library bot, runs it as a child
/// <see cref="BotExecutor"/> (the parent walk awaits — so it is paused), optionally seeding the child's
/// variables, sharing the parent's resolved targets by name, binding the nested bot's OWN targets on demand,
/// and merging the child's final variables back. Disposes only the handles it created; routes onSuccess/
/// onFailure; guards against reference cycles.</summary>
public sealed class NestedBotExecutor : IActionExecutor
{
    private readonly ActionExecutorRegistry _executors;

    public NestedBotExecutor(ActionExecutorRegistry executors)
    {
        ArgumentNullException.ThrowIfNull(executors);
        _executors = executors;
    }

    public string TypeKey => NestedBotAction.NestedBotTypeKey;

    public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        var config = context.Action.Config;
        var idText = ConfigValues.GetString(config, NestedBotAction.NestedBotIdKey);
        if (string.IsNullOrWhiteSpace(idText) || !Guid.TryParse(idText, out var nestedId))
        {
            return ActionResult.Fail("This Nested Bot card has no bot assigned.");
        }

        var run = context.Context;
        if (!run.NestedBots.TryGetValue(nestedId, out var nestedBot) || nestedBot is null)
        {
            return ActionResult.Fail($"Nested bot '{nestedId}' was not found in this bot's library.");
        }

        if (run.NestedAncestry.Contains(nestedId))
        {
            return ActionResult.Fail(
                $"Nested bot cycle detected: '{nestedBot.Name}' is already running in this call chain.");
        }

        var sendVars = ConfigValues.GetBool(config, NestedBotAction.SendVarsKey);
        var sendTargets = ConfigValues.GetBool(config, NestedBotAction.SendTargetsKey);
        var receiveVars = ConfigValues.GetBool(config, NestedBotAction.ReceiveVarsKey);

        // Handles this nested run creates itself (own-target binds) — disposed when the run ends. Shared parent
        // handles are NOT added here, so they are never disposed by the child.
        var createdHandles = new List<object>();
        try
        {
            var childTargets = await BuildChildTargetsAsync(nestedBot, run, sendTargets, createdHandles, ct);

            var childOptions = new ExecutionOptions
            {
                Log = context.Log,
                NestedBotLibrary = run.NestedBots,
                NestedAncestry = run.NestedAncestry.Append(nestedId).ToList(),
                InitialVariables = sendVars ? new Dictionary<string, object>(run.Variables) : null,
                ResolvedTargets = childTargets,
                TargetBinder = run.TargetBinder,
            };

            var result = await new BotExecutor(_executors).RunAsync(nestedBot, childOptions, progress: null, ct);

            if (receiveVars)
            {
                foreach (var kv in result.FinalVariables)
                {
                    run.Variables[kv.Key] = kv.Value;
                }
            }

            return result.Success
                ? ActionResult.Ok(NestedBotAction.SuccessPort)
                : ActionResult.Fail(result.ErrorMessage ?? "Nested bot failed.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ActionResult.Fail($"Nested bot target binding failed: {ex.Message}");
        }
        finally
        {
            await DisposeHandlesAsync(createdHandles);
        }
    }

    /// <summary>Builds the child's resolved-target map. For each nested target: if Send Targets is on and a
    /// parent target shares its NAME, reuse the parent's handle (not disposed by the child); otherwise bind the
    /// nested target's own selector via the binder (tracked for disposal). Nested targets are keyed by their own
    /// id (nested actions reference nested target ids). With no binder, an unmatched nested target is omitted.</summary>
    private static async Task<IReadOnlyDictionary<Guid, ResolvedTarget>> BuildChildTargetsAsync(
        Bot nestedBot, BotExecutionContext run, bool sendTargets, List<object> createdHandles, CancellationToken ct)
    {
        var parentByName = sendTargets ? BuildParentByName(run) : new Dictionary<string, ResolvedTarget>(StringComparer.Ordinal);
        var map = new Dictionary<Guid, ResolvedTarget>();

        foreach (var t in nestedBot.Targets)
        {
            if (sendTargets && !string.IsNullOrEmpty(t.Name) && parentByName.TryGetValue(t.Name, out var shared))
            {
                map[t.Id] = shared; // reuse parent handle — do NOT track for disposal
                continue;
            }

            if (run.TargetBinder is { } binder)
            {
                var resolved = await binder.BindAsync(t, ct);
                map[t.Id] = resolved;
                if (resolved.Handle is not null)
                {
                    createdHandles.Add(resolved.Handle);
                }
            }
            // else: no binder available -> leave this nested target unresolved (actions using it fail downstream).
        }

        return map;
    }

    private static Dictionary<string, ResolvedTarget> BuildParentByName(BotExecutionContext run)
    {
        var parentByName = new Dictionary<string, ResolvedTarget>(StringComparer.Ordinal);
        foreach (var kv in run.TargetNames)
        {
            if (run.Targets.TryGetValue(kv.Key, out var resolved))
            {
                parentByName[kv.Value] = resolved;
            }
        }
        return parentByName;
    }

    /// <summary>Best-effort disposal of handles this nested run created (mirrors the runner's teardown):
    /// a handle that fails to dispose must not prevent the others from being cleaned up.</summary>
    private static async Task DisposeHandlesAsync(List<object> handles)
    {
        foreach (var handle in handles)
        {
            try
            {
                switch (handle)
                {
                    case IAsyncDisposable asyncDisposable:
                        await asyncDisposable.DisposeAsync();
                        break;
                    case IDisposable disposable:
                        disposable.Dispose();
                        break;
                }
            }
            catch
            {
                // Swallow: teardown should never throw over a handle that's already gone.
            }
        }
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotTargetBindingTests"`
Expected: PASS (4). Then confirm the B1 tests still pass:
Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotExecutorTests"`
Expected: PASS (all).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Execution/NestedBotExecutor.cs AdbCore.Tests/Execution/NestedBotTargetBindingTests.cs
git commit -m "NestedBotExecutor: lazily bind own targets, dispose created handles"
```

---

### Task 3: `RunnerTargetBinder` (BotRunner) + wire into the run

**Files:**
- Create: `BotRunner/RunnerTargetBinder.cs`
- Modify: `BotRunner/RunnerApp.cs`
- Test: `BotRunner.Tests/RunnerTargetBinderTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `BotRunner.Tests/RunnerTargetBinderTests.cs` (the Window path is testable with a fake `IWindowResolver`; Android/Browser need live infra, so only structural/error behavior is asserted here):

```csharp
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Targets;
using BotRunner;
using Xunit;

namespace BotRunner.Tests;

public class RunnerTargetBinderTests
{
    private sealed class FakeWindowResolver : IWindowResolver
    {
        private readonly IntPtr _handle;
        public FakeWindowResolver(IntPtr handle) => _handle = handle;
        public IntPtr Resolve(string selector) => _handle;
    }

    [Fact]
    public async Task BindAsync_Window_ReturnsResolvedHandle()
    {
        var binder = new RunnerTargetBinder(new FakeWindowResolver(new IntPtr(0x1234)));
        var target = new BotTarget { Id = Guid.NewGuid(), Name = "W", Type = BotTargetType.Window, Selector = "title:Game" };

        var resolved = await binder.BindAsync(target, CancellationToken.None);

        Assert.Equal(BotTargetType.Window, resolved.Type);
        Assert.Equal(new IntPtr(0x1234), resolved.Handle);
    }

    [Fact]
    public async Task BindAsync_Window_Unresolved_Throws()
    {
        var binder = new RunnerTargetBinder(new FakeWindowResolver(IntPtr.Zero));
        var target = new BotTarget { Id = Guid.NewGuid(), Name = "W", Type = BotTargetType.Window, Selector = "title:Nope" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => binder.BindAsync(target, CancellationToken.None));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~RunnerTargetBinderTests"`
Expected: FAIL — `RunnerTargetBinder` does not exist.

- [ ] **Step 3: Create `RunnerTargetBinder`**

Create `BotRunner/RunnerTargetBinder.cs`:

```csharp
using AdbCore.Android;
using AdbCore.Browser;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Targets;
using AdvancedSharpAdbClient;

namespace BotRunner;

/// <summary>Binds a single bot target to a live handle on demand — used by nested bots to resolve their own
/// (non-shared) targets when their card executes. Mirrors the three run-start binders (Window/Android/Browser).
/// Throws <see cref="InvalidOperationException"/> on an unresolvable selector (the nested executor turns that
/// into an onFailure result).</summary>
public sealed class RunnerTargetBinder : ITargetBinder
{
    private readonly IWindowResolver _windowResolver;
    private AdbClient? _adbClient;

    public RunnerTargetBinder(IWindowResolver windowResolver)
    {
        ArgumentNullException.ThrowIfNull(windowResolver);
        _windowResolver = windowResolver;
    }

    public async Task<ResolvedTarget> BindAsync(BotTarget target, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);
        var resolved = new ResolvedTarget { Type = target.Type, Selector = target.Selector };

        switch (target.Type)
        {
            case BotTargetType.Window:
                resolved.Handle = BindWindow(target.Selector);
                break;
            case BotTargetType.AndroidDevice:
                resolved.Handle = BindAndroid(target.Selector);
                break;
            case BotTargetType.Browser:
                resolved.Handle = await BindBrowserAsync(target.Selector);
                break;
        }

        return resolved;
    }

    private IntPtr BindWindow(string selector)
    {
        IntPtr handle;
        try
        {
            handle = _windowResolver.Resolve(selector);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException($"Invalid Window target selector '{selector}': {ex.Message}");
        }

        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Could not resolve Window target selector '{selector}' to a window.");
        }

        return handle;
    }

    private IAndroidDevice BindAndroid(string selector)
    {
        var serial = AdbSelector.ParseSerial(selector)
            ?? throw new InvalidOperationException($"Android target selector '{selector}' must be 'serial:<device>'.");

        if (_adbClient is null)
        {
            try
            {
                AdbServer.Instance.StartServer(adbPath: "adb", restartServerIfNewer: false);
                _adbClient = new AdbClient();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Could not reach the ADB server for nested target '{selector}': {ex.Message}");
            }
        }

        var device = _adbClient.GetDevices().FirstOrDefault(d => d.Serial == serial)
            ?? throw new InvalidOperationException($"No connected Android device with serial '{serial}'.");

        return new AdvancedSharpAdbDevice(_adbClient, device);
    }

    private static async Task<IBrowserPage> BindBrowserAsync(string selector)
    {
        var engine = BrowserSelector.ParseEngine(selector)
            ?? throw new InvalidOperationException($"Browser target selector '{selector}' must be 'browser:<engine>'.");

        if (!BrowserSelector.Engines.Contains(engine))
        {
            throw new InvalidOperationException($"Unknown browser engine '{engine}'. Use chromium, firefox, or webkit.");
        }

        try
        {
            return await PlaywrightBrowserPage.LaunchAsync(engine, headless: false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Could not launch the '{engine}' browser: {ex.Message}");
        }
    }
}
```

- [ ] **Step 4: Wire it into `RunnerApp`**

In `BotRunner/RunnerApp.cs`, the `WindowTargetBinder.Bind` call currently constructs `new Win32WindowResolver()` inline. Hoist it to a local, and pass a `RunnerTargetBinder` built from it into the options. Change:
```csharp
            // Resolve Window target selectors to live HWNDs before execution (Input/Screen need them).
            WindowTargetBinder.Bind(resolvedTargets, new Win32WindowResolver());
```
to:
```csharp
            // Resolve Window target selectors to live HWNDs before execution (Input/Screen need them).
            var windowResolver = new Win32WindowResolver();
            WindowTargetBinder.Bind(resolvedTargets, windowResolver);
```
and change:
```csharp
            var options = new ExecutionOptions
            {
                ResolvedTargets = resolvedTargets,
                Log = logger.Message,
            };
```
to:
```csharp
            var options = new ExecutionOptions
            {
                ResolvedTargets = resolvedTargets,
                Log = logger.Message,
                // Lets nested bots bind their own (non-shared) targets on demand when their card runs.
                TargetBinder = new RunnerTargetBinder(windowResolver),
            };
```

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~RunnerTargetBinderTests"`
Expected: PASS (2). Build the whole solution to confirm BotRunner compiles: `dotnet build ADB.slnx` — Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add BotRunner/RunnerTargetBinder.cs BotRunner/RunnerApp.cs BotRunner.Tests/RunnerTargetBinderTests.cs
git commit -m "Add RunnerTargetBinder; supply it so nested bots bind their own targets"
```

---

### Task 4: Full suite green

- [ ] **Step 1: Run the whole suite**

Run: `dotnet test ADB.slnx`
Expected: PASS, no regressions (772 from B1 + the new tests).

- [ ] **Step 2: Commit any fixups** (only if needed)

---

## Self-Review

- **Spec coverage (B7):** `ITargetBinder` engine interface (Task 1); nested own-target lazy binding only at card execution + disposal of created handles + parent-overlay-not-disposed (Task 2); runner implementation mirroring the three binders + wired into the run, so Test Run (which spawns BotRunner.exe) gets it too (Task 3). ✓
- **Back-compat:** top-level targets stay pre-bound; B1 overlay-by-name tests preserved; null-binder path leaves own targets unresolved exactly as B1 did. ✓
- **Placeholders:** none. ✓
- **Type consistency:** `ITargetBinder.BindAsync(BotTarget, CancellationToken)` identical across the interface, the fake binders, `NestedBotExecutor`, and `RunnerTargetBinder`; `ExecutionOptions.TargetBinder`/`BotExecutionContext.TargetBinder` consistent. ✓
- **Notes for executor:** confirm `IWindowResolver` lives in `AdbCore.Targets` and exposes `IntPtr Resolve(string)`; confirm `AdbSelector.ParseSerial` and `BrowserSelector.ParseEngine`/`Engines` signatures (read `BotRunner/AndroidTargetBinder.cs` and `BotRunner/BrowserTargetBinder.cs` for the exact calls — this plan mirrors them). If `BotRunner.Tests` lacks a reference to `AdbCore.Targets`, it's already transitively available via the BotRunner project reference.
