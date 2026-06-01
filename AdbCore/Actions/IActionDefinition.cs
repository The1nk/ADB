namespace AdbCore.Actions;

/// <summary>
/// Describes an action type — its identity, ports, and configurable fields.
/// This is the primary extension point for adding new actions.
/// </summary>
public interface IActionDefinition
{
    /// <summary>Unique key, e.g. "screen.findImage".</summary>
    string TypeKey { get; }

    string DisplayName { get; }

    /// <summary>Category for palette grouping, e.g. "Screen", "Android", "Control Flow".</summary>
    string Category { get; }

    string Description { get; }

    List<PortDefinition> InputPorts { get; }
    List<PortDefinition> OutputPorts { get; }

    /// <summary>Fields that drive the properties-panel form.</summary>
    List<ConfigField> ConfigFields { get; }

    /// <summary>Whether a retry policy is applicable to this action.</summary>
    bool SupportsRetry { get; }
}
