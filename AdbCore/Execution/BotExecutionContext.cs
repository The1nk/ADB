using System.Collections.Concurrent;
using AdbCore.Models;

namespace AdbCore.Execution;

/// <summary>Run-wide state that flows through an entire bot execution. <see cref="Variables"/> is a
/// concurrent dictionary because parallel branches may read and write it simultaneously.</summary>
public class BotExecutionContext
{
    /// <summary>Variables read/written by actions, keyed by name.</summary>
    public ConcurrentDictionary<string, object> Variables { get; } = new();

    /// <summary>Targets resolved at run start, keyed by <c>BotTarget.Id</c>.</summary>
    public Dictionary<Guid, ResolvedTarget> Targets { get; } = new();

    /// <summary>The root bot's flat nested-bot library (id -> definition), threaded unchanged into child runs.</summary>
    public IReadOnlyDictionary<Guid, Bot> NestedBots { get; set; } = new Dictionary<Guid, Bot>();

    /// <summary>Ids of nested bots currently executing in this call chain, for cycle detection.</summary>
    public IReadOnlyList<Guid> NestedAncestry { get; set; } = Array.Empty<Guid>();

    /// <summary>This bot's target id -> name, so a nested run can match shared targets by name.</summary>
    public IReadOnlyDictionary<Guid, string> TargetNames { get; set; } = new Dictionary<Guid, string>();

    /// <summary>On-demand binder for a nested bot's own targets (null when none was supplied).</summary>
    public ITargetBinder? TargetBinder { get; set; }
}
