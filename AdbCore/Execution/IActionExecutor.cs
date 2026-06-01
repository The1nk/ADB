namespace AdbCore.Execution;

/// <summary>The runtime behaviour of an action type. Resolved by <see cref="ActionExecutorRegistry"/>.</summary>
public interface IActionExecutor
{
    /// <summary>Unique key matching the corresponding <c>IActionDefinition.TypeKey</c>.</summary>
    string TypeKey { get; }

    Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct);
}
