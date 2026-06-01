namespace AdbCore.Execution;

/// <summary>Reported once per action as the engine executes it.</summary>
public class ExecutionProgress
{
    public Guid ActionId { get; set; }
    public string ActionLabel { get; set; } = string.Empty;
    public string TypeKey { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
