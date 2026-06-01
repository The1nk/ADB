namespace AdbCore.Execution;

/// <summary>The overall outcome of a bot run.</summary>
public class ExecutionResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public Guid? FailedActionId { get; set; }
    public int ActionsExecuted { get; set; }
}
