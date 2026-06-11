using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Models;

namespace AdbCore.Execution;

/// <summary>Leaf executor for the Nested Bot card: resolves the referenced library bot, runs it as a child
/// <see cref="BotExecutor"/> (the parent walk awaits — so it is paused), optionally seeding the child's
/// variables, sharing the parent's resolved targets by name, and merging the child's final variables back.
/// Routes onSuccess/onFailure and guards against reference cycles.</summary>
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

        var childOptions = new ExecutionOptions
        {
            Log = context.Log,
            NestedBotLibrary = run.NestedBots,
            NestedAncestry = run.NestedAncestry.Append(nestedId).ToList(),
            InitialVariables = sendVars ? new Dictionary<string, object>(run.Variables) : null,
            ResolvedTargets = sendTargets
                ? OverlayParentTargetsByName(nestedBot, run)
                : new Dictionary<Guid, ResolvedTarget>(),
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

    /// <summary>Maps each nested target whose NAME matches a parent target to the parent's already-resolved
    /// handle, keyed by the NESTED target's id (nested actions reference nested target ids). Nested targets with
    /// no name match are omitted — own-target binding arrives in the lazy-binder slice.</summary>
    private static IReadOnlyDictionary<Guid, ResolvedTarget> OverlayParentTargetsByName(Bot nestedBot, BotExecutionContext run)
    {
        var parentByName = new Dictionary<string, ResolvedTarget>(StringComparer.Ordinal);
        foreach (var kv in run.TargetNames)
        {
            if (run.Targets.TryGetValue(kv.Key, out var resolved))
            {
                parentByName[kv.Value] = resolved;
            }
        }

        var map = new Dictionary<Guid, ResolvedTarget>();
        foreach (var t in nestedBot.Targets)
        {
            if (!string.IsNullOrEmpty(t.Name) && parentByName.TryGetValue(t.Name, out var resolved))
            {
                map[t.Id] = resolved;
            }
        }
        return map;
    }
}
