using System.Collections.ObjectModel;
using System.Linq;
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
    [ObservableProperty] private string? _subtitle;

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

    /// <summary>True when this node's ports are flipped for a right-to-left serpentine band: inputs on the
    /// Right edge, non-failure outputs on the Left edge. Persisted so a saved-then-reloaded tidy graph stays
    /// clean. Failure (bottom-edge) ports are unaffected.</summary>
    public bool PortsFlipped { get; private set; }

    /// <summary>Action-specific settings, keyed by config-field key.</summary>
    public Dictionary<string, object> Config { get; } = new();
    public string CategoryColor =>
        TypeKey == NestedBotAction.NestedBotTypeKey ? CategoryColors.NestedBot : CategoryColors.ColorFor(Category);
    public IReadOnlyList<PortViewModel> InputPorts { get; }
    public ObservableCollection<PortViewModel> OutputPorts { get; }

    /// <summary>Output ports whose name marks a failure path; these sit on the bottom edge.</summary>
    private static readonly HashSet<string> FailurePortNames = new(StringComparer.Ordinal)
        { JoinAction.SomeFailedPort, "onFailure" };

    private static PortEdge OutputEdge(string portName) =>
        FailurePortNames.Contains(portName) ? PortEdge.Bottom : PortEdge.Right;

    /// <summary>True when <paramref name="portName"/> is a failure output (onFailure/someFailed), which
    /// stays Bottom-designated and is excluded from the single-connection orientation pass.</summary>
    public bool IsFailurePortName(string portName) => FailurePortNames.Contains(portName);

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
        if (PortsFlipped) { SetPortsFlipped(true); }
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
        if (PortsFlipped) { SetPortsFlipped(true); }
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

    /// <summary>Flips (or restores) port sides for a serpentine reversed band. Preserves port instances so
    /// wired connections keep their endpoint identity; only failure/bottom ports are left in place.</summary>
    public void SetPortsFlipped(bool flipped)
    {
        PortsFlipped = flipped;
        var inputEdge = flipped ? PortEdge.Right : PortEdge.Left;
        var outEdge = flipped ? PortEdge.Left : PortEdge.Right;

        for (var i = 0; i < InputPorts.Count; i++)
        {
            var anchor = flipped
                ? NodeLayout.RightAnchor(i, InputPorts.Count, Height)
                : NodeLayout.LeftAnchor(i, InputPorts.Count, Height);
            InputPorts[i].Reposition(inputEdge, anchor);
        }

        var sideOutputs = OutputPorts.Where(p => p.Edge is PortEdge.Left or PortEdge.Right).ToList();
        for (var i = 0; i < sideOutputs.Count; i++)
        {
            var anchor = flipped
                ? NodeLayout.LeftAnchor(i, sideOutputs.Count, Height)
                : NodeLayout.RightAnchor(i, sideOutputs.Count, Height);
            sideOutputs[i].Reposition(outEdge, anchor);
        }
    }

    /// <summary>Restores every port to its canonical band-default edge + anchor: inputs and non-failure
    /// outputs on the side dictated by <see cref="PortsFlipped"/>, failure outputs on Bottom. Unlike
    /// <see cref="SetPortsFlipped"/> this also re-homes any port the single-connection orientation pass
    /// previously parked on Top/Bottom, so that derived pass is idempotent and self-heals after a drag.</summary>
    public void ResetPortEdgesToDefault()
    {
        var inputEdge = PortsFlipped ? PortEdge.Right : PortEdge.Left;
        for (var i = 0; i < InputPorts.Count; i++)
        {
            var anchor = PortsFlipped
                ? NodeLayout.RightAnchor(i, InputPorts.Count, Height)
                : NodeLayout.LeftAnchor(i, InputPorts.Count, Height);
            InputPorts[i].Reposition(inputEdge, anchor);
        }

        var sideEdge = PortsFlipped ? PortEdge.Left : PortEdge.Right;
        var sideOutputs = OutputPorts.Where(p => !FailurePortNames.Contains(p.Name)).ToList();
        for (var i = 0; i < sideOutputs.Count; i++)
        {
            var anchor = PortsFlipped
                ? NodeLayout.LeftAnchor(i, sideOutputs.Count, Height)
                : NodeLayout.RightAnchor(i, sideOutputs.Count, Height);
            sideOutputs[i].Reposition(sideEdge, anchor);
        }

        var failOutputs = OutputPorts.Where(p => FailurePortNames.Contains(p.Name)).ToList();
        for (var j = 0; j < failOutputs.Count; j++)
        {
            failOutputs[j].Reposition(PortEdge.Bottom, NodeLayout.BottomAnchor(j, failOutputs.Count, Height));
        }
    }

    /// <summary>Moves a single input/output port to <paramref name="edge"/> with the centered sole-port
    /// anchor for that edge. Used only by the derived orientation pass on sole-1-1 connections.</summary>
    public void OrientPortTo(PortViewModel port, PortEdge edge) => port.Reposition(edge, SolePortAnchor(edge));

    private CanvasPoint SolePortAnchor(PortEdge edge) => edge switch
    {
        PortEdge.Left => NodeLayout.LeftAnchor(0, 1, Height),
        PortEdge.Right => NodeLayout.RightAnchor(0, 1, Height),
        PortEdge.Bottom => NodeLayout.BottomAnchor(0, 1, Height),
        PortEdge.Top => NodeLayout.TopAnchor(0, 1),
        _ => NodeLayout.RightAnchor(0, 1, Height),
    };
}
