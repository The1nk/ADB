namespace BotBuilder.Core.Palette;

/// <summary>One draggable entry in the action palette. Carries its category's availability so the item template
/// can grey + tooltip directly.</summary>
public sealed class PaletteItem
{
    public PaletteItem(string typeKey, string displayName, string category, bool isAvailable = true, string? disabledReason = null)
    {
        TypeKey = typeKey;
        DisplayName = displayName;
        Category = category;
        IsAvailable = isAvailable;
        DisabledReason = disabledReason;
    }

    public string TypeKey { get; }
    public string DisplayName { get; }
    public string Category { get; }

    /// <summary>False when this item's category dependency is missing on this machine.</summary>
    public bool IsAvailable { get; }

    /// <summary>Human explanation shown as a tooltip when <see cref="IsAvailable"/> is false; otherwise null.</summary>
    public string? DisabledReason { get; }
}
