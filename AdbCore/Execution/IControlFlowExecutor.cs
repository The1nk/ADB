namespace AdbCore.Execution;

/// <summary>An engine-native control-flow node (Loop, Run Parallel, …) that orchestrates sub-walks rather
/// than performing a single leaf action. Resolved by <see cref="ControlFlowExecutorRegistry"/> from its
/// TypeKey, mirroring how leaf actions resolve via <see cref="ActionExecutorRegistry"/>.</summary>
public interface IControlFlowExecutor
{
    /// <summary>Unique key matching the corresponding control-flow <c>IActionDefinition.TypeKey</c>.</summary>
    string TypeKey { get; }

    Task<ControlFlowResult> ExecuteAsync(ControlFlowContext context, CancellationToken ct);
}
