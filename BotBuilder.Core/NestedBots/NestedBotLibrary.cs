using System.Collections.ObjectModel;
using AdbCore.Actions.BuiltIn;
using AdbCore.Models;

namespace BotBuilder.Core.NestedBots;

/// <summary>The root bot's flat library of reusable nested-bot definitions. Cards reference an entry by id;
/// editing an entry updates every card that uses it. Round-tripped through <c>DocumentMapper</c> into
/// <see cref="Bot.NestedBots"/>.</summary>
public sealed class NestedBotLibrary
{
    private readonly ObservableCollection<Bot> _entries = new();

    public ReadOnlyObservableCollection<Bot> Entries { get; }

    public NestedBotLibrary() => Entries = new ReadOnlyObservableCollection<Bot>(_entries);

    /// <summary>Creates an empty nested bot (fresh id) and adds it to the library.</summary>
    public Bot AddNew(string name = "Untitled Bot")
    {
        var bot = new Bot { Id = Guid.NewGuid(), Name = name };
        _entries.Add(bot);
        return bot;
    }

    public Bot? Get(Guid id) => _entries.FirstOrDefault(b => b.Id == id);

    public void Rename(Guid id, string name)
    {
        if (Get(id) is { } bot) { bot.Name = name; }
    }

    public bool Remove(Guid id)
    {
        if (Get(id) is { } bot) { return _entries.Remove(bot); }
        return false;
    }

    /// <summary>Replaces all entries (used when loading a document).</summary>
    public void Load(IEnumerable<Bot> entries)
    {
        _entries.Clear();
        foreach (var b in entries) { _entries.Add(b); }
    }

    /// <summary>Would a card inside <paramref name="hostId"/> referencing <paramref name="candidateId"/> create a
    /// reference cycle? True for a self-reference, or when <paramref name="hostId"/> is reachable from
    /// <paramref name="candidateId"/> through existing nested-bot references.</summary>
    public bool WouldCreateCycle(Guid hostId, Guid candidateId)
    {
        if (candidateId == hostId) { return true; }

        var visited = new HashSet<Guid>();
        var stack = new Stack<Guid>();
        stack.Push(candidateId);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current == hostId) { return true; }
            if (!visited.Add(current)) { continue; }
            if (Get(current) is { } bot)
            {
                foreach (var referenced in ReferencedIds(bot)) { stack.Push(referenced); }
            }
        }
        return false;
    }

    /// <summary>The nested-bot ids referenced by a bot's Nested Bot action cards.</summary>
    private static IEnumerable<Guid> ReferencedIds(Bot bot)
    {
        foreach (var action in bot.Actions)
        {
            if (action.TypeKey != NestedBotAction.NestedBotTypeKey) { continue; }
            if (action.Config.TryGetValue(NestedBotAction.NestedBotIdKey, out var raw)
                && Guid.TryParse(raw?.ToString(), out var id))
            {
                yield return id;
            }
        }
    }

    /// <summary>Imports an external bot as a new library entry, deep-copied and detached from the source. The
    /// external's own nested library is flattened into this library; every imported bot gets a fresh id and all
    /// <c>nestedBotId</c> references are remapped. Returns the new top-level entry.</summary>
    public Bot Import(Bot external)
    {
        ArgumentNullException.ThrowIfNull(external);

        var sources = new List<Bot> { external };
        sources.AddRange(external.NestedBots);
        var idMap = sources.ToDictionary(b => b.Id, _ => Guid.NewGuid());

        Bot top = null!;
        foreach (var src in sources)
        {
            var clone = CloneBot(src, idMap);
            _entries.Add(clone);
            if (ReferenceEquals(src, external)) { top = clone; }
        }
        return top;
    }

    private static Bot CloneBot(Bot src, IReadOnlyDictionary<Guid, Guid> idMap) => new()
    {
        Id = idMap[src.Id],
        Name = src.Name,
        Description = src.Description,
        Targets = src.Targets.Select(CloneTarget).ToList(),
        Actions = src.Actions.Select(a => CloneAction(a, idMap)).ToList(),
        Connections = src.Connections.Select(CloneConnection).ToList(),
        // NestedBots intentionally left empty — flattened into the library.
    };

    private static BotAction CloneAction(BotAction a, IReadOnlyDictionary<Guid, Guid> idMap)
    {
        var config = new Dictionary<string, object>(a.Config);
        if (a.TypeKey == NestedBotAction.NestedBotTypeKey
            && config.TryGetValue(NestedBotAction.NestedBotIdKey, out var raw)
            && Guid.TryParse(raw?.ToString(), out var oldRef)
            && idMap.TryGetValue(oldRef, out var newRef))
        {
            config[NestedBotAction.NestedBotIdKey] = newRef.ToString();
        }

        return new BotAction
        {
            Id = a.Id, // kept — scoped to this bot's graph
            TypeKey = a.TypeKey,
            Label = a.Label,
            TargetId = a.TargetId,
            Config = config,
            Retry = a.Retry is null ? null : new RetryPolicy { MaxAttempts = a.Retry.MaxAttempts, DelayMs = a.Retry.DelayMs },
            CanvasPosition = new Position { X = a.CanvasPosition.X, Y = a.CanvasPosition.Y },
        };
    }

    private static ActionConnection CloneConnection(ActionConnection c) => new()
    {
        Id = c.Id,
        SourceActionId = c.SourceActionId,
        SourcePort = c.SourcePort,
        TargetActionId = c.TargetActionId,
        TargetPort = c.TargetPort,
    };

    private static BotTarget CloneTarget(BotTarget t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        Type = t.Type,
        Config = new Dictionary<string, string>(t.Config),
    };
}
