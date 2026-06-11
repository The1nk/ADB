namespace AdbCore.Execution;

/// <summary>The overall outcome of a bot run.</summary>
public class ExecutionResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public Guid? FailedActionId { get; set; }
    public int ActionsExecuted { get; set; }

    /// <summary>Snapshot of run variables at completion (used by a parent run to receive a nested run's vars).</summary>
    public IReadOnlyDictionary<string, object> FinalVariables { get; set; } = new Dictionary<string, object>();
}
