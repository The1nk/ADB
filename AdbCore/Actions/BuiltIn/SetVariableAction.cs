using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Writes a named run variable (as a string) into the execution context, then continues.
/// Readers (Branch, Loop) coerce the string to number/bool as needed.</summary>
public sealed class SetVariableAction : IActionDefinition, IActionExecutor
{
    public const string NameKey = "name";
    public const string ValueKey = "value";

    public string TypeKey => "data.setVariable";
    public string DisplayName => "Set Variable";
    public string Category => "Data";
    public string Description => "Sets a run variable to a value.";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public List<PortDefinition> OutputPorts { get; } = new() { new PortDefinition { Name = "out", Label = "Out" } };
    public List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = NameKey, Label = "Name", Type = ConfigFieldType.String },
        new ConfigField { Key = ValueKey, Label = "Value", Type = ConfigFieldType.String },
    };
    public bool SupportsRetry => false;

    public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        var name = ConfigValues.GetString(context.Action.Config, NameKey);
        if (!string.IsNullOrEmpty(name))
        {
            context.Context.Variables[name] = ConfigValues.GetString(context.Action.Config, ValueKey);
        }

        return Task.FromResult(ActionResult.Ok("out"));
    }
}
