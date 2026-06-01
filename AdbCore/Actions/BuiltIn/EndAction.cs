using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Terminates the bot run. Has a single input port and no output (terminal).</summary>
public sealed class EndAction : IActionDefinition, IActionExecutor
{
    public string TypeKey => "control.end";
    public string DisplayName => "End";
    public string Category => "Control Flow";
    public string Description => "Terminates the bot run.";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public List<PortDefinition> OutputPorts { get; } = new();
    public List<ConfigField> ConfigFields { get; } = new();
    public bool SupportsRetry => false;

    public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
        => Task.FromResult(ActionResult.Ok(string.Empty));
}
