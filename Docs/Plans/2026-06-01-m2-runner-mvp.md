# M2 — Runner MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A console `BotRunner` that loads a `.bot` file and executes it end-to-end via a new sequential execution engine in `AdbCore`, resolving targets from CLI args, emitting JSON-lines logs, and returning meaningful exit codes.

**Architecture:** `AdbCore` gains an `Execution` namespace — a `BotExecutor` that walks the action graph sequentially from the entry point, following output ports, with halt-on-failure (+ optional `onFailure` routing) and per-action retry. Action *behaviour* is supplied by `IActionExecutor`s resolved through an `ActionExecutorRegistry` (mirroring M1's `ActionRegistry`). A minimal built-in action set (Start, End, Log) implements both `IActionDefinition` and `IActionExecutor`. A new `BotRunner` console project wires parsing → target resolution → execution → logging → exit code, with the orchestration extracted into testable classes.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), `System.Text.Json` (BCL), xUnit. Builds on merged M1 (`AdbCore.Models`, `AdbCore.Serialization`, `AdbCore.Actions`). Design reference: `Docs/Design/V1.md` §4.2, §4.3, §4.4, §6.

---

## Scope

In scope (design §4.2, §4.4, §6, milestone M2):
- Execution engine + runtime contracts in `AdbCore.Execution`.
- `ActionExecutorRegistry` (TypeKey → `IActionExecutor`).
- Built-in actions: **Start, End, Log** only (definition + executor each), registered via `BuiltInActions`.
- `BotRunner` console app: `--bot`, `--target Name=selector` (repeatable), `--log-level`, `--log-file`; loads `.bot`, resolves targets, runs the engine, writes JSON-lines logs to stdout + a file, returns exit codes (0 ok, 1 run failed, 2 usage error).

Explicitly **out of scope** for M2 (do NOT build):
- Parallel execution (`Run Parallel` / `Join`) — M5.
- Any action beyond Start/End/Log (Branch, Loop, Delay, Set Variable, Screen, Input, Android, Browser, etc.) — M5/M7.
- Real target *handle* resolution (opening live HWND/ADB/Playwright handles) — M7. M2 only validates `--target` presence and captures the selector string into `ResolvedTarget`.
- DI container wiring (`Microsoft.Extensions.DependencyInjection`) — deferred; executors are resolved via `ActionExecutorRegistry`.
- The `BotBuilder` / `BotCapture` projects.

## Naming note

The design (§4.2) calls the run-wide context `ExecutionContext`. That name collides with `System.Threading.ExecutionContext` (pulled in by `ImplicitUsings`), causing ambiguity in any file that also uses `System.Threading`. To avoid that landmine this plan names the type **`BotExecutionContext`**. `ActionExecutionContext` (§4.3) keeps its name (no collision).

## File Structure

```
AdbCore/
  Execution/
    ResolvedTarget.cs          # a target resolved to a selector (live handle deferred to M7)
    BotExecutionContext.cs     # run-wide Variables + Targets
    ActionResult.cs            # outcome of one action (+ Ok/Fail factories)
    ActionExecutionContext.cs  # what an executor receives (Action, Context, Log)
    IActionExecutor.cs         # runtime behaviour contract (TypeKey + ExecuteAsync)
    ActionExecutorRegistry.cs  # TypeKey -> IActionExecutor
    ExecutionOptions.cs        # StopOnError, ResolvedTargets, Log sink
    ExecutionProgress.cs       # per-action progress report
    ExecutionResult.cs         # overall run outcome
    BotExecutor.cs             # the engine
  Actions/
    BuiltIn/
      StartAction.cs           # control.start  (IActionDefinition + IActionExecutor)
      EndAction.cs             # control.end
      LogAction.cs             # data.log
      BuiltInActions.cs        # registers all built-ins into both registries
BotRunner/
  BotRunner.csproj             # console exe, net10.0-windows, refs AdbCore
  CommandLineException.cs      # usage error -> exit 2
  LogLevel.cs                  # Debug | Info | Warn | Error
  CommandLineArgs.cs           # parse + hold CLI args
  TargetResolver.cs            # bot targets + selectors -> ResolvedTarget map
  LogEntry.cs                  # one JSON-lines log record
  RunLogger.cs                 # writes JSON lines to stdout + file, level-filtered
  RunnerApp.cs                 # orchestration (load -> resolve -> run -> log)
  Cli.cs                       # arg parse + exit-code mapping (testable entry)
  Program.cs                   # Main -> Cli.RunAsync(Console.Out/Error)
AdbCore.Tests/
  Execution/ActionExecutorRegistryTests.cs
  Execution/ActionResultTests.cs
  Execution/BotExecutorTests.cs
  Execution/FakeExecutor.cs    # test double IActionExecutor
  Actions/BuiltIn/BuiltInActionsTests.cs
BotRunner.Tests/
  BotRunner.Tests.csproj       # xunit, net10.0-windows, refs BotRunner + AdbCore
  CommandLineArgsTests.cs
  TargetResolverTests.cs
  CliIntegrationTests.cs       # end-to-end .bot run via Cli.RunAsync
```

---

### Task 1: Execution contracts + ActionExecutorRegistry

Adds the runtime types under `AdbCore/Execution/` and the executor registry. No engine yet.

**Files:**
- Create: `AdbCore/Execution/ResolvedTarget.cs`, `BotExecutionContext.cs`, `ActionResult.cs`, `ActionExecutionContext.cs`, `IActionExecutor.cs`, `ActionExecutorRegistry.cs`, `ExecutionOptions.cs`, `ExecutionProgress.cs`, `ExecutionResult.cs`
- Test: `AdbCore.Tests/Execution/ActionExecutorRegistryTests.cs`, `AdbCore.Tests/Execution/ActionResultTests.cs`, `AdbCore.Tests/Execution/FakeExecutor.cs`

- [ ] **Step 1: Write the failing tests + test double**

Create `AdbCore.Tests/Execution/FakeExecutor.cs`:
```csharp
using AdbCore.Execution;

namespace AdbCore.Tests.Execution;

/// <summary>Test double executor with a configurable TypeKey and result.</summary>
internal sealed class FakeExecutor : IActionExecutor
{
    public required string TypeKey { get; init; }
    public Func<ActionExecutionContext, ActionResult> Behavior { get; init; } = _ => ActionResult.Ok("out");
    public int Calls { get; private set; }

    public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(Behavior(context));
    }
}
```

Create `AdbCore.Tests/Execution/ActionResultTests.cs`:
```csharp
using AdbCore.Execution;
using Xunit;

namespace AdbCore.Tests.Execution;

public class ActionResultTests
{
    [Fact]
    public void Ok_SetsSuccessAndPort()
    {
        var r = ActionResult.Ok("out");

        Assert.True(r.Success);
        Assert.Equal("out", r.OutputPort);
        Assert.Null(r.ErrorMessage);
        Assert.NotNull(r.Outputs);
    }

    [Fact]
    public void Fail_SetsErrorAndNotSuccess()
    {
        var r = ActionResult.Fail("boom");

        Assert.False(r.Success);
        Assert.Equal("boom", r.ErrorMessage);
    }
}
```

Create `AdbCore.Tests/Execution/ActionExecutorRegistryTests.cs`:
```csharp
using AdbCore.Execution;
using Xunit;

namespace AdbCore.Tests.Execution;

public class ActionExecutorRegistryTests
{
    [Fact]
    public void Register_ThenGet_ReturnsSameInstance()
    {
        var registry = new ActionExecutorRegistry();
        var exec = new FakeExecutor { TypeKey = "test.alpha" };

        registry.Register(exec);

        Assert.Same(exec, registry.Get("test.alpha"));
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void TryGet_UnknownKey_ReturnsFalseAndNull()
    {
        var registry = new ActionExecutorRegistry();

        var found = registry.TryGet("nope", out var exec);

        Assert.False(found);
        Assert.Null(exec);
    }

    [Fact]
    public void Get_UnknownKey_Throws()
    {
        var registry = new ActionExecutorRegistry();

        Assert.Throws<KeyNotFoundException>(() => registry.Get("nope"));
    }

    [Fact]
    public void Register_DuplicateKey_Throws()
    {
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "dup" });

        var ex = Assert.Throws<InvalidOperationException>(
            () => registry.Register(new FakeExecutor { TypeKey = "dup" }));
        Assert.Contains("dup", ex.Message);
    }

    [Fact]
    public void Register_Null_Throws()
    {
        var registry = new ActionExecutorRegistry();

        Assert.Throws<ArgumentNullException>(() => registry.Register(null!));
    }
}
```

- [ ] **Step 2: Run tests to verify they FAIL**

Run: `dotnet test`
Expected: build FAILS — `IActionExecutor`, `ActionResult`, `ActionExecutorRegistry`, `ActionExecutionContext` don't exist.

- [ ] **Step 3: Create the execution contract types**

`AdbCore/Execution/ResolvedTarget.cs`:
```csharp
using AdbCore.Models;

namespace AdbCore.Execution;

/// <summary>A bot target resolved at run start. In M2 only the selector is captured;
/// opening a live handle (HWND / ADB device / Playwright page) is deferred to M7.</summary>
public class ResolvedTarget
{
    public BotTargetType Type { get; set; }

    /// <summary>The raw selector provided at runtime, e.g. "process:BlueStacks".</summary>
    public string Selector { get; set; } = string.Empty;

    /// <summary>The live handle, populated in later milestones. Null in M2.</summary>
    public object? Handle { get; set; }
}
```

`AdbCore/Execution/BotExecutionContext.cs`:
```csharp
namespace AdbCore.Execution;

/// <summary>Run-wide state that flows through an entire bot execution.</summary>
public class BotExecutionContext
{
    /// <summary>Variables read/written by actions, keyed by name.</summary>
    public Dictionary<string, object> Variables { get; } = new();

    /// <summary>Targets resolved at run start, keyed by <c>BotTarget.Id</c>.</summary>
    public Dictionary<Guid, ResolvedTarget> Targets { get; } = new();
}
```

`AdbCore/Execution/ActionResult.cs`:
```csharp
namespace AdbCore.Execution;

/// <summary>The outcome of executing a single action.</summary>
public class ActionResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>The output port to follow next, e.g. "out", "onSuccess", "true". Empty = terminal.</summary>
    public string OutputPort { get; set; } = string.Empty;

    /// <summary>Optional named outputs produced by the action.</summary>
    public Dictionary<string, object> Outputs { get; set; } = new();

    public static ActionResult Ok(string outputPort) => new() { Success = true, OutputPort = outputPort };

    public static ActionResult Fail(string errorMessage) => new() { Success = false, ErrorMessage = errorMessage };
}
```

`AdbCore/Execution/ActionExecutionContext.cs`:
```csharp
using AdbCore.Models;

namespace AdbCore.Execution;

/// <summary>Everything an <see cref="IActionExecutor"/> needs to run one action.</summary>
public class ActionExecutionContext
{
    public ActionExecutionContext(BotAction action, BotExecutionContext context, Action<string> log)
    {
        Action = action;
        Context = context;
        Log = log;
    }

    /// <summary>The action node being executed (its config, target, retry).</summary>
    public BotAction Action { get; }

    /// <summary>The run-wide context (variables, resolved targets).</summary>
    public BotExecutionContext Context { get; }

    /// <summary>Emits a message to the run log sink.</summary>
    public Action<string> Log { get; }
}
```

`AdbCore/Execution/IActionExecutor.cs`:
```csharp
namespace AdbCore.Execution;

/// <summary>The runtime behaviour of an action type. Resolved by <see cref="ActionExecutorRegistry"/>.</summary>
public interface IActionExecutor
{
    /// <summary>Unique key matching the corresponding <c>IActionDefinition.TypeKey</c>.</summary>
    string TypeKey { get; }

    Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct);
}
```

`AdbCore/Execution/ActionExecutorRegistry.cs`:
```csharp
namespace AdbCore.Execution;

/// <summary>Catalogue of action executors, keyed by <see cref="IActionExecutor.TypeKey"/>.</summary>
public class ActionExecutorRegistry
{
    private readonly Dictionary<string, IActionExecutor> _byKey = new(StringComparer.Ordinal);

    public int Count => _byKey.Count;

    public IReadOnlyCollection<IActionExecutor> All => _byKey.Values;

    public void Register(IActionExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);

        if (!_byKey.TryAdd(executor.TypeKey, executor))
        {
            throw new InvalidOperationException(
                $"An executor with TypeKey '{executor.TypeKey}' is already registered.");
        }
    }

    public bool TryGet(string typeKey, out IActionExecutor? executor)
        => _byKey.TryGetValue(typeKey, out executor);

    public IActionExecutor Get(string typeKey)
    {
        if (!_byKey.TryGetValue(typeKey, out var executor))
        {
            throw new KeyNotFoundException($"No executor registered with TypeKey '{typeKey}'.");
        }

        return executor;
    }
}
```

`AdbCore/Execution/ExecutionOptions.cs`:
```csharp
namespace AdbCore.Execution;

/// <summary>Options controlling a single bot run.</summary>
public class ExecutionOptions
{
    /// <summary>Halt the run when an action fails and no <c>onFailure</c> port is wired. Default true.</summary>
    public bool StopOnError { get; set; } = true;

    /// <summary>Targets resolved before the run, keyed by <c>BotTarget.Id</c>.</summary>
    public IReadOnlyDictionary<Guid, ResolvedTarget> ResolvedTargets { get; set; }
        = new Dictionary<Guid, ResolvedTarget>();

    /// <summary>Sink for messages emitted by actions (e.g. the Log action). Optional.</summary>
    public Action<string>? Log { get; set; }
}
```

`AdbCore/Execution/ExecutionProgress.cs`:
```csharp
namespace AdbCore.Execution;

/// <summary>Reported once per action as the engine executes it.</summary>
public class ExecutionProgress
{
    public Guid ActionId { get; set; }
    public string ActionLabel { get; set; } = string.Empty;
    public string TypeKey { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
```

`AdbCore/Execution/ExecutionResult.cs`:
```csharp
namespace AdbCore.Execution;

/// <summary>The overall outcome of a bot run.</summary>
public class ExecutionResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public Guid? FailedActionId { get; set; }
    public int ActionsExecuted { get; set; }
}
```

- [ ] **Step 4: Run tests to verify they PASS**

Run: `dotnet test`
Expected: all tests pass (19 prior + 7 new = 26), 0 failures.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): add execution contracts and executor registry"
```

---

### Task 2: BotExecutor engine

The sequential graph-walking engine. Driven by `FakeExecutor` test doubles.

**Files:**
- Create: `AdbCore/Execution/BotExecutor.cs`
- Test: `AdbCore.Tests/Execution/BotExecutorTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `AdbCore.Tests/Execution/BotExecutorTests.cs`:
```csharp
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Execution;

public class BotExecutorTests
{
    private static BotAction Node(string typeKey, out Guid id)
    {
        id = Guid.NewGuid();
        return new BotAction { Id = id, TypeKey = typeKey, Label = typeKey };
    }

    private static ActionConnection Edge(Guid from, string port, Guid to)
        => new() { Id = Guid.NewGuid(), SourceActionId = from, SourcePort = port, TargetActionId = to, TargetPort = "in" };

    [Fact]
    public async Task RunAsync_LinearPath_ExecutesAllInOrderAndSucceeds()
    {
        var start = Node("a", out var startId);
        var mid = Node("b", out var midId);
        var end = Node("c", out var endId);
        var bot = new Bot { Name = "linear" };
        bot.Actions.AddRange(new[] { start, mid, end });
        bot.Connections.Add(Edge(startId, "out", midId));
        bot.Connections.Add(Edge(midId, "out", endId));

        var order = new List<string>();
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "a", Behavior = c => { order.Add("a"); return ActionResult.Ok("out"); } });
        registry.Register(new FakeExecutor { TypeKey = "b", Behavior = c => { order.Add("b"); return ActionResult.Ok("out"); } });
        registry.Register(new FakeExecutor { TypeKey = "c", Behavior = c => { order.Add("c"); return ActionResult.Ok(string.Empty); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);
        Assert.Equal(3, result.ActionsExecuted);
        Assert.Equal(new[] { "a", "b", "c" }, order);
    }

    [Fact]
    public async Task RunAsync_FollowsNamedOutputPort()
    {
        var branch = Node("branch", out var branchId);
        var yes = Node("yes", out var yesId);
        var no = Node("no", out var noId);
        var bot = new Bot { Name = "ports" };
        bot.Actions.AddRange(new[] { branch, yes, no });
        bot.Connections.Add(Edge(branchId, "true", yesId));
        bot.Connections.Add(Edge(branchId, "false", noId));

        var taken = "";
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "branch", Behavior = c => ActionResult.Ok("true") });
        registry.Register(new FakeExecutor { TypeKey = "yes", Behavior = c => { taken = "yes"; return ActionResult.Ok(string.Empty); } });
        registry.Register(new FakeExecutor { TypeKey = "no", Behavior = c => { taken = "no"; return ActionResult.Ok(string.Empty); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);
        Assert.Equal("yes", taken);
    }

    [Fact]
    public async Task RunAsync_MissingExecutor_FailsGracefully()
    {
        var only = Node("ghost", out _);
        var bot = new Bot { Name = "missing" };
        bot.Actions.Add(only);

        var result = await new BotExecutor(new ActionExecutorRegistry()).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.False(result.Success);
        Assert.Contains("ghost", result.ErrorMessage);
    }

    [Fact]
    public async Task RunAsync_NoEntryPoint_Fails()
    {
        var a = Node("a", out var aId);
        var b = Node("b", out var bId);
        var bot = new Bot { Name = "cycle-ish" };
        bot.Actions.AddRange(new[] { a, b });
        // both have incoming edges -> no entry point
        bot.Connections.Add(Edge(aId, "out", bId));
        bot.Connections.Add(Edge(bId, "out", aId));

        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "a" });
        registry.Register(new FakeExecutor { TypeKey = "b" });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.False(result.Success);
        Assert.Contains("entry point", result.ErrorMessage);
    }

    [Fact]
    public async Task RunAsync_FailureWithNoFailurePort_HaltsByDefault()
    {
        var start = Node("start", out var startId);
        var boom = Node("boom", out var boomId);
        var never = Node("never", out var neverId);
        var bot = new Bot { Name = "halt" };
        bot.Actions.AddRange(new[] { start, boom, never });
        bot.Connections.Add(Edge(startId, "out", boomId));
        bot.Connections.Add(Edge(boomId, "out", neverId));

        var reachedNever = false;
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "start", Behavior = c => ActionResult.Ok("out") });
        registry.Register(new FakeExecutor { TypeKey = "boom", Behavior = c => ActionResult.Fail("kaboom") });
        registry.Register(new FakeExecutor { TypeKey = "never", Behavior = c => { reachedNever = true; return ActionResult.Ok(string.Empty); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.False(result.Success);
        Assert.Equal("kaboom", result.ErrorMessage);
        Assert.Equal(boomId, result.FailedActionId);
        Assert.False(reachedNever);
    }

    [Fact]
    public async Task RunAsync_FailureWithFailurePort_FollowsIt()
    {
        var boom = Node("boom", out var boomId);
        var handler = Node("handler", out var handlerId);
        var bot = new Bot { Name = "recover" };
        bot.Actions.AddRange(new[] { boom, handler });
        bot.Connections.Add(Edge(boomId, "onFailure", handlerId));

        var recovered = false;
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "boom", Behavior = c => ActionResult.Fail("kaboom") });
        registry.Register(new FakeExecutor { TypeKey = "handler", Behavior = c => { recovered = true; return ActionResult.Ok(string.Empty); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);
        Assert.True(recovered);
    }

    [Fact]
    public async Task RunAsync_RetriesFailingActionUpToMaxAttempts()
    {
        var flaky = Node("flaky", out var flakyId);
        flaky.Retry = new RetryPolicy { MaxAttempts = 3, DelayMs = 0 };
        var bot = new Bot { Name = "retry" };
        bot.Actions.Add(flaky);

        var attempts = 0;
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor
        {
            TypeKey = "flaky",
            Behavior = c => { attempts++; return attempts < 3 ? ActionResult.Fail("not yet") : ActionResult.Ok(string.Empty); },
        });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task RunAsync_ReportsProgressPerAction()
    {
        var start = Node("start", out var startId);
        var end = Node("end", out var endId);
        var bot = new Bot { Name = "progress" };
        bot.Actions.AddRange(new[] { start, end });
        bot.Connections.Add(Edge(startId, "out", endId));

        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor { TypeKey = "start", Behavior = c => ActionResult.Ok("out") });
        registry.Register(new FakeExecutor { TypeKey = "end", Behavior = c => ActionResult.Ok(string.Empty) });

        var reports = new List<ExecutionProgress>();
        var progress = new InlineTestProgress(reports.Add);

        await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), progress, default);

        Assert.Equal(2, reports.Count);
        Assert.All(reports, r => Assert.True(r.Success));
    }

    private sealed class InlineTestProgress : IProgress<ExecutionProgress>
    {
        private readonly Action<ExecutionProgress> _h;
        public InlineTestProgress(Action<ExecutionProgress> h) => _h = h;
        public void Report(ExecutionProgress value) => _h(value);
    }
}
```

