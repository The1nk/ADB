namespace BotBuilder.Core.Undo;

/// <summary>A reversible editing operation.</summary>
public interface IUndoableCommand
{
    /// <summary>Apply (or re-apply, on redo) the change.</summary>
    void Do();

    /// <summary>Reverse the change.</summary>
    void Undo();
}
