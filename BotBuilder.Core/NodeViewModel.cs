using System.Collections.ObjectModel;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
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
    [ObservableProperty] private Guid? _targetId;
    [ObservableProperty] private int _retryMaxAttempts = 1;
    [ObservableProperty] private int _retryDelayMs;
    [ObservableProperty] private NodeRunState _runState;

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
        OutputPorts = new ObservableCollection<PortViewModel>(outputPorts);
        _x = x;
        _y = y;
    }

    public Guid Id { get; }
    public string TypeKey { get; }
    public string Category { get; }

    /// <summary>Action-specific settings, keyed by config-field key.</summary>
    public Dictionary<string, object> Config { get; } = new();
    public string CategoryColor => CategoryColors.ColorFor(Category);
    public IReadOnlyList<PortViewModel> InputPorts { get; }
    public ObservableCollection<PortViewModel> OutputPorts { get; }

    /// <summary>Builds a node from an action definition, deriving ports/category from it.</summary>
    public static NodeViewModel FromDefinition(IActionDefinition definition, Guid id, string label, double x, double y)
    {
        var outCount = definition.OutputPorts.Count;
        var height = NodeLayout.CardHeight(outCount);
        var inputs = definition.InputPorts
            .Select((p, i) => new PortViewModel(p.Name, PortDirection.In, NodeLayout.LeftAnchor(i, definition.InputPorts.Count, height)))
            .ToList();
        var outputs = definition.OutputPorts
            .Select((p, i) => new PortViewModel(p.Name, PortDirection.Out, NodeLayout.RightAnchor(i, outCount, height)))
            .ToList();

        return new NodeViewModel(
            id,
            definition.TypeKey,
            string.IsNullOrEmpty(label) ? definition.DisplayName : label,
            definition.Category,
            inputs,
            outputs,
            x,
            y);
    }

    /// <summary>Builds the output PortViewModel for a 0-based branch index (Run Parallel dynamic ports).
    /// NOTE: count/height are provisional placeholders; Task 2 will supply the real values.</summary>
    public static PortViewModel BranchOutputPort(int zeroBasedIndex) =>
        new(RunParallelAction.BranchPort(zeroBasedIndex + 1), PortDirection.Out, NodeLayout.RightAnchor(zeroBasedIndex, 1, NodeLayout.CardHeight_Default));

    /// <summary>Grows or shrinks the output ports to exactly <paramref name="count"/> branch ports,
    /// preserving existing instances. Non-undoable primitive (used on load and by the undo command's snapshots).</summary>
    public void SetBranchPortCount(int count)
    {
        while (OutputPorts.Count < count)
        {
            OutputPorts.Add(BranchOutputPort(OutputPorts.Count));
        }
        while (OutputPorts.Count > count)
        {
            OutputPorts.RemoveAt(OutputPorts.Count - 1);
        }
    }

    /// <summary>Replaces the output ports with the given instances (used by the undoable branch-count command).</summary>
    public void ReplaceOutputPorts(IReadOnlyList<PortViewModel> ports)
    {
        OutputPorts.Clear();
        foreach (var p in ports)
        {
            OutputPorts.Add(p);
        }
    }
}