- [ ] **Step 2: Run tests to verify they FAIL**

Run: `dotnet test`
Expected: build FAILS — `BotExecutor` does not exist.

- [ ] **Step 3: Implement the engine**

Create `AdbCore/Execution/BotExecutor.cs`:
```csharp
using AdbCore.Models;

namespace AdbCore.Execution;

/// <summary>Walks a bot's action graph sequentially from its entry point, executing each action
/// and following the output port returned by its executor. Halts on failure unless an
/// <c>onFailure</c> port is wired or <see cref="ExecutionOptions.StopOnError"/> is false.</summary>
public class BotExecutor
{
    private const string FailurePort = "onFailure";

    private readonly ActionExecutorRegistry _executors;

    public BotExecutor(ActionExecutorRegistry executors)
    {
        ArgumentNullException.ThrowIfNull(executors);
        _executors = executors;
    }

    public async Task<ExecutionResult> RunAsync(
        Bot bot,
        ExecutionOptions options,
        IProgress<ExecutionProgress>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(bot);
        ArgumentNullException.ThrowIfNull(options);

        var context = new BotExecutionContext();
        foreach (var kvp in options.ResolvedTargets)
        {
            context.Targets[kvp.Key] = kvp.Value;
        }

        var log = options.Log ?? (_ => { });

        var current = FindEntryPoint(bot);
        if (current is null)
        {
            return new ExecutionResult
            {
                Success = false,
                ErrorMessage = "No entry point: every action has an incoming connection.",
            };
        }

        var executed = 0;
        while (current is not null)
        {
            ct.ThrowIfCancellationRequested();

            if (!_executors.TryGet(current.TypeKey, out var executor) || executor is null)
            {
                return new ExecutionResult
                {
                    Success = false,
                    ErrorMessage = $"No executor registered for TypeKey '{current.TypeKey}'.",
                    FailedActionId = current.Id,
                    ActionsExecuted = executed,
                };
            }

            var result = await ExecuteWithRetryAsync(executor, current, context, log, ct);
            executed++;

            progress?.Report(new ExecutionProgress
            {
                ActionId = current.Id,
                ActionLabel = current.Label,
                TypeKey = current.TypeKey,
                Success = result.Success,
                ErrorMessage = result.ErrorMessage,
            });

            if (!result.Success)
            {
                var failureNext = FindNext(bot, current.Id, FailurePort);
                if (failureNext is not null)
                {
                    current = failureNext;
                    continue;
                }

                return new ExecutionResult
                {
                    Success = false,
                    ErrorMessage = result.ErrorMessage,
                    FailedActionId = current.Id,
                    ActionsExecuted = executed,
                };
            }

            current = FindNext(bot, current.Id, result.OutputPort);
        }

        return new ExecutionResult { Success = true, ActionsExecuted = executed };
    }

    private async Task<ActionResult> ExecuteWithRetryAsync(
        IActionExecutor executor,
        BotAction action,
        BotExecutionContext context,
        Action<string> log,
        CancellationToken ct)
    {
        var attempts = action.Retry?.MaxAttempts ?? 1;
        if (attempts < 1)
        {
            attempts = 1;
        }

        var delayMs = action.Retry?.DelayMs ?? 0;
        var result = ActionResult.Fail("Action did not execute.");

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (attempt > 0 && delayMs > 0)
            {
                await Task.Delay(delayMs, ct);
            }

            try
            {
                var actionContext = new ActionExecutionContext(action, context, log);
                result = await executor.ExecuteAsync(actionContext, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result = ActionResult.Fail(ex.Message);
            }

            if (result.Success)
            {
                return result;
            }
        }

        return result;
    }

    private static BotAction? FindEntryPoint(Bot bot)
    {
        var withIncoming = bot.Connections.Select(c => c.TargetActionId).ToHashSet();
        return bot.Actions.FirstOrDefault(a => !withIncoming.Contains(a.Id));
    }

    private static BotAction? FindNext(Bot bot, Guid fromActionId, string sourcePort)
    {
        var edge = bot.Connections.FirstOrDefault(
            c => c.SourceActionId == fromActionId && c.SourcePort == sourcePort);
        return edge is null ? null : bot.Actions.FirstOrDefault(a => a.Id == edge.TargetActionId);
    }
}
```

