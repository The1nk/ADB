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
