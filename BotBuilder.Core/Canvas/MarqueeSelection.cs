namespace BotBuilder.Core.Canvas;

/// <summary>Selects the nodes whose card rectangle intersects a marquee rectangle (all in
/// world coordinates). The marquee rect is expected normalized (non-negative width/height).</summary>
public static class MarqueeSelection
{
    public static IEnumerable<NodeViewModel> NodesInRect(
        IEnumerable<NodeViewModel> nodes, double x, double y, double width, double height)
    {
        var right = x + width;
        var bottom = y + height;

        return nodes.Where(n =>
            n.X < right && n.X + NodeLayout.CardWidth > x &&
            n.Y < bottom && n.Y + NodeLayout.CardHeight_Default > y);
    }
}
