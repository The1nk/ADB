using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn;

/// <summary>A per-bot last-chance catch. When present, the engine routes any otherwise-unhandled failure
/// into this node (see <see cref="BotExecutor"/>) and continues from its output — so an author can wire a
/// recovery flow (e.g. reboot + re-initialize) that loops back into the graph. Reached only via the error
/// cascade, never a normal edge; does no work itself.</summary>
public sealed class ErrorHandlerAction : IActionDefinition, IActionExecutor
{
    /// <summary>The registry key for the Error Handler node. The graph walk routes unhandled failures here.</summary>
    public const string Key = "control.errorHandler";

    public string TypeKey => Key;
    public string DisplayName => "Error Handler";
    public string Category => "Control Flow";
    public string Description => "Catches any otherwise-unhandled error; wire its output into a recovery flow.";
    public List<PortDefinition> InputPorts { get; } = new();
    public List<PortDefinition> OutputPorts { get; } = new() { new PortDefinition { Name = "out", Label = "On Error Handled" } };
    public List<ConfigField> ConfigFields { get; } = new();
    public bool SupportsRetry => false;

    public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
        => Task.FromResult(ActionResult.Ok("out"));
}
