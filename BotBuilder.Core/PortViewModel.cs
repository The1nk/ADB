using CommunityToolkit.Mvvm.ComponentModel;

namespace BotBuilder.Core;

/// <summary>A single input or output port shown on a node card.</summary>
public sealed partial class PortViewModel : ObservableObject
{
    /// <summary>Position of this port relative to the card's top-left corner. Observable so the canvas
    /// re-renders the port when a node's layout recomputes (e.g. a Run Parallel branch-count change moves
    /// every branch port to a new centered anchor).</summary>
    [ObservableProperty] private CanvasPoint _anchorOffset;

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

    /// <summary>Re-place this port (used when a node's layout recomputes, e.g. Run Parallel branch count).
    /// Raises a change notification so the bound canvas port + its connectors re-route.</summary>
    public void MoveTo(CanvasPoint anchorOffset) => AnchorOffset = anchorOffset;
}
