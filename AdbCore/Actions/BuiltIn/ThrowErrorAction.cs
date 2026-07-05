using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Deliberately fails the run with a configurable message. Terminal (one input, no outputs), so its
/// failure always propagates: it escapes an enclosing Loop and, inside a nested bot, returns control to the
/// parent — unlike End, which returns Ok and merely dead-ends the current path. Caught by the bot's Error
/// Handler if one is present, otherwise it bubbles up. The message is ${var}-interpolated by the engine
/// before execution.</summary>
public sealed class ThrowErrorAction : IActionDefinition, IActionExecutor
{
    public const string MessageKey = "message";
    public const string DefaultMessage = "Bot threw an error.";

    public string TypeKey => "control.throwError";
    public string DisplayName => "Throw Error";
    public string Category => "Control Flow";
    public string Description => "Fails the run with a message; use it to exit a nested bot or break out to an error handler.";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public List<PortDefinition> OutputPorts { get; } = new();
    public List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = MessageKey, Label = "Message", Type = ConfigFieldType.String, DefaultValue = DefaultMessage },
    };
    public bool SupportsRetry => false;

    public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        var message = ConfigValues.GetString(context.Action.Config, MessageKey, DefaultMessage);
        return Task.FromResult(ActionResult.Fail(string.IsNullOrWhiteSpace(message) ? DefaultMessage : message));
    }
}
