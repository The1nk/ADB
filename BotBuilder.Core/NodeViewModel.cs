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
    [ObservableProperty] private double _height;

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

    /// <summary>Output ports whose name marks a failure path; these sit on the bottom edge.</summary>
    private static readonly HashSet<string> FailurePortNames = new(StringComparer.Ordinal)
        { JoinAction.SomeFailedPort, "onFailure" };

    private static PortEdge OutputEdge(string portName) =>
        FailurePortNames.Contains(portName) ? PortEdge.Bottom : PortEdge.Right;

    /// <summary>Builds a node from an action definition, deriving ports/category from it. Right-edge outputs
    /// drive the card height; failure outputs (onFailure/someFailed) drop to the bottom edge.</summary>
    public static NodeViewModel FromDefinition(IActionDefinition definition, Guid id, string label, double x, double y)
    {
        var rightNames = definition.OutputPorts.Where(p => OutputEdge(p.Name) == PortEdge.Right).Select(p => p.Name).ToList();
        var bottomNames = definition.OutputPorts.Where(p => OutputEdge(p.Name) == PortEdge.Bottom).Select(p => p.Name).ToList();
        var height = NodeLayout.CardHeight(rightNames.Count);

        var inputs = definition.InputPorts
            .Select((p, i) => new PortViewModel(p.Name, PortDirection.In, PortEdge.Left, NodeLayout.LeftAnchor(i, definition.InputPorts.Count, height)))
            .ToList();

        var outputs = new List<PortViewModel>(rightNames.Count + bottomNames.Count);
        for (var i = 0; i < rightNames.Count; i++)
        {
            outputs.Add(new PortViewModel(rightNames[i], PortDirection.Out, PortEdge.Right, NodeLayout.RightAnchor(i, rightNames.Count, height)));
        }
        for (var j = 0; j < bottomNames.Count; j++)
        {
            outputs.Add(new PortViewModel(bottomNames[j], PortDirection.Out, PortEdge.Bottom, NodeLayout.BottomAnchor(j, bottomNames.Count, height)));
        }

        var node = new NodeViewModel(
            id,
            definition.TypeKey,
            string.IsNullOrEmpty(label) ? definition.DisplayName : label,
            definition.Category,
            inputs,
            outputs,
            x,
            y);
        node.Height = height;
        return node;
    }

    /// <summary>Builds the output PortViewModel for a 0-based Run Parallel branch index (right edge).
    /// The anchor is a placeholder; the owning node immediately re-anchors all branch ports via
    /// <see cref="ReplaceOutputPorts"/> (grow path) or <see cref="SetBranchPortCount"/> once it recomputes
    /// its layout for the final branch count/height.</summary>
    public static PortViewModel BranchOutputPort(int zeroBasedIndex) =>
        new(RunParallelAction.BranchPort(zeroBasedIndex + 1), PortDirection.Out, PortEdge.Right, default);

    /// <summary>Sets the Run Parallel output ports to exactly <paramref name="count"/> right-edge branch ports,
    /// re-centering them and growing/shrinking the card height. Surviving port instances are preserved so wired
    /// connections keep their endpoint identity. (All Run Parallel outputs are right-edge — no failure ports.)</summary>
    public void SetBranchPortCount(int count)
    {
        var height = NodeLayout.CardHeight(count);
        while (OutputPorts.Count < count)
        {
            OutputPorts.Add(new PortViewModel(
                RunParallelAction.BranchPort(OutputPorts.Count + 1), PortDirection.Out, PortEdge.Right, default));
        }
        while (OutputPorts.Count > count)
        {
            OutputPorts.RemoveAt(OutputPorts.Count - 1);
        }

        ReanchorRightOutputsAndInputs(height);
        Height = height;
    }

    /// <summary>Replaces the output ports with the given instances (used by the undoable branch-count command).
    /// Recomputes the card height from the new right-port count and re-anchors all ports + inputs so the node
    /// stays self-consistent when called directly.</summary>
    public void ReplaceOutputPorts(IReadOnlyList<PortViewModel> ports)
    {
        OutputPorts.Clear();
        foreach (var p in ports)
        {
            OutputPorts.Add(p);
        }

        var rightCount = OutputPorts.Count(p => p.Edge == PortEdge.Right);
        var height = NodeLayout.CardHeight(rightCount);
        ReanchorRightOutputsAndInputs(height);
        Height = height;
    }

    /// <summary>Re-places right-edge outputs and all inputs onto the given height, centering each block.
    /// (Right-edge outputs are the only ones that affect height; this is the Run Parallel / replace path,
    /// where there are no bottom-edge ports.)</summary>
    private void ReanchorRightOutputsAndInputs(double height)
    {
        var rightCount = OutputPorts.Count(p => p.Edge == PortEdge.Right);
        var ri = 0;
        foreach (var port in OutputPorts)
        {
            if (port.Edge == PortEdge.Right)
            {
                port.MoveTo(NodeLayout.RightAnchor(ri++, rightCount, height));
            }
        }
        for (var i = 0; i < InputPorts.Count; i++)
        {
            InputPorts[i].MoveTo(NodeLayout.LeftAnchor(i, InputPorts.Count, height));
        }
    }
}
