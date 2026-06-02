namespace AdbCore.Actions.BuiltIn;

/// <summary>Repeats its Body sub-path N times (count) or once per item (for-each), then follows Done.
/// Execution is engine-native (see <c>BotExecutor.ExecuteLoopAsync</c>); this type supplies palette
/// and properties-panel metadata only and has no executor.</summary>
public sealed class LoopAction : IActionDefinition
{
    public const string LoopTypeKey = "control.loop";
    public const string BodyPort = "body";
    public const string DonePort = "done";

    public const string ModeKey = "mode";
    public const string CountKey = "count";
    public const string CollectionVariableKey = "collectionVariable";
    public const string IndexVariableKey = "indexVariable";
    public const string ItemVariableKey = "itemVariable";

    public const string ModeCount = "Count";
    public const string ModeForEach = "ForEach";

    public string TypeKey => LoopTypeKey;
    public string DisplayName => "Loop";
    public string Category => "Control Flow";
    public string Description => "Repeats the Body path by count or for each item, then follows Done.";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public List<PortDefinition> OutputPorts { get; } = new()
    {
        new PortDefinition { Name = BodyPort, Label = "Body" },
        new PortDefinition { Name = DonePort, Label = "Done" },
    };
    public List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField
        {
            Key = ModeKey, Label = "Mode", Type = ConfigFieldType.Enum,
            DefaultValue = ModeCount, Options = new() { ModeCount, ModeForEach },
        },
        new ConfigField { Key = CountKey, Label = "Count", Type = ConfigFieldType.Number, DefaultValue = 1 },
        new ConfigField { Key = CollectionVariableKey, Label = "Collection Variable", Type = ConfigFieldType.String },
        new ConfigField { Key = IndexVariableKey, Label = "Index Variable", Type = ConfigFieldType.String },
        new ConfigField { Key = ItemVariableKey, Label = "Item Variable", Type = ConfigFieldType.String },
    };
    public bool SupportsRetry => false;
}