- [ ] **Step 4: Run tests to verify they PASS**

Run: `dotnet test`
Expected: all tests pass (26 prior + 8 new = 34), 0 failures.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): add sequential bot execution engine"
```

---

### Task 3: Built-in actions (Start, End, Log)

Each built-in implements **both** `IActionDefinition` (metadata) and `IActionExecutor` (behaviour) as a single stateless class, registered into both registries by `BuiltInActions`.

**Files:**
- Create: `AdbCore/Actions/BuiltIn/StartAction.cs`, `EndAction.cs`, `LogAction.cs`, `BuiltInActions.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs`:
```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class BuiltInActionsTests
{
    private static ActionExecutionContext Ctx(BotAction action, Action<string> log)
        => new(action, new BotExecutionContext(), log);

    [Fact]
    public void Register_AddsAllBuiltInsToBothRegistries()
    {
        var defs = new ActionRegistry();
        var execs = new ActionExecutorRegistry();

        BuiltInActions.Register(defs, execs);

        foreach (var key in new[] { "control.start", "control.end", "data.log" })
        {
            Assert.True(defs.TryGet(key, out _));
            Assert.True(execs.TryGet(key, out _));
        }
        Assert.Equal(3, defs.Count);
        Assert.Equal(3, execs.Count);
    }

    [Fact]
    public async Task Start_ReturnsOutPort()
    {
        var result = await new StartAction().ExecuteAsync(Ctx(new BotAction(), _ => { }), default);

        Assert.True(result.Success);
        Assert.Equal("out", result.OutputPort);
    }

    [Fact]
    public async Task End_IsTerminal()
    {
        var result = await new EndAction().ExecuteAsync(Ctx(new BotAction(), _ => { }), default);

        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.OutputPort);
    }

    [Fact]
    public async Task Log_EmitsConfiguredMessage_AndContinues()
    {
        var action = new BotAction { TypeKey = "data.log" };
        action.Config[LogAction.MessageKey] = "hello world";
        var captured = new List<string>();

        var result = await new LogAction().ExecuteAsync(Ctx(action, captured.Add), default);

        Assert.True(result.Success);
        Assert.Equal("out", result.OutputPort);
        Assert.Equal(new[] { "hello world" }, captured);
    }

    [Fact]
    public async Task Log_MissingMessage_EmitsEmptyString()
    {
        var captured = new List<string>();

        await new LogAction().ExecuteAsync(Ctx(new BotAction { TypeKey = "data.log" }, captured.Add), default);

        Assert.Equal(new[] { string.Empty }, captured);
    }
}
```

- [ ] **Step 2: Run tests to verify they FAIL**

Run: `dotnet test`
Expected: build FAILS — `StartAction`, `EndAction`, `LogAction`, `BuiltInActions` don't exist.

- [ ] **Step 3: Implement the built-ins**

`AdbCore/Actions/BuiltIn/StartAction.cs`:
```csharp
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn;

