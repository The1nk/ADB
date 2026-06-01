namespace AdbCore.Models;

/// <summary>A bot is a named DAG of actions and the connections between them.</summary>
public class Bot
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Named targets, resolved to live handles at run start.</summary>
    public List<BotTarget> Targets { get; set; } = new();

    public List<BotAction> Actions { get; set; } = new();
    public List<ActionConnection> Connections { get; set; } = new();

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
