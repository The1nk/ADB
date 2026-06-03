namespace BotBuilder.Core.Integration;

/// <summary>Overall state of a Test Run.</summary>
public enum RunStatus { Idle, Running, Succeeded, Failed }

/// <summary>Folds runner log entries into per-node run states and an overall <see cref="Status"/>, for
/// highlighting the canvas during a Test Run.</summary>
public sealed class RunStatusTracker
{
    private readonly Dictionary<Guid, NodeRunState> _nodeStates = new();

    public RunStatus Status { get; private set; } = RunStatus.Idle;
    public IReadOnlyDictionary<Guid, NodeRunState> NodeStates => _nodeStates;

    public void Reset()
    {
        Status = RunStatus.Idle;
        _nodeStates.Clear();
    }

    /// <summary>Updates state from a parsed log entry. Returns the node id whose state changed (so the UI
    /// can repaint just that node), or null when nothing node-specific changed.</summary>
    public Guid? Apply(RunLogEntry entry)
    {
        switch (entry.Kind)
        {
            case RunLogKind.RunStart:
                Status = RunStatus.Running;
                _nodeStates.Clear();
                return null;

            case RunLogKind.Action when Guid.TryParse(entry.ActionId, out var id):
                _nodeStates[id] = entry.Success == true ? NodeRunState.Succeeded : NodeRunState.Failed;
                return id;

            case RunLogKind.RunEnd:
                Status = entry.Success == true ? RunStatus.Succeeded : RunStatus.Failed;
                return null;

            default:
                return null;
        }
    }
}