/// <summary>The entry point of a bot. Has a single output port and does nothing but proceed.</summary>
public sealed class StartAction : IActionDefinition, IActionExecutor
{
    public string TypeKey => "control.start";
    public string DisplayName => "Start";
    public string Category => "Control Flow";
    public string Description => "Entry point of the bot.";
    public List<PortDefinition> InputPorts { get; } = new();
    public List<PortDefinition> OutputPorts { get; } = new() { new PortDefinition { Name = "out", Label = "Out" } };
    public List<ConfigField> ConfigFields { get; } = new();
    public bool SupportsRetry => false;

    public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
        => Task.FromResult(ActionResult.Ok("out"));
}
```

`AdbCore/Actions/BuiltIn/EndAction.cs`:
```csharp
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Terminates the bot run. Has a single input port and no output (terminal).</summary>
public sealed class EndAction : IActionDefinition, IActionExecutor
{
    public string TypeKey => "control.end";
    public string DisplayName => "End";
    public string Category => "Control Flow";
    public string Description => "Terminates the bot run.";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public List<PortDefinition> OutputPorts { get; } = new();
    public List<ConfigField> ConfigFields { get; } = new();
    public bool SupportsRetry => false;

    public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
        => Task.FromResult(ActionResult.Ok(string.Empty));
}
```

`AdbCore/Actions/BuiltIn/LogAction.cs`:
```csharp
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Writes a configured message to the run log, then continues.</summary>
public sealed class LogAction : IActionDefinition, IActionExecutor
{
    /// <summary>Config key holding the message to log.</summary>
    public const string MessageKey = "message";

