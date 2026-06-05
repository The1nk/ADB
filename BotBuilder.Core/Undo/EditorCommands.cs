using AdbCore.Actions.BuiltIn;
using BotBuilder.Core.Connections;

namespace BotBuilder.Core.Undo;

internal sealed class AddNodeCommand : IUndoableCommand
{
    private readonly BotEditorViewModel _editor;
    private readonly NodeViewModel _node;
    public AddNodeCommand(BotEditorViewModel editor, NodeViewModel node) { _editor = editor; _node = node; }
    public void Do() => _editor.AddNodeCore(_node);
    public void Undo() => _editor.RemoveNodeCore(_node);
}

internal sealed class MoveNodeCommand : IUndoableCommand
{
    private readonly NodeViewModel _node;
    private readonly double _oldX, _oldY, _newX, _newY;
    public MoveNodeCommand(NodeViewModel node, double oldX, double oldY, double newX, double newY)
    { _node = node; _oldX = oldX; _oldY = oldY; _newX = newX; _newY = newY; }
    public void Do() { _node.X = _newX; _node.Y = _newY; }
    public void Undo() { _node.X = _oldX; _node.Y = _oldY; }
}

/// <summary>Moves a set of nodes (a multi-selection drag) as one undoable step.</summary>
internal sealed class MoveNodesCommand : IUndoableCommand
{
    private readonly IReadOnlyList<(NodeViewModel Node, double OldX, double OldY, double NewX, double NewY)> _moves;
    public MoveNodesCommand(IReadOnlyList<(NodeViewModel, double, double, double, double)> moves) { _moves = moves; }
    public void Do() { foreach (var m in _moves) { m.Node.X = m.NewX; m.Node.Y = m.NewY; } }
    public void Undo() { foreach (var m in _moves) { m.Node.X = m.OldX; m.Node.Y = m.OldY; } }
}

internal sealed class ConnectCommand : IUndoableCommand
{
    private readonly BotEditorViewModel _editor;
    private readonly ConnectionViewModel _connection;
    public ConnectCommand(BotEditorViewModel editor, ConnectionViewModel connection) { _editor = editor; _connection = connection; }
    public void Do() => _editor.AddConnectionCore(_connection);
    public void Undo() => _editor.RemoveConnectionCore(_connection);
}

internal sealed class DisconnectCommand : IUndoableCommand
{
    private readonly BotEditorViewModel _editor;
    private readonly ConnectionViewModel _connection;
    public DisconnectCommand(BotEditorViewModel editor, ConnectionViewModel connection) { _editor = editor; _connection = connection; }
    public void Do() => _editor.RemoveConnectionCore(_connection);
    public void Undo() => _editor.AddConnectionCore(_connection);
}

/// <summary>Removes a set of nodes and a set of connections; undo restores all of them.</summary>
internal sealed class DeleteNodesCommand : IUndoableCommand
{
    private readonly BotEditorViewModel _editor;
    private readonly IReadOnlyList<NodeViewModel> _nodes;
    private readonly IReadOnlyList<ConnectionViewModel> _connections;

    public DeleteNodesCommand(
        BotEditorViewModel editor,
        IReadOnlyList<NodeViewModel> nodes,
        IReadOnlyList<ConnectionViewModel> connections)
    {
        _editor = editor;
        _nodes = nodes;
        _connections = connections;
    }

    public void Do()
    {
        foreach (var c in _connections) { _editor.RemoveConnectionCore(c); }
        foreach (var n in _nodes) { _editor.RemoveNodeCore(n); }
    }

    public void Undo()
    {
        foreach (var n in _nodes) { _editor.AddNodeCore(n); }
        foreach (var c in _connections) { _editor.AddConnectionCore(c); }
    }
}

/// <summary>Adds a pasted set of nodes and connections; undo removes them.</summary>
internal sealed class PasteCommand : IUndoableCommand
{
    private readonly BotEditorViewModel _editor;
    private readonly IReadOnlyList<NodeViewModel> _nodes;
    private readonly IReadOnlyList<ConnectionViewModel> _connections;

    public PasteCommand(BotEditorViewModel editor, IReadOnlyList<NodeViewModel> nodes, IReadOnlyList<ConnectionViewModel> connections)
    {
        _editor = editor;
        _nodes = nodes;
        _connections = connections;
    }

    public void Do()
    {
        foreach (var n in _nodes) { _editor.AddNodeCore(n); }
        foreach (var c in _connections) { _editor.AddConnectionCore(c); }
    }

    public void Undo()
    {
        foreach (var c in _connections) { _editor.RemoveConnectionCore(c); }
        foreach (var n in _nodes) { _editor.RemoveNodeCore(n); }
    }
}

/// <summary>Changes a Run Parallel node's branch-port count: swaps its output ports and removes the
/// connections orphaned by a shrink. Undo restores the previous ports and re-adds those connections.</summary>
internal sealed class SetBranchCountCommand : IUndoableCommand
{
    private readonly BotEditorViewModel _editor;
    private readonly NodeViewModel _node;
    private readonly IReadOnlyList<PortViewModel> _oldPorts;
    private readonly IReadOnlyList<PortViewModel> _newPorts;
    private readonly int _oldCount;
    private readonly int _newCount;
    private readonly IReadOnlyList<ConnectionViewModel> _removedConnections;

    public SetBranchCountCommand(
        BotEditorViewModel editor,
        NodeViewModel node,
        IReadOnlyList<PortViewModel> oldPorts,
        IReadOnlyList<PortViewModel> newPorts,
        int oldCount,
        int newCount,
        IReadOnlyList<ConnectionViewModel> removedConnections)
    {
        _editor = editor;
        _node = node;
        _oldPorts = oldPorts;
        _newPorts = newPorts;
        _oldCount = oldCount;
        _newCount = newCount;
        _removedConnections = removedConnections;
    }

    public void Do()
    {
        _node.Config[RunParallelAction.BranchesKey] = _newCount;
        _node.ReplaceOutputPorts(_newPorts);
        foreach (var c in _removedConnections) { _editor.RemoveConnectionCore(c); }
    }

    public void Undo()
    {
        _node.Config[RunParallelAction.BranchesKey] = _oldCount;
        _node.ReplaceOutputPorts(_oldPorts);
        foreach (var c in _removedConnections) { _editor.AddConnectionCore(c); }
    }
}
