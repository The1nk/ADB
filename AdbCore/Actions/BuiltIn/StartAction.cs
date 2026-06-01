using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn;

/// <summary>The entry point of a bot. Has a single output port and does nothing but proceed.</summary>
public sealed class StartAction : IActionDefinition, IActionExecutor
{
    public string TypeKey => "control.start";
    public string DisplayName => "Start";
    public string Category => "Control Flow";
    public string Description => "Entry point of the bot.";
    public List<PortDefinition> InputPorts { get; } = new();
    public List<PortDefinition> OutputPorts { get; } = new() { new PortDefinition { Name = "out", Label = "Out" } };
    public List<ConfigField> ConfigFields { get; } = new();
    public bool SupportsRetry => false;

    public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
        => Task.FromResult(ActionResult.Ok("out"));
}
