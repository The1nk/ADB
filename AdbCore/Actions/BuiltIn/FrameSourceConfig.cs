namespace AdbCore.Actions.BuiltIn;

/// <summary>Shared "Source" config for readers that can either capture fresh or reuse a stored
/// <see cref="AdbCore.Screen.FrameSnapshot"/>. Default is Fresh, so existing bots are unchanged.</summary>
public static class FrameSourceConfig
{
    public const string SourceKey = "source";
    public const string FreshValue = "Fresh";
    public const string StoredValue = "Stored";
    public const string FrameNameKey = "frameName";
    public const string DefaultFrameName = "frame";

    public static ConfigField SourceField() => new()
    {
        Key = SourceKey,
        Label = "Source",
        Type = ConfigFieldType.Enum,
        DefaultValue = FreshValue,
        Options = new() { FreshValue, StoredValue },
    };

    public static ConfigField FrameNameField() => new()
    {
        Key = FrameNameKey,
        Label = "Frame Name",
        Type = ConfigFieldType.String,
        DefaultValue = DefaultFrameName,
    };

    /// <summary>The Source + Frame Name field pair, in display order.</summary>
    public static IEnumerable<ConfigField> Fields() => [SourceField(), FrameNameField()];

    public static bool UsesStoredFrame(IReadOnlyDictionary<string, object> config)
        => string.Equals(ConfigValues.GetString(config, SourceKey, FreshValue), StoredValue, StringComparison.OrdinalIgnoreCase);

    public static string FrameNameOf(IReadOnlyDictionary<string, object> config)
    {
        var name = ConfigValues.GetString(config, FrameNameKey, DefaultFrameName);
        return string.IsNullOrWhiteSpace(name) ? DefaultFrameName : name;
    }
}
