# Nested-bot logging Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every action inside a nested bot emit a prefixed execution-trace line to the run log during an F5 Test Run, composing for deep nesting.

**Architecture:** `NestedBotExecutor` currently runs the child bot with `progress: null`, so nested actions are silent. Wrap the child's `Log` sink with a `[<NestedBotName>] ` prefix and feed the child a synchronous `IProgress<ExecutionProgress>` adapter that formats each executed action into a prefixed message line routed to that same sink. Because each nesting level wraps the sink it was handed, prefixes compose automatically (`[Outer] [Inner] ✓ …`).

**Tech Stack:** C# / .NET 10, xUnit, AdbCore execution engine.

**Spec:** `Docs/superpowers/specs/2026-07-03-nested-logging-and-error-handler-design.md` (Feature 1).

---

### Task 1: Nested runs forward a prefixed per-action trace to the log

**Files:**
- Modify: `AdbCore/Execution/NestedBotExecutor.cs`
- Test: `AdbCore.Tests/Execution/NestedBotLoggingTests.cs` (create)

Reference for house style: `AdbCore.Tests/Execution/NestedBotExecutorTests.cs` (fakes, `NestedBot` builder, `RunCard` helper) and `BotRunner/RunnerApp.cs` (`InlineProgress<T>` — the synchronous progress pattern to mirror; do NOT use `System.Progress<T>`, which posts asynchronously and reorders log lines).

- [ ] **Step 1: Write the failing tests**

Create `AdbCore.Tests/Execution/NestedBotLoggingTests.cs`:

