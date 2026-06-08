using AdbCore.Models;

namespace AdbCore.Execution;

/// <summary>What an <see cref="IControlFlowExecutor"/> returns: either a halting failure, or success plus the
/// node from which the parent walk should resume (null = the path ends here).</summary>
public sealed class ControlFlowResult
{
    private ControlFlowResult(WalkOutcome outcome, BotAction? next)
    {
        Outcome = outcome;
        Next = next;
    }

    public WalkOutcome Outcome { get; }
    public BotAction? Next { get; }

    /// <summary>Success; resume the parent walk at <paramref name="next"/> (null ends the path).</summary>
    public static ControlFlowResult Continue(BotAction? next) => new(WalkOutcome.Completed(), next);

    /// <summary>Halt the walk with the given failure outcome.</summary>
    public static ControlFlowResult Halt(WalkOutcome failure) => new(failure, null);
}
