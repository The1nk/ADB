using AdbCore.Models;

namespace AdbCore.Execution;

/// <summary>Everything an <see cref="IActionExecutor"/> needs to run one action.</summary>
public class ActionExecutionContext
{
    public ActionExecutionContext(BotAction action, BotExecutionContext context, Action<string> log)
    {
        Action = action;
        Context = context;
        Log = log;
    }

    /// <summary>The action node being executed (its config, target, retry).</summary>
    public BotAction Action { get; }

    /// <summary>The run-wide context (variables, resolved targets).</summary>
    public BotExecutionContext Context { get; }

    /// <summary>Emits a message to the run log sink.</summary>
    public Action<string> Log { get; }

    /// <summary>Convenience accessor for <see cref="BotExecutionContext.Variables"/>.</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<string, object> Variables => Context.Variables;
}
