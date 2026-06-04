namespace BotBuilder.Core.Picker;

/// <summary>Maps an action TypeKey to the coordinate point(s) the picker fills. Actions absent from the
/// map don't support coordinate picking. Keys here mirror each action's config field keys.</summary>
public static class CoordinateFieldMap
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<CoordinatePoint>> Map =
        new Dictionary<string, IReadOnlyList<CoordinatePoint>>
        {
            ["android.tap"] = [new CoordinatePoint("x", "y", "Target")],
            ["android.swipe"] = [new CoordinatePoint("x1", "y1", "Start"), new CoordinatePoint("x2", "y2", "End")],
            ["input.click"] = [new CoordinatePoint("x", "y", "Target")],
            ["input.rightClick"] = [new CoordinatePoint("x", "y", "Target")],
            ["input.doubleClick"] = [new CoordinatePoint("x", "y", "Target")],
            ["input.mouseMove"] = [new CoordinatePoint("x", "y", "Target")],
        };

    public static bool Supports(string typeKey) => Map.ContainsKey(typeKey);

    public static IReadOnlyList<CoordinatePoint> ForTypeKey(string typeKey) =>
        Map.TryGetValue(typeKey, out var points) ? points : [];
}
