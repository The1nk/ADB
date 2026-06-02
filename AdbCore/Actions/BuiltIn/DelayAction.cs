using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Waits a configured number of milliseconds, then continues. Cancellation-aware.</summary>
public sealed class DelayAction : IActionDefinition, IActionExecutor
{
    public const string DurationMsKey = "durationMs";

    public string TypeKey => "control.delay";
    public string DisplayName => "Delay";
    public string Category => "Control Flow";
    public string Description => "Waits for a fixed duration before continuing.";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public List<PortDefinition> OutputPorts { get; } = new() { new PortDefinition { Name = "out", Label = "Out" } };
    public List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = DurationMsKey, Label = "Duration (ms)", Type = ConfigFieldType.Number, DefaultValue = 0 },
    };
    public bool SupportsRetry => false;

    public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        var durationMs = ConfigValues.GetInt(context.Action.Config, DurationMsKey, 0);
        if (durationMs > 0)
        {
            await Task.Delay(durationMs, ct);
        }

        return ActionResult.Ok("out");
    }
}
