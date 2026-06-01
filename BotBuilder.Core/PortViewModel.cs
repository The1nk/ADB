namespace BotBuilder.Core;

/// <summary>A single input or output port shown on a node card.</summary>
public sealed class PortViewModel
{
    public PortViewModel(string name, PortDirection direction, CanvasPoint anchorOffset)
    {
        Name = name;
        Direction = direction;
        AnchorOffset = anchorOffset;
    }

    public string Name { get; }
    public PortDirection Direction { get; }

    /// <summary>Position of this port relative to the card's top-left corner.</summary>
    public CanvasPoint AnchorOffset { get; }
}