```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Execution;

public class NestedBotLoggingTests
{
    // Leaf that (a) optionally fails, (b) optionally emits a Log-sink message from config "log".
    private sealed class FakeLeaf : IActionDefinition, IActionExecutor
    {
        public string TypeKey => "test.leaf";
        public string DisplayName => "Leaf";
        public string Category => "Test";
        public string Description => "";
        public List<PortDefinition> InputPorts { get; } = new() { new() { Name = "in", Label = "In" } };
        public List<PortDefinition> OutputPorts { get; } = new() { new() { Name = "out", Label = "Out" } };
        public List<ConfigField> ConfigFields { get; } = new();
        public bool SupportsRetry => false;

        public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
        {
            var msg = ConfigValues.GetString(context.Action.Config, "log");
            if (!string.IsNullOrEmpty(msg)) context.Log(msg);
            if (ConfigValues.GetBool(context.Action.Config, "fail")) return Task.FromResult(ActionResult.Fail("boom"));
            return Task.FromResult(ActionResult.Ok("out"));
        }
    }

    private static ActionExecutorRegistry Registry()
    {
        var defs = new ActionRegistry();
        var execs = new ActionExecutorRegistry();
        var leaf = new FakeLeaf();
        defs.Register(leaf); execs.Register(leaf);
        defs.Register(new StartAction()); execs.Register(new StartAction());
        defs.Register(new NestedBotAction());
        execs.Register(new NestedBotExecutor(execs));
        return execs;
    }

    // Start -> leaf(label, optional fail / optional log message).
    private static Bot NestedBot(string name, string leafLabel, bool fail = false, string? logMessage = null)
    {
        var start = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.start" };
        var leaf = new BotAction { Id = Guid.NewGuid(), TypeKey = "test.leaf", Label = leafLabel };
        if (fail) leaf.Config["fail"] = true;
        if (logMessage is not null) leaf.Config["log"] = logMessage;
        var bot = new Bot { Id = Guid.NewGuid(), Name = name, Actions = { start, leaf } };
        bot.Connections.Add(new ActionConnection { SourceActionId = start.Id, SourcePort = "out", TargetActionId = leaf.Id, TargetPort = "in" });
        return bot;
    }

    private static BotAction Card(Guid nestedBotId) => new()
    {
        Id = Guid.NewGuid(),
        TypeKey = "control.nestedBot",
        Config = { ["nestedBotId"] = nestedBotId.ToString() },
    };

    private static async Task<List<string>> RunCardCapturingLog(ActionExecutorRegistry execs, BotExecutionContext ctx, BotAction card)
    {
        var log = new List<string>();
        var exec = new NestedBotExecutor(execs);
        await exec.ExecuteAsync(new ActionExecutionContext(card, ctx, log.Add), CancellationToken.None);
        return log;
    }

    [Fact]
    public async Task NestedAction_EmitsPrefixedSuccessLine()
    {
        var execs = Registry();
        var nested = NestedBot("Login", "Click username");
        var ctx = new BotExecutionContext { NestedBots = new Dictionary<Guid, Bot> { [nested.Id] = nested } };

        var log = await RunCardCapturingLog(execs, ctx, Card(nested.Id));

        Assert.Contains("[Login] ✓ Click username", log);
    }

    [Fact]
    public async Task NestedAction_Failure_EmitsPrefixedCrossLineWithError()
    {
        var execs = Registry();
        var nested = NestedBot("Login", "Click submit", fail: true);
        var ctx = new BotExecutionContext { NestedBots = new Dictionary<Guid, Bot> { [nested.Id] = nested } };

        var log = await RunCardCapturingLog(execs, ctx, Card(nested.Id));

        Assert.Contains("[Login] ✗ Click submit: boom", log);
    }

    [Fact]
    public async Task ExplicitLogAction_InsideNestedBot_IsPrefixed()
    {
        var execs = Registry();
        var nested = NestedBot("Login", "Say", logMessage: "logging in");
        var ctx = new BotExecutionContext { NestedBots = new Dictionary<Guid, Bot> { [nested.Id] = nested } };

        var log = await RunCardCapturingLog(execs, ctx, Card(nested.Id));

        Assert.Contains("[Login] logging in", log);
    }

    [Fact]
    public async Task BlankNestedBotName_FallsBackToNestedPrefix()
    {
        var execs = Registry();
        var nested = NestedBot("", "Do thing");
        var ctx = new BotExecutionContext { NestedBots = new Dictionary<Guid, Bot> { [nested.Id] = nested } };

        var log = await RunCardCapturingLog(execs, ctx, Card(nested.Id));

        Assert.Contains("[Nested] ✓ Do thing", log);
    }

    [Fact]
    public async Task DeepNesting_ComposesPrefixes()
    {
        // outer(top) -> card(A); A -> card(B); B -> leaf. Running outer top-level, B's leaf line is [A] [B] prefixed.
        var execs = Registry();

        var botB = NestedBot("B", "deep click");

        var bStart = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.start" };
        var bCard = Card(botB.Id);
        var botA = new Bot { Id = Guid.NewGuid(), Name = "A", Actions = { bStart, bCard } };
        botA.Connections.Add(new ActionConnection { SourceActionId = bStart.Id, SourcePort = "out", TargetActionId = bCard.Id, TargetPort = "in" });

        var oStart = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.start" };
        var aCard = Card(botA.Id);
        var outer = new Bot { Id = Guid.NewGuid(), Name = "Outer", Actions = { oStart, aCard } };
        outer.Connections.Add(new ActionConnection { SourceActionId = oStart.Id, SourcePort = "out", TargetActionId = aCard.Id, TargetPort = "in" });

        var library = new Dictionary<Guid, Bot> { [botA.Id] = botA, [botB.Id] = botB };
        var log = new List<string>();
        var options = new ExecutionOptions { NestedBotLibrary = library, Log = log.Add };

        var result = await new BotExecutor(execs).RunAsync(outer, options, progress: null, CancellationToken.None);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Contains("[A] [B] ✓ deep click", log);
    }

    [Fact]
    public async Task LineOrdering_IsDeterministic()
    {
        // Two leaves in sequence produce lines in execution order (synchronous progress).
        var execs = Registry();
        var start = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.start" };
        var first = new BotAction { Id = Guid.NewGuid(), TypeKey = "test.leaf", Label = "first" };
        var second = new BotAction { Id = Guid.NewGuid(), TypeKey = "test.leaf", Label = "second" };
        var nested = new Bot { Id = Guid.NewGuid(), Name = "Seq", Actions = { start, first, second } };
        nested.Connections.Add(new ActionConnection { SourceActionId = start.Id, SourcePort = "out", TargetActionId = first.Id, TargetPort = "in" });
        nested.Connections.Add(new ActionConnection { SourceActionId = first.Id, SourcePort = "out", TargetActionId = second.Id, TargetPort = "in" });
        var ctx = new BotExecutionContext { NestedBots = new Dictionary<Guid, Bot> { [nested.Id] = nested } };

        var log = await RunCardCapturingLog(execs, ctx, Card(nested.Id));

        var firstIdx = log.IndexOf("[Seq] ✓ first");
        var secondIdx = log.IndexOf("[Seq] ✓ second");
        Assert.True(firstIdx >= 0 && secondIdx >= 0);
        Assert.True(firstIdx < secondIdx);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotLoggingTests"`
Expected: FAIL — the nested `✓`/`✗` and prefix lines are absent (nested runs pass `progress: null` and an unprefixed `Log`).

- [ ] **Step 3: Implement the prefix + progress adapter in `NestedBotExecutor`**

In `AdbCore/Execution/NestedBotExecutor.cs`, inside `ExecuteAsync`, replace the `childOptions` construction and the `RunAsync` call (currently `progress: null`) with a prefixed sink and a progress adapter. The existing block is:

