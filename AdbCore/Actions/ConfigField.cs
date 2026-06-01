namespace AdbCore.Actions;

/// <summary>Metadata describing one configurable field of an action, used to drive the properties panel.</summary>
public class ConfigField
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public ConfigFieldType Type { get; set; }
    public object? DefaultValue { get; set; }

    /// <summary>Allowed values when <see cref="Type"/> is <see cref="ConfigFieldType.Enum"/>.</summary>
    public List<string> Options { get; set; } = new();
}
