using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Models;

namespace AdbCore.Execution;

/// <summary>Walks a bot's action graph from its entry point, executing each leaf action and following
/// the output port its executor returns. The walk is recursive (<see cref="WalkAsync"/>) so engine-native
/// control-flow nodes can drive sub-walks. Halts on failure unless an <c>onFailure</c> port is wired.</summary>
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

        var entry = FindEntryPoint(bot);
        if (entry is null)
        {
            return new ExecutionResult
            {
                Success = false,
                ErrorMessage = "No entry point: every action has an incoming connection.",
            };
        }

        var state = new RunState(bot, _executors, context, options.Log ?? (_ => { }), progress);
        var outcome = await WalkAsync(state, entry, ct);

        return new ExecutionResult
        {
            Success = outcome.Success,
            ErrorMessage = outcome.ErrorMessage,
            FailedActionId = outcome.FailedActionId,
            ActionsExecuted = state.ActionsExecuted,
        };
    }

    /// <summary>Walks forward from <paramref name="start"/>, following output ports until the path
    /// dead-ends (no matching connection). Returns the first unhandled failure, or completion.</summary>
    private async Task<WalkOutcome> WalkAsync(RunState state, BotAction? start, CancellationToken ct, Guid? stopBeforeId = null)
    {
        var current = start;
        while (current is not null)
        {
            ct.ThrowIfCancellationRequested();

            if (stopBeforeId is not null && current.Id == stopBeforeId.Value)
            {
                return WalkOutcome.Completed();
            }

            if (current.TypeKey == LoopAction.LoopTypeKey)
            {
                var loopOutcome = await ExecuteLoopAsync(state, current, ct);
                if (!loopOutcome.Success)
                {
                    return loopOutcome;
                }

                current = FindNext(state.Bot, current.Id, LoopAction.DonePort);
                continue;
            }

            if (current.TypeKey == RunParallelAction.RunParallelTypeKey)
            {
                var (parallelOutcome, joinId, joinPort) = await ExecuteParallelAsync(state, current, ct);
                if (!parallelOutcome.Success)
                {
                    return parallelOutcome;
                }

                current = joinId is null ? null : FindNext(state.Bot, joinId.Value, joinPort);
                continue;
            }

            if (!state.Executors.TryGet(current.TypeKey, out var executor) || executor is null)
            {
                return WalkOutcome.Failed($"No executor registered for TypeKey '{current.TypeKey}'.", current.Id);
            }

            var result = await ExecuteWithRetryAsync(executor, current, state, ct);
            state.RecordActionExecuted();

            state.Progress?.Report(new ExecutionProgress
            {
                ActionId = current.Id,
                ActionLabel = current.Label,
                TypeKey = current.TypeKey,
                Success = result.Success,
                ErrorMessage = result.ErrorMessage,
            });

            if (!result.Success)
            {
                var failureNext = FindNext(state.Bot, current.Id, FailurePort);
                if (failureNext is not null)
                {
                    current = failureNext;
                    continue;
                }

                return WalkOutcome.Failed(result.ErrorMessage, current.Id);
            }

            current = FindNext(state.Bot, current.Id, result.OutputPort);
        }

        return WalkOutcome.Completed();
    }

    /// <summary>Engine-native Loop: re-walks the Body sub-path once per iteration (count or for-each),
    /// setting the optional index/item variables, then returns so the caller can follow Done.</summary>
    private async Task<WalkOutcome> ExecuteLoopAsync(RunState state, BotAction loop, CancellationToken ct)
    {
        var bodyStart = FindNext(state.Bot, loop.Id, LoopAction.BodyPort);
        var mode = ConfigValues.GetString(loop.Config, LoopAction.ModeKey, LoopAction.ModeCount);
        var indexVar = ConfigValues.GetString(loop.Config, LoopAction.IndexVariableKey);
        var itemVar = ConfigValues.GetString(loop.Config, LoopAction.ItemVariableKey);

        IReadOnlyList<string?> items;
        if (string.Equals(mode, LoopAction.ModeForEach, StringComparison.OrdinalIgnoreCase))
        {
            var collectionVar = ConfigValues.GetString(loop.Config, LoopAction.CollectionVariableKey);
            var raw = !string.IsNullOrEmpty(collectionVar)
                && state.Context.Variables.TryGetValue(collectionVar, out var v) ? v : null;
            items = SplitItems(raw);
        }
        else
        {
            // Fallback matches LoopAction's "count" ConfigField.DefaultValue (1): a dropped Loop whose
            // Count was never edited has no "count" key in Config, yet should iterate once, not zero times.
            var count = Math.Max(0, ConfigValues.GetInt(loop.Config, LoopAction.CountKey, 1));
            var placeholders = new string?[count];
            items = placeholders;
        }

        for (var i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (!string.IsNullOrEmpty(indexVar))
            {
                state.Context.Variables[indexVar] = i;
            }

            if (!string.IsNullOrEmpty(itemVar) && items[i] is not null)
            {
                state.Context.Variables[itemVar] = items[i]!;
            }

            var bodyOutcome = await WalkAsync(state, bodyStart, ct);
            if (!bodyOutcome.Success)
            {
                return bodyOutcome;
            }
        }

        return WalkOutcome.Completed();
    }

    /// <summary>For-each item source: a comma-separated string. Empty/whitespace yields no items;
    /// each item is trimmed.</summary>
    private static IReadOnlyList<string?> SplitItems(object? raw)
    {
        var text = ConfigValues.AsString(raw);
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string?>();
        }

        return text.Split(',').Select(part => (string?)part.Trim()).ToList();
    }

    /// <summary>Engine-native Run Parallel: runs each wired branch concurrently as a sub-walk that stops
    /// at the convergent Join, aggregates the outcomes per <see cref="ParallelErrorStrategy"/>, and reports
    /// where execution should continue (the Join's allSucceeded/someFailed port), or a halting failure.</summary>
    private async Task<(WalkOutcome Outcome, Guid? JoinId, string JoinPort)> ExecuteParallelAsync(
        RunState state, BotAction runParallel, CancellationToken ct)
    {
        var strategy = ParseStrategy(
            ConfigValues.GetString(runParallel.Config, RunParallelAction.OnBranchFailureKey, nameof(ParallelErrorStrategy.HaltAll)));
        var branchCount = Math.Max(1,
            ConfigValues.GetInt(runParallel.Config, RunParallelAction.BranchesKey, RunParallelAction.DefaultBranchCount));

        var branchStarts = new List<BotAction>();
        for (var i = 1; i <= branchCount; i++)
        {
            var start = FindNext(state.Bot, runParallel.Id, RunParallelAction.BranchPort(i));
            if (start is not null)
            {
                branchStarts.Add(start);
            }
        }

        if (branchStarts.Count == 0)
        {
            return (WalkOutcome.Failed("Run Parallel has no wired branch ports.", runParallel.Id), null, string.Empty);
        }

        var joinId = FindConvergentJoin(state.Bot, branchStarts.Select(b => b.Id).ToList());
        if (joinId is null)
        {
            return (WalkOutcome.Failed("Run Parallel branches must converge on exactly one Join.", runParallel.Id), null, string.Empty);
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var outcomes = new WalkOutcome[branchStarts.Count];

        async Task RunBranchAsync(int index)
        {
            try
            {
                var outcome = await WalkAsync(state, branchStarts[index], linked.Token, joinId.Value);
                outcomes[index] = outcome;
                if (!outcome.Success && strategy == ParallelErrorStrategy.HaltAll)
                {
                    linked.Cancel();
                }
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                // Cancelled because a sibling failed under HaltAll — not a failure of this branch itself.
                outcomes[index] = WalkOutcome.Completed();
            }
        }

        var tasks = Enumerable.Range(0, branchStarts.Count).Select(RunBranchAsync).ToArray();
        await Task.WhenAll(tasks); // a genuine user cancellation (outer ct) surfaces as OperationCanceledException

        var firstFailure = outcomes.FirstOrDefault(o => o is not null && !o.Success);
        if (firstFailure is null)
        {
            return (WalkOutcome.Completed(), joinId, JoinAction.AllSucceededPort);
        }

        // A branch failed. If someFailed is wired, route to it (handled) regardless of strategy.
        if (FindNext(state.Bot, joinId.Value, JoinAction.SomeFailedPort) is not null)
        {
            return (WalkOutcome.Completed(), joinId, JoinAction.SomeFailedPort);
        }

        // Unhandled failure: Continue swallows it; Halt strategies fail the run.
        if (strategy == ParallelErrorStrategy.Continue)
        {
            return (WalkOutcome.Completed(), joinId, JoinAction.SomeFailedPort);
        }

        return (WalkOutcome.Failed(firstFailure.ErrorMessage, firstFailure.FailedActionId ?? runParallel.Id), null, string.Empty);
    }

    private static ParallelErrorStrategy ParseStrategy(string value)
        => Enum.TryParse<ParallelErrorStrategy>(value, ignoreCase: true, out var s) ? s : ParallelErrorStrategy.HaltAll;

    /// <summary>Finds the single Join node all branches converge on, choosing the nearest common Join when
    /// more than one is reachable from every branch. Returns null if zero, or an ambiguous tie.</summary>
    private static Guid? FindConvergentJoin(Bot bot, IReadOnlyList<Guid> branchStartIds)
    {
        var perBranch = branchStartIds.Select(id => JoinDistances(bot, id)).ToList();
        if (perBranch.Count == 0)
        {
            return null;
        }

        IEnumerable<Guid> common = perBranch[0].Keys;
        foreach (var map in perBranch.Skip(1))
        {
            common = common.Intersect(map.Keys);
        }

        var commonJoins = common.ToList();
        if (commonJoins.Count == 0)
        {
            return null;
        }

        if (commonJoins.Count == 1)
        {
            return commonJoins[0];
        }

        Guid? best = null;
        var bestScore = int.MaxValue;
        var tie = false;
        foreach (var join in commonJoins)
        {
            var score = perBranch.Max(map => map[join]);
            if (score < bestScore)
            {
                bestScore = score;
                best = join;
                tie = false;
            }
            else if (score == bestScore)
            {
                tie = true;
            }
        }

        return tie ? null : best;
    }

    /// <summary>BFS forward from <paramref name="startId"/> over all outgoing edges, returning the shortest
    /// distance to each reachable Join node.</summary>
    private static Dictionary<Guid, int> JoinDistances(Bot bot, Guid startId)
    {
        var distances = new Dictionary<Guid, int>();
        var visited = new HashSet<Guid> { startId };
        var queue = new Queue<(Guid Id, int Depth)>();
        queue.Enqueue((startId, 0));

        while (queue.Count > 0)
        {
            var (id, depth) = queue.Dequeue();
            var node = bot.Actions.FirstOrDefault(a => a.Id == id);
            if (node is not null && node.TypeKey == JoinAction.JoinTypeKey && !distances.ContainsKey(id))
            {
                distances[id] = depth;
            }

            foreach (var edge in bot.Connections.Where(c => c.SourceActionId == id))
            {
                if (visited.Add(edge.TargetActionId))
                {
                    queue.Enqueue((edge.TargetActionId, depth + 1));
                }
            }
        }

        return distances;
    }

    private async Task<ActionResult> ExecuteWithRetryAsync(
        IActionExecutor executor,
        BotAction action,
        RunState state,
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
                var actionContext = new ActionExecutionContext(action, state.Context, state.Log);
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

    /// <summary>Mutable per-run state threaded through the recursive walk.</summary>
    private sealed class RunState
    {
        public RunState(
            Bot bot,
            ActionExecutorRegistry executors,
            BotExecutionContext context,
            Action<string> log,
            IProgress<ExecutionProgress>? progress)
        {
            Bot = bot;
            Executors = executors;
            Context = context;
            Log = log;
            Progress = progress;
        }

        public Bot Bot { get; }
        public ActionExecutorRegistry Executors { get; }
        public BotExecutionContext Context { get; }
        public Action<string> Log { get; }
        public IProgress<ExecutionProgress>? Progress { get; }
        private int _actionsExecuted;
        public int ActionsExecuted => Volatile.Read(ref _actionsExecuted);
        public void RecordActionExecuted() => Interlocked.Increment(ref _actionsExecuted);
    }

    /// <summary>Result of walking a sub-path: completed, or failed at a specific action.</summary>
    private sealed class WalkOutcome
    {
        public bool Success { get; private init; }
        public string? ErrorMessage { get; private init; }
        public Guid? FailedActionId { get; private init; }

        public static WalkOutcome Completed() => new() { Success = true };

        public static WalkOutcome Failed(string? errorMessage, Guid failedActionId)
            => new() { Success = false, ErrorMessage = errorMessage, FailedActionId = failedActionId };
    }
}
