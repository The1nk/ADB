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
