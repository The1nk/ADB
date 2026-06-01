namespace BotBuilder.Core.Undo;

/// <summary>A linear undo/redo history of <see cref="IUndoableCommand"/>s.</summary>
public sealed class UndoStack
{
    private readonly Stack<IUndoableCommand> _undo = new();
    private readonly Stack<IUndoableCommand> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Runs the command's <see cref="IUndoableCommand.Do"/>, then records it.</summary>
    public void Execute(IUndoableCommand command)
    {
        command.Do();
        PushExecuted(command);
    }

    /// <summary>Records a command whose effect was already applied (e.g. a live drag).</summary>
    public void PushExecuted(IUndoableCommand command)
    {
        _undo.Push(command);
        _redo.Clear();
    }

    public void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }
        var command = _undo.Pop();
        command.Undo();
        _redo.Push(command);
    }

    public void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }
        var command = _redo.Pop();
        command.Do();
        _undo.Push(command);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