```csharp
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
```

Replace it with:

```csharp
            // Prefix every line this nested run emits so the log shows which bot it came from. The prefix
            // wraps the sink we were handed, so deeper nesting composes automatically (e.g. "[Outer] [Inner] ").
            var prefix = $"[{(string.IsNullOrWhiteSpace(nestedBot.Name) ? "Nested" : nestedBot.Name)}] ";
            void ChildLog(string message) => context.Log(prefix + message);

            var childOptions = new ExecutionOptions
            {
                Log = ChildLog,
                NestedBotLibrary = run.NestedBots,
                NestedAncestry = run.NestedAncestry.Append(nestedId).ToList(),
                InitialVariables = sendVars ? new Dictionary<string, object>(run.Variables) : null,
                ResolvedTargets = childTargets,
                TargetBinder = run.TargetBinder,
            };

            // Nested actions don't reach the runner's progress channel, so synthesize their trace as prefixed
            // log messages. Format mirrors BotBuilder.Core RunLogEntry.Display's Action branch (AdbCore can't
            // reference the editor). Synchronous adapter — keeps lines in execution order.
            var childProgress = new DelegateProgress<ExecutionProgress>(p => ChildLog(FormatActionLine(p)));

            var result = await new BotExecutor(_executors).RunAsync(nestedBot, childOptions, childProgress, ct);
```

Then add these two private members to the `NestedBotExecutor` class (e.g. just above `DisposeHandlesAsync`):

```csharp
    /// <summary>One trace line for an executed nested action. Mirrors BotBuilder.Core's
    /// <c>RunLogEntry.Display</c> Action rendering so nested lines read like top-level ones.</summary>
    private static string FormatActionLine(ExecutionProgress p)
    {
        var name = string.IsNullOrEmpty(p.ActionLabel) ? p.TypeKey : p.ActionLabel;
        return p.Success
            ? $"✓ {name}"
            : $"✗ {name}" + (string.IsNullOrEmpty(p.ErrorMessage) ? string.Empty : $": {p.ErrorMessage}");
    }

    /// <summary>Synchronous <see cref="IProgress{T}"/> so synthesized log lines stay in execution order
    /// (mirrors <c>RunnerApp.InlineProgress</c>; <see cref="Progress{T}"/> would post asynchronously).</summary>
    private sealed class DelegateProgress<T> : IProgress<T>
    {
        private readonly Action<T> _report;
        public DelegateProgress(Action<T> report) => _report = report;
        public void Report(T value) => _report(value);
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotLoggingTests"`
Expected: PASS (all six).

- [ ] **Step 5: Run the full AdbCore test suite (no regressions)**

Run: `dotnet test AdbCore.Tests/AdbCore.Tests.csproj`
Expected: PASS. In particular `NestedBotExecutorTests` (which passes a no-op `_ => { }` log) still passes — the new code only adds output, never changes routing or results.

- [ ] **Step 6: Commit**

```bash
git add AdbCore/Execution/NestedBotExecutor.cs AdbCore.Tests/Execution/NestedBotLoggingTests.cs
git commit -m "Nested bots emit a prefixed per-action trace to the run log"
```

---

### Task 2: Docs sync

**Files:**
- Modify: `CLAUDE.md` (the `.bot` / execution notes, if they describe nested-bot logging behavior)
- Modify: `README.md` (only if it describes the Test Run log; keep the goblin voice)

- [ ] **Step 1: Update docs**

Add a one-line note where nested bots / Test Run logging are described: nested-bot actions now log a `[BotName]`-prefixed trace, composing for deep nesting. Do not invent behavior beyond what Task 1 ships. If neither file currently describes this, add a concise sentence to the nested-bot section of `CLAUDE.md` only.

- [ ] **Step 2: Commit**

```bash
git add CLAUDE.md README.md
git commit -m "Docs: note nested-bot prefixed logging"
```

---

## Self-Review

- **Spec coverage (Feature 1):** prefix wrap (Task 1 Step 3) ✓; progress→log adapter ✓; deterministic synchronous progress ✓; message-kind (routes through `context.Log`) ✓; deep-nesting composition (test) ✓; explicit Log-action prefixing (test) ✓; blank-name fallback (test) ✓.
- **Placeholder scan:** none — all steps carry real code/commands.
- **Type consistency:** `context.Log` is `Action<string>` (non-null); `ExecutionProgress` has `ActionLabel` (string, may be empty), `TypeKey`, `Success`, `ErrorMessage` (nullable) — matches the formatter. `NestedBotAction`/`control.nestedBot`, `control.start` typekeys match existing tests. `RunAsync(bot, options, progress, ct)` signature matches `BotExecutor`.