    public string TypeKey => "data.log";
    public string DisplayName => "Log";
    public string Category => "Data";
    public string Description => "Writes a message to the run log.";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public List<PortDefinition> OutputPorts { get; } = new() { new PortDefinition { Name = "out", Label = "Out" } };
    public List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = MessageKey, Label = "Message", Type = ConfigFieldType.String },
    };
    public bool SupportsRetry => false;

    public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        var message = context.Action.Config.TryGetValue(MessageKey, out var value)
            ? value?.ToString() ?? string.Empty
            : string.Empty;

        context.Log(message);
        return Task.FromResult(ActionResult.Ok("out"));
    }
}
```

`AdbCore/Actions/BuiltIn/BuiltInActions.cs`:
```csharp
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Registers the built-in action set into the definition and executor registries.</summary>
public static class BuiltInActions
{
    public static void Register(ActionRegistry definitions, ActionExecutorRegistry executors)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(executors);

        Add(new StartAction(), definitions, executors);
        Add(new EndAction(), definitions, executors);
        Add(new LogAction(), definitions, executors);
    }

    private static void Add<T>(T action, ActionRegistry definitions, ActionExecutorRegistry executors)
        where T : IActionDefinition, IActionExecutor
    {
        definitions.Register(action);
        executors.Register(action);
    }
}
```

- [ ] **Step 4: Run tests to verify they PASS**

Run: `dotnet test`
Expected: all tests pass (34 prior + 5 new = 39), 0 failures.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): add Start, End, Log built-in actions"
```

---

### Task 4: BotRunner project + CLI parsing + target resolution

Scaffolds the console project and implements the pure, testable pieces: argument parsing and target resolution.

**Files:**
- Create: `BotRunner/BotRunner.csproj`, `BotRunner/CommandLineException.cs`, `BotRunner/LogLevel.cs`, `BotRunner/CommandLineArgs.cs`, `BotRunner/TargetResolver.cs`
- Create: `BotRunner.Tests/BotRunner.Tests.csproj`, `BotRunner.Tests/CommandLineArgsTests.cs`, `BotRunner.Tests/TargetResolverTests.cs`
- Modify: `ADB.slnx` (add both new projects)

- [ ] **Step 1: Scaffold the projects**

Run from the worktree root:
```bash
dotnet new console -o BotRunner
dotnet new xunit -o BotRunner.Tests
dotnet sln ADB.slnx add BotRunner/BotRunner.csproj BotRunner.Tests/BotRunner.Tests.csproj
dotnet add BotRunner/BotRunner.csproj reference AdbCore/AdbCore.csproj
dotnet add BotRunner.Tests/BotRunner.Tests.csproj reference BotRunner/BotRunner.csproj
dotnet add BotRunner.Tests/BotRunner.Tests.csproj reference AdbCore/AdbCore.csproj
```

Overwrite `BotRunner/BotRunner.csproj` with exactly:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>BotRunner</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\AdbCore\AdbCore.csproj" />
  </ItemGroup>

</Project>
```

In `BotRunner.Tests/BotRunner.Tests.csproj`, change `<TargetFramework>` to `net10.0-windows` (leave template package versions as-is; keep the two ProjectReferences added above). Delete the template `BotRunner.Tests/UnitTest1.cs`. Delete the default `BotRunner/Program.cs` content for now by replacing it with a minimal placeholder (it is fully implemented in Task 5):
```csharp
// Program entry point is implemented in Task 5.
return 0;
```

- [ ] **Step 2: Write the failing tests**

Create `BotRunner.Tests/CommandLineArgsTests.cs`:
```csharp
using BotRunner;
using Xunit;

