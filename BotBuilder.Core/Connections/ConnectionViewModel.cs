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

        Source.PropertyChanged += OnEndpointMoved;
        Target.PropertyChanged += OnEndpointMoved;
    }

    public Guid Id { get; }
    public NodeViewModel Source { get; }
    public PortViewModel SourcePort { get; }
    public NodeViewModel Target { get; }
    public PortViewModel TargetPort { get; }

    public string PathData => ConnectionGeometry.BuildPath(Anchor(Source, SourcePort), Anchor(Target, TargetPort));

    /// <summary>Detaches endpoint subscriptions; call when the connection is removed.</summary>
    public void Detach()
    {
        Source.PropertyChanged -= OnEndpointMoved;
        Target.PropertyChanged -= OnEndpointMoved;
    }

    private void OnEndpointMoved(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NodeViewModel.X) or nameof(NodeViewModel.Y))
        {
            OnPropertyChanged(nameof(PathData));
        }
    }

    private static CanvasPoint Anchor(NodeViewModel node, PortViewModel port)
        => new(node.X + port.AnchorOffset.X, node.Y + port.AnchorOffset.Y);
}
