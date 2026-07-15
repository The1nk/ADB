using System.Collections.ObjectModel;
using AdbCore.Actions.BuiltIn;

namespace BotBuilder.Core.NestedBots;

/// <summary>A single row in the Nested Bot Library manager: an entry's id, name, action count, and how many
/// Nested Bot cards reference it (top-level + nested).</summary>
public sealed record NestedBotEntryRow(Guid Id, string Name, int ActionCount, int UsageCount);

/// <summary>View-model for the "Manage Nested Bot Library" dialog: lists library entries with usage counts and
/// removes entries (one, or all orphans unreachable from the top-level graph), keeping the editor in sync.</summary>
public sealed class NestedBotLibraryManagerViewModel
{
    private readonly BotEditorViewModel _editor;

    public ObservableCollection<NestedBotEntryRow> Entries { get; } = new();

    public NestedBotLibraryManagerViewModel(BotEditorViewModel editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        _editor = editor;
        Refresh();
    }

    /// <summary>Rebuilds the rows from the current library + top-level references.</summary>
    public void Refresh()
    {
        var topLevel = TopLevelRefs();
        Entries.Clear();
        foreach (var entry in _editor.NestedBotLibrary.Entries)
        {
            Entries.Add(new NestedBotEntryRow(
                entry.Id, entry.Name, entry.Actions.Count,
                NestedBotUsage.UsageCount(_editor.NestedBotLibrary, topLevel, entry.Id)));
        }
    }

    /// <summary>Removes one entry; marks the document dirty and refreshes card subtitles + rows. Returns false
    /// when the entry wasn't in the library.</summary>
    public bool Remove(Guid id)
    {
        if (!_editor.NestedBotLibrary.Remove(id)) { return false; }
        AfterMutation();
        return true;
    }

    /// <summary>Removes every entry not reachable from the top-level graph. Returns how many were removed.</summary>
    public int RemoveUnused()
    {
        var unused = NestedBotUsage.UnusedEntries(_editor.NestedBotLibrary, TopLevelRefs());
        foreach (var id in unused) { _editor.NestedBotLibrary.Remove(id); }
        if (unused.Count > 0) { AfterMutation(); }
        return unused.Count;
    }

    private void AfterMutation()
    {
        _editor.MarkDirty();
        _editor.RefreshNestedBotSubtitles();
        Refresh();
    }

    private IReadOnlyList<Guid> TopLevelRefs()
    {
        var refs = new List<Guid>();
        foreach (var node in _editor.Nodes)
        {
            if (node.TypeKey == NestedBotAction.NestedBotTypeKey
                && node.Config.TryGetValue(NestedBotAction.NestedBotIdKey, out var raw)
                && Guid.TryParse(raw?.ToString(), out var id))
            {
                refs.Add(id);
            }
        }
        return refs;
    }
}
