namespace BotBuilder.Core.Palette;

/// <summary>A named group of palette items.</summary>
public sealed class PaletteCategory
{
    public PaletteCategory(string name, IReadOnlyList<PaletteItem> items)
    {
        Name = name;
        Items = items;
    }

    public string Name { get; }
    public IReadOnlyList<PaletteItem> Items { get; }
}
