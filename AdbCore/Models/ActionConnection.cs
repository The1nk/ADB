namespace AdbCore.Models;

/// <summary>A directed edge between two actions in the graph.</summary>
public class ActionConnection
{
    public Guid Id { get; set; }
    public Guid SourceActionId { get; set; }
    public string SourcePort { get; set; } = string.Empty;
    public Guid TargetActionId { get; set; }
    public string TargetPort { get; set; } = string.Empty;
}
