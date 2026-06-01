using System.Text.Json.Serialization;

namespace BotRunner;

/// <summary>One JSON-lines log record. Null fields are omitted on serialization.</summary>
public sealed class LogEntry
{
    public string Ts { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public string Event { get; set; } = string.Empty;

    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("bot")] public string? Bot { get; set; }
    [JsonPropertyName("actionId")] public string? ActionId { get; set; }
    [JsonPropertyName("label")] public string? Label { get; set; }
    [JsonPropertyName("typeKey")] public string? TypeKey { get; set; }
    [JsonPropertyName("success")] public bool? Success { get; set; }
    [JsonPropertyName("actionsExecuted")] public int? ActionsExecuted { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
}