namespace BotRunner.Tests;

public class CommandLineArgsTests
{
    [Fact]
    public void Parse_BotPath_IsCaptured()
    {
        var args = CommandLineArgs.Parse(new[] { "--bot", @"C:\bots\farm.bot" });

        Assert.Equal(@"C:\bots\farm.bot", args.BotPath);
        Assert.Equal(LogLevel.Info, args.LogLevel);
        Assert.Empty(args.Targets);
    }

    [Fact]
    public void Parse_MissingBot_Throws()
    {
        Assert.Throws<CommandLineException>(() => CommandLineArgs.Parse(Array.Empty<string>()));
    }

    [Fact]
    public void Parse_Targets_AreSplitOnFirstEquals()
    {
        var args = CommandLineArgs.Parse(new[]
        {
            "--bot", "b.bot",
            "--target", "Client 1=process:BlueStacks",
            "--target", "My Phone=serial:emulator-5554",
        });

        Assert.Equal("process:BlueStacks", args.Targets["Client 1"]);
        Assert.Equal("serial:emulator-5554", args.Targets["My Phone"]);
    }

    [Fact]
    public void Parse_TargetWithoutEquals_Throws()
    {
        Assert.Throws<CommandLineException>(
            () => CommandLineArgs.Parse(new[] { "--bot", "b.bot", "--target", "bogus" }));
    }

    [Fact]
    public void Parse_LogLevel_IsCaseInsensitive()
    {
        var args = CommandLineArgs.Parse(new[] { "--bot", "b.bot", "--log-level", "DEBUG" });

        Assert.Equal(LogLevel.Debug, args.LogLevel);
    }

    [Fact]
    public void Parse_UnknownLogLevel_Throws()
    {
        Assert.Throws<CommandLineException>(
            () => CommandLineArgs.Parse(new[] { "--bot", "b.bot", "--log-level", "loud" }));
    }

    [Fact]
    public void Parse_UnknownArgument_Throws()
    {
        Assert.Throws<CommandLineException>(
            () => CommandLineArgs.Parse(new[] { "--bot", "b.bot", "--wat" }));
    }

    [Fact]
    public void Parse_FlagWithoutValue_Throws()
    {
        Assert.Throws<CommandLineException>(() => CommandLineArgs.Parse(new[] { "--bot" }));
    }
}
```

Create `BotRunner.Tests/TargetResolverTests.cs`:
```csharp
using AdbCore.Models;
using BotRunner;
using Xunit;

namespace BotRunner.Tests;

public class TargetResolverTests
{
    [Fact]
    public void Resolve_MapsSelectorsByTargetName()
    {
        var bot = new Bot();
        var id = Guid.NewGuid();
        bot.Targets.Add(new BotTarget { Id = id, Name = "Client 1", Type = BotTargetType.Window });
        var selectors = new Dictionary<string, string> { ["Client 1"] = "process:BlueStacks" };

        var resolved = TargetResolver.Resolve(bot, selectors);

        Assert.Equal("process:BlueStacks", resolved[id].Selector);
        Assert.Equal(BotTargetType.Window, resolved[id].Type);
    }

    [Fact]
    public void Resolve_DeclaredTargetWithoutSelector_Throws()
    {
        var bot = new Bot();
        bot.Targets.Add(new BotTarget { Id = Guid.NewGuid(), Name = "My Phone", Type = BotTargetType.AndroidDevice });

        var ex = Assert.Throws<CommandLineException>(
            () => TargetResolver.Resolve(bot, new Dictionary<string, string>()));
        Assert.Contains("My Phone", ex.Message);
    }

    [Fact]
    public void Resolve_NoTargets_ReturnsEmpty()
    {
        var resolved = TargetResolver.Resolve(new Bot(), new Dictionary<string, string>());

        Assert.Empty(resolved);
    }
}
```

- [ ] **Step 3: Run tests to verify they FAIL**

Run: `dotnet test`
Expected: build FAILS — `CommandLineArgs`, `CommandLineException`, `LogLevel`, `TargetResolver` don't exist.

- [ ] **Step 4: Implement parsing and resolution**

`BotRunner/CommandLineException.cs`:
```csharp
namespace BotRunner;

/// <summary>Raised for invalid CLI usage; maps to exit code 2.</summary>
public sealed class CommandLineException : Exception
{
    public CommandLineException(string message) : base(message) { }
}
```

`BotRunner/LogLevel.cs`:
```csharp
namespace BotRunner;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error,
}
```

`BotRunner/CommandLineArgs.cs`:
```csharp
namespace BotRunner;

/// <summary>Parsed command-line arguments for the runner.</summary>
public sealed class CommandLineArgs
{
    public string BotPath { get; init; } = string.Empty;
    public Dictionary<string, string> Targets { get; init; } = new(StringComparer.Ordinal);
    public LogLevel LogLevel { get; init; } = LogLevel.Info;
    public string? LogFile { get; init; }

    public static CommandLineArgs Parse(string[] args)
    {
        string? botPath = null;
        var targets = new Dictionary<string, string>(StringComparer.Ordinal);
        var logLevel = LogLevel.Info;
        string? logFile = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--bot":
                    botPath = RequireValue(args, ref i, "--bot");
                    break;

                case "--target":
                    var target = RequireValue(args, ref i, "--target");
                    var eq = target.IndexOf('=');
                    if (eq <= 0)
                    {
                        throw new CommandLineException(
                            $"--target must be in the form Name=selector, got '{target}'.");
                    }
                    targets[target[..eq]] = target[(eq + 1)..];
                    break;

                case "--log-level":
                    var level = RequireValue(args, ref i, "--log-level");
                    if (!Enum.TryParse<LogLevel>(level, ignoreCase: true, out var parsed))
                    {
                        throw new CommandLineException(
                            $"Unknown --log-level '{level}'. Use debug, info, warn, or error.");
                    }
                    logLevel = parsed;
                    break;

                case "--log-file":
                    logFile = RequireValue(args, ref i, "--log-file");
                    break;

                default:
                    throw new CommandLineException($"Unknown argument '{args[i]}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(botPath))
        {
            throw new CommandLineException("--bot <path> is required.");
        }

        return new CommandLineArgs
        {
            BotPath = botPath,
            Targets = targets,
            LogLevel = logLevel,
            LogFile = logFile,
        };
    }

    private static string RequireValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
        {
            throw new CommandLineException($"{flag} requires a value.");
        }
        return args[++i];
    }
}
```

`BotRunner/TargetResolver.cs`:
```csharp
using AdbCore.Execution;
using AdbCore.Models;

