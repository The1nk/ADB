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
    private async Task<WalkOutcome> WalkAsync(RunState state, BotAction? start, CancellationToken ct)
    {
        var current = start;
        while (current is not null)
        {
            ct.ThrowIfCancellationRequested();

            if (!state.Executors.TryGet(current.TypeKey, out var executor) || executor is null)
            {
                return WalkOutcome.Failed($"No executor registered for TypeKey '{current.TypeKey}'.", current.Id);
            }

            var result = await ExecuteWithRetryAsync(executor, current, state, ct);
            state.ActionsExecuted++;

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
        public int ActionsExecuted { get; set; }
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
