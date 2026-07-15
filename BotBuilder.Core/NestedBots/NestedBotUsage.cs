using AdbCore.Models;

namespace BotBuilder.Core.NestedBots;

/// <summary>Computes how nested-bot library entries are used: per-entry reference counts, and the set of
/// entries transitively reachable from the top-level graph's Nested Bot cards (everything else is an orphan
/// that can be purged).</summary>
public static class NestedBotUsage
{
    /// <summary>Total Nested Bot cards referencing <paramref name="entryId"/> — across the top-level graph
    /// (<paramref name="topLevelRefs"/>) and every library entry's own cards.</summary>
    public static int UsageCount(NestedBotLibrary library, IReadOnlyList<Guid> topLevelRefs, Guid entryId)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(topLevelRefs);

        var count = topLevelRefs.Count(r => r == entryId);
        foreach (var entry in library.Entries)
        {
            count += NestedBotLibrary.ReferencedIds(entry).Count(r => r == entryId);
        }
        return count;
    }

    /// <summary>The set of entry ids transitively reachable from the top-level roots through library
    /// references. (Ids in <paramref name="topLevelRefs"/> that don't match an entry are simply skipped.)</summary>
    public static HashSet<Guid> ReachableFromTopLevel(NestedBotLibrary library, IEnumerable<Guid> topLevelRefs)
    {
        ArgumentNullException.ThrowIfNull(library);

        var reachable = new HashSet<Guid>();
        var stack = new Stack<Guid>(topLevelRefs);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!reachable.Add(current)) { continue; }
            if (library.Get(current) is { } bot)
            {
                foreach (var referenced in NestedBotLibrary.ReferencedIds(bot)) { stack.Push(referenced); }
            }
        }
        return reachable;
    }

    /// <summary>Entry ids NOT reachable from the top-level graph — orphans safe to purge. Reachability, not raw
    /// usage count: an entry referenced only by another orphan is still unused.</summary>
    public static IReadOnlyList<Guid> UnusedEntries(NestedBotLibrary library, IEnumerable<Guid> topLevelRefs)
    {
        ArgumentNullException.ThrowIfNull(library);

        var reachable = ReachableFromTopLevel(library, topLevelRefs);
        return library.Entries.Where(e => !reachable.Contains(e.Id)).Select(e => e.Id).ToList();
    }
}
