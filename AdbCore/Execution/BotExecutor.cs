using AdbCore.Models;

namespace AdbCore.Execution;

/// <summary>Walks a bot's action graph sequentially from its entry point, executing each action
/// and following the output port returned by its executor. Halts on failure unless an
/// <c>onFailure</c> port is wired.</summary>
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
