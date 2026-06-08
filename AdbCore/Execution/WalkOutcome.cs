namespace AdbCore.Execution;

/// <summary>The result of walking a (sub-)path of the action graph: completed, or failed at a specific
/// action. Returned by the graph walk and by <see cref="IControlFlowExecutor"/> implementations.</summary>
public sealed class WalkOutcome
{
    public bool Success { get; private init; }
    public string? ErrorMessage { get; private init; }
    public Guid? FailedActionId { get; private init; }

    public static WalkOutcome Completed() => new() { Success = true };

    public static WalkOutcome Failed(string? errorMessage, Guid failedActionId)
        => new() { Success = false, ErrorMessage = errorMessage, FailedActionId = failedActionId };
}
