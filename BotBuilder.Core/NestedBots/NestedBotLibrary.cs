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
}
