namespace BotBuilder.Core;

/// <summary>Shared geometry constants for node cards, used by both the view (rendering)
/// and the core (connection anchor math) so connection endpoints line up with rendered ports.</summary>
public static class NodeLayout
{
    public const double CardWidth = 160;
    public const double CardHeight = 70;
    public const double HeaderHeight = 28;
    public const double PortAreaTop = HeaderHeight + 12;
    public const double PortSpacing = 20;
    public const double PortRadius = 5;

    public static CanvasPoint InputAnchor(int index) => new(0, PortAreaTop + index * PortSpacing);
    public static CanvasPoint OutputAnchor(int index) => new(CardWidth, PortAreaTop + index * PortSpacing);
}
