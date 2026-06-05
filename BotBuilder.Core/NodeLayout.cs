namespace BotBuilder.Core;

/// <summary>Shared geometry for node cards, used by both the view (rendering) and the core (connection
/// anchor math) so endpoints line up with rendered ports. Cards grow to fit their right-edge ports; each
/// edge's ports form a block centered on the card body; failure outputs sit on the bottom edge.</summary>
public static class NodeLayout
{
    public const double CardWidth = 160;
    public const double CardHeight_Default = 70;
    public const double HeaderHeight = 28;
    public const double PortSpacing = 20;
    public const double PortRadius = 5;
    public const double BodyPad = 11;

    /// <summary>Card height needed to fit <paramref name="rightCount"/> centered right-edge ports
    /// (bottom-edge ports do not affect height). Never smaller than the default 70.</summary>
    public static double CardHeight(int rightCount)
    {
        var needed = HeaderHeight + Math.Max(0, rightCount - 1) * PortSpacing + 2 * BodyPad;
        return Math.Max(CardHeight_Default, needed);
    }

    private static double CenterY(double height) => HeaderHeight + (height - HeaderHeight) / 2;
    private static double BlockY(int index, int count, double height)
        => CenterY(height) - (count - 1) * PortSpacing / 2 + index * PortSpacing;

    public static CanvasPoint LeftAnchor(int index, int count, double height) => new(0, BlockY(index, count, height));
    public static CanvasPoint RightAnchor(int index, int count, double height) => new(CardWidth, BlockY(index, count, height));

    /// <summary>Bottom-edge anchor for failure port <paramref name="index"/> of <paramref name="count"/>,
    /// distributed evenly across the card's bottom edge.</summary>
    public static CanvasPoint BottomAnchor(int index, int count, double height)
        => new(CardWidth * (index + 1) / (count + 1), height);

    /// <summary>Unit outward normal for a port edge (the direction a connector leaves/approaches the port).</summary>
    public static CanvasPoint Outward(PortEdge edge) => edge switch
    {
        PortEdge.Left => new(-1, 0),
        PortEdge.Right => new(1, 0),
        PortEdge.Bottom => new(0, 1),
        _ => new(1, 0),
    };
}
