namespace BotBuilder.Core;

/// <summary>Maps an action category to the hex colour used for its node-card header.</summary>
public static class CategoryColors
{
    /// <summary>Colour used for categories with no explicit mapping.</summary>
    public const string Default = "#9B9B9B";

    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Control Flow"] = "#4A90D9",
        ["Screen"] = "#7ED321",
        ["Input"] = "#F5A623",
        ["Android"] = "#9013FE",
        ["Browser"] = "#50E3C2",
        ["Web & API"] = "#B8E986",
        ["Files & System"] = "#BD10E0",
        ["Desktop UI"] = "#417505",
        ["Data"] = "#D0021B",
        ["Scripting"] = "#8B572A",
    };

    public static string ColorFor(string category)
        => Map.TryGetValue(category, out var hex) ? hex : Default;
}
