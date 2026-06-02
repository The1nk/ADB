using System.Collections.Concurrent;

namespace AdbCore.Execution;

/// <summary>Run-wide state that flows through an entire bot execution. <see cref="Variables"/> is a
/// concurrent dictionary because parallel branches may read and write it simultaneously.</summary>
public class BotExecutionContext
{
    /// <summary>Variables read/written by actions, keyed by name.</summary>
    public ConcurrentDictionary<string, object> Variables { get; } = new();

    /// <summary>Targets resolved at run start, keyed by <c>BotTarget.Id</c>.</summary>
    public Dictionary<Guid, ResolvedTarget> Targets { get; } = new();
}
