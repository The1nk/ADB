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
