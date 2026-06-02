using AdbCore.Actions;

namespace BotBuilder.Core.Tests;

/// <summary>A test action definition with varied config fields and retry support.</summary>
internal sealed class FakeConfigurableDefinition : IActionDefinition
{
    public string TypeKey => "test.configurable";
    public string DisplayName => "Configurable";
    public string Category => "Test";
    public string Description => "A configurable test action.";
    public List<PortDefinition> InputPorts { get; } = new();
    public List<PortDefinition> OutputPorts { get; } = new();
    public List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = "msg", Label = "Message", Type = ConfigFieldType.String },
        new ConfigField { Key = "count", Label = "Count", Type = ConfigFieldType.Number },
        new ConfigField { Key = "flag", Label = "Flag", Type = ConfigFieldType.Boolean },
    };
    public bool SupportsRetry => true;
}
