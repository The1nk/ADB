using AdbCore.Models;

namespace AdbCore.Execution;

/// <summary>Options controlling a single bot run.</summary>
public class ExecutionOptions
{
    /// <summary>Targets resolved before the run, keyed by <c>BotTarget.Id</c>.</summary>
    public IReadOnlyDictionary<Guid, ResolvedTarget> ResolvedTargets { get; set; }
        = new Dictionary<Guid, ResolvedTarget>();

    /// <summary>Sink for messages emitted by actions (e.g. the Log action). Optional.</summary>
    public Action<string>? Log { get; set; }

    /// <summary>Variables to seed into the run before execution (used by nested runs that share vars).</summary>
    public IReadOnlyDictionary<string, object>? InitialVariables { get; set; }

    /// <summary>Ids of nested bots already running in this call chain (cycle detection). Empty at top level.</summary>
    public IReadOnlyList<Guid> NestedAncestry { get; set; } = Array.Empty<Guid>();

    /// <summary>When set, the run uses this flat library instead of building one from the bot's own NestedBots
    /// (so a child run inherits the root library unchanged).</summary>
    public IReadOnlyDictionary<Guid, Bot>? NestedBotLibrary { get; set; }

    /// <summary>Binds a nested bot's own targets on demand. Null at the top level (top-level targets are
    /// pre-resolved into <see cref="ResolvedTargets"/>); supplied by the runner so nested runs can bind theirs.</summary>
    public ITargetBinder? TargetBinder { get; set; }
}
