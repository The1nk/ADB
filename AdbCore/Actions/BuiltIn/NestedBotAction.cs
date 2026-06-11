namespace AdbCore.Actions.BuiltIn;

/// <summary>A card that runs another bot from this file's nested-bot library, then continues. Execution is
/// performed by <c>NestedBotExecutor</c> (a leaf executor that runs the referenced bot as a child). This type
/// supplies palette/properties metadata. The <c>nestedBotId</c> reference is stored in config and set by the
/// editor UI; the three boolean flags control variable/target sharing per call site.</summary>
public sealed class NestedBotAction : IActionDefinition
{
    public const string NestedBotTypeKey = "control.nestedBot";

    public const string NestedBotIdKey = "nestedBotId";
    public const string SendVarsKey = "sendVars";
    public const string SendTargetsKey = "sendTargets";
    public const string ReceiveVarsKey = "receiveVars";

    public const string SuccessPort = "onSuccess";
    public const string FailurePort = "onFailure";

    public string TypeKey => NestedBotTypeKey;
    public string DisplayName => "Nested Bot";
    public string Category => "Control Flow";
    public string Description =>
        "Runs another bot from this file's nested-bot library, then continues. Optionally shares variables and targets.";

    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public List<PortDefinition> OutputPorts { get; } = new()
    {
        new PortDefinition { Name = SuccessPort, Label = "On Success" },
        new PortDefinition { Name = FailurePort, Label = "On Failure" },
    };
    public List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = SendVarsKey, Label = "Send Vars", Type = ConfigFieldType.Boolean, DefaultValue = false },
        new ConfigField { Key = SendTargetsKey, Label = "Send Targets", Type = ConfigFieldType.Boolean, DefaultValue = false },
        new ConfigField { Key = ReceiveVarsKey, Label = "Receive Vars", Type = ConfigFieldType.Boolean, DefaultValue = false },
    };
    public bool SupportsRetry => true;
}
