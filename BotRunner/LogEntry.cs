namespace BotRunner;

/// <summary>One JSON-lines log record. Null fields are omitted on serialization.</summary>
public sealed class LogEntry
{
    public string Ts { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public string Event { get; set; } = string.Empty;

    public string? Message { get; set; }
    public string? Bot { get; set; }
    public string? ActionId { get; set; }
    public string? Label { get; set; }
    public string? TypeKey { get; set; }
    public bool? Success { get; set; }
    public int? ActionsExecuted { get; set; }
    public string? Error { get; set; }
}
