using AdbCore.Actions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotBuilder.Core;

/// <summary>A node card on the canvas, wrapping a bot action instance.</summary>
public partial class NodeViewModel : ObservableObject
{
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private string _label = string.Empty;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string? _targetBadge;

    public NodeViewModel(
        Guid id,
        string typeKey,
        string label,
        string category,
        IReadOnlyList<PortViewModel> inputPorts,
        IReadOnlyList<PortViewModel> outputPorts,
        double x,
        double y)
    {
        Id = id;
        TypeKey = typeKey;
        _label = label;
        Category = category;
        InputPorts = inputPorts;
        OutputPorts = outputPorts;
        _x = x;
        _y = y;
    }

    public Guid Id { get; }
    public string TypeKey { get; }
    public string Category { get; }
    public string CategoryColor => CategoryColors.ColorFor(Category);
    public IReadOnlyList<PortViewModel> InputPorts { get; }
    public IReadOnlyList<PortViewModel> OutputPorts { get; }

    /// <summary>Builds a node from an action definition, deriving ports/category from it.</summary>
    public static NodeViewModel FromDefinition(IActionDefinition definition, Guid id, string label, double x, double y)
        => new(
            id,
            definition.TypeKey,
            string.IsNullOrEmpty(label) ? definition.DisplayName : label,
            definition.Category,
            definition.InputPorts.Select(p => new PortViewModel(p.Name, PortDirection.In)).ToList(),
            definition.OutputPorts.Select(p => new PortViewModel(p.Name, PortDirection.Out)).ToList(),
            x,
            y);
}
