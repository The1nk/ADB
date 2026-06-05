namespace BotBuilder.Core;

/// <summary>A single input or output port shown on a node card.</summary>
public sealed class PortViewModel
{
    public PortViewModel(string name, PortDirection direction, PortEdge edge, CanvasPoint anchorOffset)
    {
        Name = name;
        Direction = direction;
        Edge = edge;
        AnchorOffset = anchorOffset;
    }

    public string Name { get; }
    public PortDirection Direction { get; }

    /// <summary>Which edge of the card this port sits on (drives its anchor and connector direction).</summary>
    public PortEdge Edge { get; }

    /// <summary>Position of this port relative to the card's top-left corner.</summary>
    public CanvasPoint AnchorOffset { get; private set; }

    /// <summary>Re-place this port (used when a node's layout recomputes, e.g. Run Parallel branch count).</summary>
    public void MoveTo(CanvasPoint anchorOffset) => AnchorOffset = anchorOffset;
}
