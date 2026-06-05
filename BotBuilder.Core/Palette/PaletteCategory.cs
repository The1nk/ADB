namespace BotBuilder.Core.Palette;

/// <summary>A named group of palette items, plus whether its external dependency is available here.</summary>
public sealed class PaletteCategory
{
    public PaletteCategory(string name, IReadOnlyList<PaletteItem> items, bool isAvailable = true, string? disabledReason = null)
    {
        Name = name;
        Items = items;
        IsAvailable = isAvailable;
        DisabledReason = disabledReason;
    }

    public string Name { get; }
    public IReadOnlyList<PaletteItem> Items { get; }

    /// <summary>False when this category's dependency (e.g. adb / Playwright) is missing on this machine.</summary>
    public bool IsAvailable { get; }

    /// <summary>Human explanation shown as a tooltip when <see cref="IsAvailable"/> is false; otherwise null.</summary>
    public string? DisabledReason { get; }
}