namespace BotRunner;

/// <summary>Resolves the targets declared in a bot against the selectors supplied on the CLI.</summary>
public static class TargetResolver
{
    public static Dictionary<Guid, ResolvedTarget> Resolve(Bot bot, IReadOnlyDictionary<string, string> selectors)
    {
        var resolved = new Dictionary<Guid, ResolvedTarget>();

        foreach (var target in bot.Targets)
        {
            if (!selectors.TryGetValue(target.Name, out var selector))
            {
                throw new CommandLineException(
                    $"Target '{target.Name}' declared in the bot has no matching --target argument.");
            }

            resolved[target.Id] = new ResolvedTarget { Type = target.Type, Selector = selector };
        }

        return resolved;
    }
}
```

- [ ] **Step 5: Run tests to verify they PASS**

Run: `dotnet test`
Expected: all tests pass (39 prior in AdbCore.Tests + 11 new in BotRunner.Tests = 50 total across both test projects), 0 failures.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(runner): scaffold BotRunner with CLI parsing and target resolution"
```

---

### Task 5: Runner orchestration, JSON-lines logging, and entry point

Wires everything together: load → resolve → execute → log → exit code, with an end-to-end integration test driving a real `.bot` file.

**Files:**
- Create: `BotRunner/LogEntry.cs`, `BotRunner/RunLogger.cs`, `BotRunner/RunnerApp.cs`, `BotRunner/Cli.cs`
- Modify: `BotRunner/Program.cs` (replace the Task 4 placeholder)
- Test: `BotRunner.Tests/CliIntegrationTests.cs`

- [ ] **Step 1: Write the failing integration tests**

Create `BotRunner.Tests/CliIntegrationTests.cs`:
```csharp
using System.Text.Json.Nodes;
using AdbCore.Models;
using AdbCore.Serialization;
using BotRunner;
using Xunit;

namespace BotRunner.Tests;

public class CliIntegrationTests
{
    private static string WriteBot(Bot bot)
    {
        var path = Path.Combine(Path.GetTempPath(), $"adb-m2-{Guid.NewGuid():N}.bot");
        new BotSerializer().Save(bot, path);
        return path;
    }

    private static Bot StartLogEndBot(string message)
    {
        var startId = Guid.NewGuid();
        var logId = Guid.NewGuid();
        var endId = Guid.NewGuid();
        var bot = new Bot { Name = "hello" };
        bot.Actions.Add(new BotAction { Id = startId, TypeKey = "control.start", Label = "Start" });
        var log = new BotAction { Id = logId, TypeKey = "data.log", Label = "Log" };
        log.Config["message"] = message;
        bot.Actions.Add(log);
        bot.Actions.Add(new BotAction { Id = endId, TypeKey = "control.end", Label = "End" });
        bot.Connections.Add(new ActionConnection { Id = Guid.NewGuid(), SourceActionId = startId, SourcePort = "out", TargetActionId = logId, TargetPort = "in" });
        bot.Connections.Add(new ActionConnection { Id = Guid.NewGuid(), SourceActionId = logId, SourcePort = "out", TargetActionId = endId, TargetPort = "in" });
        return bot;
    }

    [Fact]
    public async Task RunAsync_SimpleBot_Succeeds_LogsMessage_ExitsZero()
    {
        var botPath = WriteBot(StartLogEndBot("hello from M2"));
        var logPath = Path.ChangeExtension(botPath, ".log");
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        try
        {
            var exit = await Cli.RunAsync(new[] { "--bot", botPath }, stdout, stderr, default);

            Assert.Equal(0, exit);
            var logText = await File.ReadAllTextAsync(logPath);
            Assert.Contains("hello from M2", logText);
            // every log line is valid JSON
            foreach (var line in logText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                Assert.NotNull(JsonNode.Parse(line));
            }
            Assert.Contains("run-end", logText);
        }
        finally
        {
            File.Delete(botPath);
            if (File.Exists(logPath)) File.Delete(logPath);
        }
    }

    [Fact]
    public async Task RunAsync_MissingBotFile_ExitsTwo()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = await Cli.RunAsync(new[] { "--bot", @"C:\nope\does-not-exist.bot" }, stdout, stderr, default);

        Assert.Equal(2, exit);
        Assert.Contains("not found", stderr.ToString());
    }

    [Fact]
    public async Task RunAsync_DeclaredTargetMissingArg_ExitsTwo()
    {
        var bot = StartLogEndBot("hi");
        bot.Targets.Add(new BotTarget { Id = Guid.NewGuid(), Name = "Client 1", Type = BotTargetType.Window });
        var botPath = WriteBot(bot);
        var logPath = Path.ChangeExtension(botPath, ".log");
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        try
        {
            var exit = await Cli.RunAsync(new[] { "--bot", botPath }, stdout, stderr, default);

            Assert.Equal(2, exit);
            Assert.Contains("Client 1", stderr.ToString());
        }
        finally
        {
            File.Delete(botPath);
            if (File.Exists(logPath)) File.Delete(logPath);
        }
    }

    [Fact]
    public async Task RunAsync_BadArguments_ExitsTwo()
    {
        var exit = await Cli.RunAsync(Array.Empty<string>(), new StringWriter(), new StringWriter(), default);

        Assert.Equal(2, exit);
    }
}
```

- [ ] **Step 2: Run tests to verify they FAIL**

Run: `dotnet test`
Expected: build FAILS — `Cli`, `RunnerApp`, `RunLogger`, `LogEntry` don't exist.

- [ ] **Step 3: Implement logging, orchestration, and the entry point**

`BotRunner/LogEntry.cs`:
```csharp
using System.Text.Json.Serialization;

namespace BotRunner;

/// <summary>One JSON-lines log record. Null fields are omitted on serialization.</summary>
public sealed class LogEntry
{
    public string Ts { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public string Event { get; set; } = string.Empty;

    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("bot")] public string? Bot { get; set; }
    [JsonPropertyName("actionId")] public string? ActionId { get; set; }
    [JsonPropertyName("label")] public string? Label { get; set; }
    [JsonPropertyName("typeKey")] public string? TypeKey { get; set; }
    [JsonPropertyName("success")] public bool? Success { get; set; }
    [JsonPropertyName("actionsExecuted")] public int? ActionsExecuted { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
}
```

`BotRunner/RunLogger.cs`:
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using AdbCore.Execution;

namespace BotRunner;

/// <summary>Writes JSON-lines log records to stdout and a file, filtered by minimum level.</summary>
public sealed class RunLogger
{
    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly TextWriter _stdout;
    private readonly TextWriter _file;
    private readonly LogLevel _minLevel;

