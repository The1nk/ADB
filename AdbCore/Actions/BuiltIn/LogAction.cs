using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Writes a configured message to the run log, then continues.</summary>
public sealed class LogAction : IActionDefinition, IActionExecutor
{
    /// <summary>Config key holding the message to log.</summary>
    public const string MessageKey = "message";

    public string TypeKey => "data.log";
    public string DisplayName => "Log";
    public string Category => "Data";
    public string Description => "Writes a message to the run log.";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public List<PortDefinition> OutputPorts { get; } = new() { new PortDefinition { Name = "out", Label = "Out" } };
    public List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = MessageKey, Label = "Message", Type = ConfigFieldType.String },
    };
    public bool SupportsRetry => false;

    public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        var message = context.Action.Config.TryGetValue(MessageKey, out var value)
            ? value?.ToString() ?? string.Empty
            : string.Empty;

        context.Log(message);
        return Task.FromResult(ActionResult.Ok("out"));
    }
}
