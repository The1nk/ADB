using AdbCore.Actions;

namespace AdbCore.Tests.Actions;

/// <summary>Minimal test double for exercising the registry without shipping real actions.</summary>
internal sealed class FakeActionDefinition : IActionDefinition
{
    public required string TypeKey { get; init; }
    public string DisplayName { get; init; } = "Fake";
    public string Category { get; init; } = "Test";
    public string Description { get; init; } = "A fake action for tests.";
    public List<PortDefinition> InputPorts { get; init; } = new();
    public List<PortDefinition> OutputPorts { get; init; } = new();
    public List<ConfigField> ConfigFields { get; init; } = new();
    public bool SupportsRetry { get; init; }
}
