using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotBuilder.Core.Connections;

/// <summary>A directed connection between an output port and an input port. Recomputes its
/// bezier <see cref="PathData"/> whenever either endpoint node moves.</summary>
public partial class ConnectionViewModel : ObservableObject
{
    [ObservableProperty] private bool _isSelected;

    public ConnectionViewModel(
        Guid id,
        NodeViewModel source,
        PortViewModel sourcePort,
        NodeViewModel target,
        PortViewModel targetPort)
    {
        Id = id;
        Source = source;
        SourcePort = sourcePort;
        Target = target;
        TargetPort = targetPort;

        Attach();
    }

    public Guid Id { get; }
    public NodeViewModel Source { get; }
    public PortViewModel SourcePort { get; }
    public NodeViewModel Target { get; }
    public PortViewModel TargetPort { get; }

    private BackRoutePlan? _lane;

    public string PathData
    {
        get
        {
            var start = Anchor(Source, SourcePort);
            var end = Anchor(Target, TargetPort);
            if (_lane is { } lane && ConnectionGeometry.IsBackward(start, SourcePort.Edge, end, Source.PortsFlipped))
                return ConnectionGeometry.BuildLanedBackRoute(
                    start, SourcePort.Edge, end, TargetPort.Edge,
                    lane.RightCornerX, lane.LeftCornerX, lane.GutterY);
            return ConnectionGeometry.BuildPath(start, SourcePort.Edge, end, TargetPort.Edge, Source.PortsFlipped);
        }
    }

    /// <summary>True when this connection currently routes as a backward/return (loop) wire — i.e. it has
    /// been assigned a back-route lane and its geometry faces backward. Derived display state used to render
    /// return wires dashed + muted so loops read as loops. Recomputed whenever <see cref="PathData"/> is.</summary>
    public bool IsBackEdge
    {
        get
        {
            var start = Anchor(Source, SourcePort);
            var end = Anchor(Target, TargetPort);
            return _lane is not null && ConnectionGeometry.IsBackward(start, SourcePort.Edge, end, Source.PortsFlipped);
        }
    }

    /// <summary>Assigns (or clears) this connection's back-route lane and re-renders its path. Lanes are
    /// derived display state computed by <see cref="BackRoutePlanner"/> after layout/move/load.</summary>
    public void SetLane(BackRoutePlan? lane)
    {
        _lane = lane;
        OnPropertyChanged(nameof(PathData));
        OnPropertyChanged(nameof(IsBackEdge));
    }

    /// <summary>(Re)subscribes to endpoint move notifications. Idempotent.</summary>
    public void Attach()
    {
        Source.PropertyChanged -= OnEndpointMoved;
        Source.PropertyChanged += OnEndpointMoved;
        Target.PropertyChanged -= OnEndpointMoved;
        Target.PropertyChanged += OnEndpointMoved;
    }

    /// <summary>Detaches endpoint subscriptions; call when the connection is removed.</summary>
    public void Detach()
    {
        Source.PropertyChanged -= OnEndpointMoved;
        Target.PropertyChanged -= OnEndpointMoved;
    }

    private void OnEndpointMoved(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NodeViewModel.X) or nameof(NodeViewModel.Y) or nameof(NodeViewModel.Height))
        {
            OnPropertyChanged(nameof(PathData));
            OnPropertyChanged(nameof(IsBackEdge));
        }
    }

    private static CanvasPoint Anchor(NodeViewModel node, PortViewModel port)
        => new(node.X + port.AnchorOffset.X, node.Y + port.AnchorOffset.Y);
}
