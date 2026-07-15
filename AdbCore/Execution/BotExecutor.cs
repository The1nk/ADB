using AdbCore.Actions;
using AdbCore.Models;

namespace AdbCore.Execution;

/// <summary>Walks a bot's action graph from its entry point, executing each leaf action and following
/// the output port its executor returns. The walk is recursive (<see cref="WalkAsync"/>) so engine-native
/// control-flow nodes can drive sub-walks. Halts on failure unless an <c>onFailure</c> port is wired.</summary>
public class BotExecutor
{
    private const string FailurePort = "onFailure";

    private readonly ActionExecutorRegistry _executors;
    private readonly ControlFlowExecutorRegistry _controlFlow;

    public BotExecutor(ActionExecutorRegistry executors, ControlFlowExecutorRegistry? controlFlow = null)
    {
        ArgumentNullException.ThrowIfNull(executors);
        _executors = executors;
        _controlFlow = controlFlow ?? ControlFlowExecutorRegistry.CreateDefault();
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
        context.TargetNames = bot.Targets.ToDictionary(t => t.Id, t => t.Name);
        context.NestedBots = options.NestedBotLibrary ?? bot.NestedBots.ToDictionary(b => b.Id);
        context.NestedAncestry = options.NestedAncestry;
        context.TargetBinder = options.TargetBinder;
        if (options.InitialVariables is not null)
        {
            foreach (var kv in options.InitialVariables)
            {
                context.Variables[kv.Key] = kv.Value;
            }
        }

        var graph = new BotGraph(bot);
        var entry = graph.EntryPoint;
        if (entry is null)
        {
            return new ExecutionResult
            {
                Success = false,
                ErrorMessage = "No entry point: every action has an incoming connection.",
                FinalVariables = new Dictionary<string, object>(context.Variables),
            };
        }

        // Serialize the shared log + progress sinks so concurrent Run Parallel branches can't corrupt a
        // non-thread-safe sink (file writer, UI list, etc.). A single per-run lock; uncontended for the common
        // single-threaded run.
        // ONE shared gate for both sinks (not two): a handler that logs from inside a progress callback — or
        // reports from inside a log call — would deadlock across two separate locks (AB-BA), but cannot across
        // one. Each (nested) run allocates its own gate, so cross-run nesting is strictly child→parent.
        var sinkGate = new object();
        var rawLog = options.Log ?? (_ => { });
        void SynchronizedLog(string message) { lock (sinkGate) { rawLog(message); } }
        var synchronizedProgress = progress is null ? null : new SynchronizedProgress(progress, sinkGate);

        var state = new RunState(graph, _executors, _controlFlow, context, SynchronizedLog, synchronizedProgress);
        var outcome = await WalkAsync(state, entry, ct);

        // Global Error Handler: when the walk ends in an unhandled failure and the bot has an Error Handler
        // node, route into it (a fresh walk from that node) rather than aborting. The handler's own flow can
        // wire back to an earlier node to build a reboot/retry loop; if that flow fails again, we re-enter the
        // handler. Iterative here (no stack growth) and cancellable via ct — same shape as an always-on Loop.
        // With no Error Handler this loop never runs, so the failed outcome bubbles exactly as before.
        var errorHandler = graph.ErrorHandler;
        while (!outcome.Success && errorHandler is not null)
        {
            SeedErrorContext(context, graph, outcome);
            state.Log($"⚠ unhandled error → Error Handler: {outcome.ErrorMessage}");
            outcome = await WalkAsync(state, errorHandler, ct);
        }

        return new ExecutionResult
        {
            Success = outcome.Success,
            ErrorMessage = outcome.ErrorMessage,
            FailedActionId = outcome.FailedActionId,
            ActionsExecuted = state.ActionsExecuted,
            FinalVariables = new Dictionary<string, object>(context.Variables),
        };
    }

    /// <summary>Publishes an unhandled failure's details as run variables the Error Handler flow can read
    /// via <c>${error.message}</c> etc. before it runs.</summary>
    private static void SeedErrorContext(BotExecutionContext context, BotGraph graph, WalkOutcome outcome)
    {
        var failed = outcome.FailedActionId is Guid id ? graph.Find(id) : null;
        context.Variables["error.message"] = outcome.ErrorMessage ?? string.Empty;
        context.Variables["error.action"] = failed?.Label ?? string.Empty;
        context.Variables["error.actionId"] = outcome.FailedActionId?.ToString() ?? string.Empty;
        context.Variables["error.typeKey"] = failed?.TypeKey ?? string.Empty;
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

            if (state.ControlFlow.TryGet(current.TypeKey, out var controlFlow) && controlFlow is not null)
            {
                var cfContext = new ControlFlowContext(
                    state.Graph, current, state.Context, state.Log,
                    (cfStart, cfStop, cfToken) => WalkAsync(state, cfStart, cfToken, cfStop));
                var cfResult = await controlFlow.ExecuteAsync(cfContext, ct);
                if (!cfResult.Outcome.Success)
                {
                    return cfResult.Outcome;
                }
                if (cfResult.IsBreak)
                {
                    return WalkOutcome.Break(); // unwind this sub-walk; the innermost loop consumes it
                }
                current = cfResult.Next;
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
                var failureNext = state.Graph.FindNext(current.Id, FailurePort);
                if (failureNext is not null)
                {
                    current = failureNext;
                    continue;
                }

                return WalkOutcome.Failed(result.ErrorMessage, current.Id);
            }

            current = state.Graph.FindNext(current.Id, result.OutputPort);
        }

        return WalkOutcome.Completed();
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

        // Resolve ${var} tokens in config against the current run variables, once per execution
        // (variables are stable across this action's retry attempts). Retry policy is read from the original.
        var resolvedAction = ConfigInterpolator.Resolve(action, state.Context.Variables);

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (attempt > 0 && delayMs > 0)
            {
                await Task.Delay(delayMs, ct);
            }

            try
            {
                var actionContext = new ActionExecutionContext(resolvedAction, state.Context, state.Log);
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

    /// <summary>Mutable per-run state threaded through the recursive walk.</summary>
    private sealed class RunState
    {
        public RunState(
            BotGraph graph,
            ActionExecutorRegistry executors,
            ControlFlowExecutorRegistry controlFlow,
            BotExecutionContext context,
            Action<string> log,
            IProgress<ExecutionProgress>? progress)
        {
            Graph = graph;
            Executors = executors;
            ControlFlow = controlFlow;
            Context = context;
            Log = log;
            Progress = progress;
        }

        public BotGraph Graph { get; }
        public ActionExecutorRegistry Executors { get; }
        public ControlFlowExecutorRegistry ControlFlow { get; }
        public BotExecutionContext Context { get; }
        public Action<string> Log { get; }
        public IProgress<ExecutionProgress>? Progress { get; }
        private int _actionsExecuted;
        public int ActionsExecuted => Volatile.Read(ref _actionsExecuted);
        public void RecordActionExecuted() => Interlocked.Increment(ref _actionsExecuted);
    }

    /// <summary>Wraps an <see cref="IProgress{T}"/> so concurrent Run Parallel branches report one at a time,
    /// sharing the run's sink lock with the log delegate.</summary>
    private sealed class SynchronizedProgress : IProgress<ExecutionProgress>
    {
        private readonly IProgress<ExecutionProgress> _inner;
        private readonly object _gate;

        public SynchronizedProgress(IProgress<ExecutionProgress> inner, object gate)
        {
            _inner = inner;
            _gate = gate;
        }

        public void Report(ExecutionProgress value)
        {
            lock (_gate) { _inner.Report(value); }
        }
    }

}
