namespace BotBuilder.Core.Palette;

/// <summary>One draggable entry in the action palette.</summary>
public sealed class PaletteItem
{
    public PaletteItem(string typeKey, string displayName, string category)
    {
        TypeKey = typeKey;
        DisplayName = displayName;
        Category = category;
    }

    public string TypeKey { get; }
    public string DisplayName { get; }
    public string Category { get; }
}
