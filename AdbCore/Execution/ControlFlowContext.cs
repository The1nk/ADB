using AdbCore.Models;

namespace AdbCore.Execution;

/// <summary>Everything an <see cref="IControlFlowExecutor"/> needs to orchestrate sub-walks: the graph index,
/// the control-flow node being executed, run-wide state, the log sink, and a callback to walk a sub-path.</summary>
public sealed class ControlFlowContext
{
    private readonly Func<BotAction?, Guid?, CancellationToken, Task<WalkOutcome>> _walk;

    public ControlFlowContext(
        BotGraph graph,
        BotAction action,
        BotExecutionContext runContext,
        Action<string> log,
        Func<BotAction?, Guid?, CancellationToken, Task<WalkOutcome>> walk)
    {
        Graph = graph;
        Action = action;
        RunContext = runContext;
        Log = log;
        _walk = walk;
    }

    /// <summary>The per-run action/connection index.</summary>
    public BotGraph Graph { get; }

    /// <summary>The control-flow node being executed.</summary>
    public BotAction Action { get; }

    /// <summary>Run-wide state (variables, resolved targets).</summary>
    public BotExecutionContext RunContext { get; }

    /// <summary>Emits a message to the run log sink.</summary>
    public Action<string> Log { get; }

    /// <summary>Walks a sub-path from <paramref name="start"/>, optionally stopping before
    /// <paramref name="stopBeforeId"/> (used to halt parallel branches at their convergent Join).</summary>
    public Task<WalkOutcome> WalkAsync(BotAction? start, CancellationToken ct, Guid? stopBeforeId = null)
        => _walk(start, stopBeforeId, ct);
}
