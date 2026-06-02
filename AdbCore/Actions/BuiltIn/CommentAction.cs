using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn;

/// <summary>A documentation node. Does nothing at runtime; if wired into the flow it passes through.</summary>
public sealed class CommentAction : IActionDefinition, IActionExecutor
{
    public const string TextKey = "text";

    public string TypeKey => "data.comment";
    public string DisplayName => "Comment";
    public string Category => "Data";
    public string Description => "A note on the canvas. No effect at runtime.";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public List<PortDefinition> OutputPorts { get; } = new() { new PortDefinition { Name = "out", Label = "Out" } };
    public List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = TextKey, Label = "Text", Type = ConfigFieldType.MultilineString },
    };
    public bool SupportsRetry => false;

    public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
        => Task.FromResult(ActionResult.Ok("out"));
}