    public RunLogger(TextWriter stdout, TextWriter file, LogLevel minLevel)
    {
        _stdout = stdout;
        _file = file;
        _minLevel = minLevel;
    }

    public void RunStart(string botName)
        => Write(LogLevel.Info, new LogEntry { Event = "run-start", Bot = botName });

    public void ActionExecuted(ExecutionProgress p)
        => Write(p.Success ? LogLevel.Info : LogLevel.Error, new LogEntry
        {
            Event = "action",
            ActionId = p.ActionId.ToString(),
            Label = p.ActionLabel,
            TypeKey = p.TypeKey,
            Success = p.Success,
            Error = p.ErrorMessage,
        });

    public void Message(string text)
        => Write(LogLevel.Info, new LogEntry { Event = "log", Message = text });

    public void RunEnd(ExecutionResult r)
        => Write(LogLevel.Info, new LogEntry
        {
            Event = "run-end",
            Success = r.Success,
            ActionsExecuted = r.ActionsExecuted,
            Error = r.ErrorMessage,
        });

    private void Write(LogLevel level, LogEntry entry)
    {
        if (level < _minLevel)
        {
            return;
        }

        entry.Ts = DateTime.UtcNow.ToString("o");
        entry.Level = level.ToString().ToLowerInvariant();

        var line = JsonSerializer.Serialize(entry, Json);
        _stdout.WriteLine(line);
        _file.WriteLine(line);
    }
}
```

`BotRunner/RunnerApp.cs`:
```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Serialization;

namespace BotRunner;

/// <summary>Orchestrates a single bot run: load, resolve targets, execute, log, and produce an exit code.</summary>
public sealed class RunnerApp
{
    /// <summary>Runs the bot. Returns 0 on success, 1 on run failure.
    /// Throws <see cref="CommandLineException"/> for usage problems (caller maps to exit 2).</summary>
    public async Task<int> RunAsync(CommandLineArgs args, TextWriter stdout, CancellationToken ct)
    {
        if (!File.Exists(args.BotPath))
        {
            throw new CommandLineException($"Bot file not found: {args.BotPath}");
        }

        var bot = new BotSerializer().Load(args.BotPath);

        // Throws CommandLineException before any file is opened if a declared target is unmatched.
        var resolvedTargets = TargetResolver.Resolve(bot, args.Targets);

        var logPath = args.LogFile ?? Path.ChangeExtension(args.BotPath, ".log");
        using var fileWriter = new StreamWriter(logPath, append: false);
        var logger = new RunLogger(stdout, fileWriter, args.LogLevel);

        var definitions = new ActionRegistry();
        var executors = new ActionExecutorRegistry();
        BuiltInActions.Register(definitions, executors);

        var options = new ExecutionOptions
        {
            ResolvedTargets = resolvedTargets,
            Log = logger.Message,
        };
        var progress = new InlineProgress<ExecutionProgress>(logger.ActionExecuted);

        logger.RunStart(bot.Name);
        var result = await new BotExecutor(executors).RunAsync(bot, options, progress, ct);
        logger.RunEnd(result);

        return result.Success ? 0 : 1;
    }

    /// <summary>Synchronous <see cref="IProgress{T}"/> so log lines are written in deterministic order.</summary>
    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;
        public InlineProgress(Action<T> handler) => _handler = handler;
        public void Report(T value) => _handler(value);
    }
}
```

`BotRunner/Cli.cs`:
```csharp
namespace BotRunner;

/// <summary>Testable command-line entry point: parses args, runs, and maps exceptions to exit codes.</summary>
public static class Cli
{
    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        try
        {
            var parsed = CommandLineArgs.Parse(args);
            return await new RunnerApp().RunAsync(parsed, stdout, ct);
        }
        catch (CommandLineException ex)
        {
            stderr.WriteLine($"Error: {ex.Message}");
            return 2;
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"Unexpected error: {ex.Message}");
            return 1;
        }
    }
}
```

Replace `BotRunner/Program.cs` entirely with:
```csharp
using BotRunner;

return await Cli.RunAsync(args, Console.Out, Console.Error, CancellationToken.None);
```

- [ ] **Step 4: Run tests to verify they PASS**

Run: `dotnet test`
Expected: all tests pass (39 in AdbCore.Tests + 15 in BotRunner.Tests = 54 total), 0 failures.

- [ ] **Step 5: Smoke-test the real executable**

Run (PowerShell):
```
dotnet run --project BotRunner -- --bot "Docs/Plans/sample-not-needed"
```
Expected: exits non-zero with `Error: Bot file not found:` on stderr. (This just confirms the wired entry point behaves; no assertion needed.)

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(runner): orchestrate end-to-end run with JSON-lines logging and exit codes"
```

---

## Self-Review

**Spec coverage (design §9 M2 + §4.2/§4.4/§6):**
- Console runner executing a simple bot end-to-end — Task 5 (`Cli`/`RunnerApp`) + Tasks 1–3 (engine + built-ins). ✓
- Target resolution via CLI args — Task 4 (`CommandLineArgs`, `TargetResolver`). ✓
- Logging (JSON lines, one entry per action, to stdout + file) — Task 5 (`RunLogger`). ✓
- Exit codes (0 ok / 1 fail / 2 usage) — Task 5 (`Cli` + `RunnerApp` return values). ✓
- Execution engine (§4.4: entry point, sequential, ports, halt-on-failure, retry) — Task 2 (`BotExecutor`). ✓
- Variable/target context (§4.2) — Task 1 (`BotExecutionContext`, `ResolvedTarget`). ✓

**Placeholder scan:** No "TBD"/"handle edge cases"/"similar to". The only deliberate placeholder is `BotRunner/Program.cs` in Task 4, which is fully replaced in Task 5. ✓

**Type consistency:** `IActionExecutor` (TypeKey + ExecuteAsync), `ActionResult` (Ok/Fail), `ActionExecutionContext` (Action/Context/Log), `BotExecutionContext`, `ResolvedTarget` (Type/Selector/Handle), `ExecutionOptions` (StopOnError/ResolvedTargets/Log), `ExecutionProgress`, `ExecutionResult` defined in Task 1 are used unchanged in Tasks 2, 3, 5. Built-in TypeKeys `control.start`/`control.end`/`data.log` match across Task 3 and the Task 5 integration bot. `CommandLineException` (Task 4) is the type caught in `Cli` (Task 5). ✓

**Scope check:** No parallel execution, no actions beyond Start/End/Log, no live handle resolution, no DI container, no BotBuilder/BotCapture. `ResolvedTarget.Handle` exists but is unused/null in M2 (forward-compat for M7). ✓

**Naming-collision check:** Run-wide context is `BotExecutionContext`, avoiding the `System.Threading.ExecutionContext` ambiguity. ✓
