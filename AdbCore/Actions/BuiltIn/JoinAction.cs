namespace AdbCore.Actions.BuiltIn;

/// <summary>Synchronization point for a Run Parallel: the engine awaits all branches, then routes
/// <c>allSucceeded</c> or <c>someFailed</c>. Execution is engine-native; this type supplies
/// palette/properties metadata only and has no executor.</summary>
public sealed class JoinAction : IActionDefinition
{
    public const string JoinTypeKey = "control.join";
    public const string AllSucceededPort = "allSucceeded";
    public const string SomeFailedPort = "someFailed";

    public string TypeKey => JoinTypeKey;
    public string DisplayName => "Join";
    public string Category => "Control Flow";
    public string Description => "Waits for all parallel branches, then routes by success.";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public List<PortDefinition> OutputPorts { get; } = new()
    {
        new PortDefinition { Name = AllSucceededPort, Label = "All Succeeded" },
        new PortDefinition { Name = SomeFailedPort, Label = "Some Failed" },
    };
    public List<ConfigField> ConfigFields { get; } = new();
    public bool SupportsRetry => false;
}
