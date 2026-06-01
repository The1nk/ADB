namespace AdbCore.Actions;

/// <summary>The kind of UI control a <see cref="ConfigField"/> renders as in the properties panel.</summary>
public enum ConfigFieldType
{
    String,
    MultilineString,
    Number,
    Boolean,
    Enum,
    FilePath,
    ImagePath,
}
