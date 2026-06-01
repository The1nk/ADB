namespace AdbCore.Actions;

/// <summary>A named input or output port on an action node.</summary>
public class PortDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}
