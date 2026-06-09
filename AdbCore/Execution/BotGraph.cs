using AdbCore.Models;

namespace AdbCore.Execution;

/// <summary>An index over a <see cref="Bot"/>'s actions and connections, built once per run. Replaces the
/// repeated linear <c>FirstOrDefault</c> scans the graph walk would otherwise perform on every hop.</summary>
/// <remarks>If <see cref="Bot.Actions"/> contains duplicate <see cref="BotAction.Id"/> values, the last
/// entry with that id wins in the id lookup; invalid bots are tolerated, not rejected.</remarks>
public sealed class BotGraph
{
    private readonly Dictionary<Guid, BotAction> _byId;
    private readonly Dictionary<Guid, IReadOnlyList<ActionConnection>> _outgoing;

    public BotGraph(Bot bot)
    {
        ArgumentNullException.ThrowIfNull(bot);

        _byId = new Dictionary<Guid, BotAction>(bot.Actions.Count);
        foreach (var action in bot.Actions)
        {
            _byId[action.Id] = action;
        }

        var outgoing = new Dictionary<Guid, List<ActionConnection>>();
        var withIncoming = new HashSet<Guid>();
        foreach (var connection in bot.Connections)
        {
            if (!outgoing.TryGetValue(connection.SourceActionId, out var edges))
            {
                edges = new List<ActionConnection>();
                outgoing[connection.SourceActionId] = edges;
            }
            edges.Add(connection);
            withIncoming.Add(connection.TargetActionId);
        }

        _outgoing = new Dictionary<Guid, IReadOnlyList<ActionConnection>>(outgoing.Count);
        foreach (var kvp in outgoing)
        {
            _outgoing[kvp.Key] = kvp.Value.AsReadOnly();
        }

        EntryPoint = bot.Actions.FirstOrDefault(a => !withIncoming.Contains(a.Id));
    }

    /// <summary>The entry point: the first action (document order) with no incoming connection, or null when
    /// every action has one.</summary>
    public BotAction? EntryPoint { get; }

    /// <summary>The action with the given id, or null.</summary>
    public BotAction? Find(Guid id) => _byId.GetValueOrDefault(id);

    /// <summary>The action reached by following <paramref name="sourcePort"/> out of
    /// <paramref name="fromActionId"/>, or null when that port is unwired. Matches the first connection on
    /// that port in document order.</summary>
    public BotAction? FindNext(Guid fromActionId, string sourcePort)
    {
        if (!_outgoing.TryGetValue(fromActionId, out var edges))
        {
            return null;
        }
        var edge = edges.FirstOrDefault(c => c.SourcePort == sourcePort);
        return edge is null ? null : Find(edge.TargetActionId);
    }

    /// <summary>The outgoing connections from <paramref name="fromActionId"/> (empty when none).</summary>
    public IReadOnlyList<ActionConnection> Outgoing(Guid fromActionId)
        => _outgoing.TryGetValue(fromActionId, out var edges) ? edges : Array.Empty<ActionConnection>();
}
