namespace AdbCore.Execution;

/// <summary>The result of walking a (sub-)path of the action graph: completed normally, exited early via
/// a Loop-Break signal, or failed at a specific action. Returned by the graph walk and by
/// <see cref="IControlFlowExecutor"/> implementations.</summary>
public sealed class WalkOutcome
{
    public bool Success { get; private init; }

    /// <summary>Marks an early loop exit (Loop-Break) to be consumed by the innermost enclosing loop.
    /// A break outcome is not a failure — <see cref="Success"/> remains <see langword="true"/>.</summary>
    public bool IsBreak { get; private init; }
    public string? ErrorMessage { get; private init; }
    public Guid? FailedActionId { get; private init; }

    public static WalkOutcome Completed() => new() { Success = true };

    /// <summary>Signals the innermost enclosing loop to exit early.</summary>
    public static WalkOutcome Break() => new() { Success = true, IsBreak = true };

    public static WalkOutcome Failed(string? errorMessage, Guid failedActionId)
        => new() { Success = false, ErrorMessage = errorMessage, FailedActionId = failedActionId };
}
