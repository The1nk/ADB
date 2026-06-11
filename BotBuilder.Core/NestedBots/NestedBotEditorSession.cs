using AdbCore.Actions;

namespace BotBuilder.Core.NestedBots;

/// <summary>A child editing session for one library entry: builds a <see cref="BotEditorViewModel"/> populated
/// from the entry (sharing the registry + root library) and syncs edits back into the entry. The WPF layer hosts
/// <see cref="Editor"/> in a child window and calls <see cref="SyncBack"/> on save/close.</summary>
public sealed class NestedBotEditorSession
{
    private readonly NestedBotLibrary _library;

    private NestedBotEditorSession(Guid nestedBotId, BotEditorViewModel editor, NestedBotLibrary library)
    {
        NestedBotId = nestedBotId;
        Editor = editor;
        _library = library;
    }

    public Guid NestedBotId { get; }
    public BotEditorViewModel Editor { get; }

    /// <summary>Builds a child editor for the library entry <paramref name="nestedBotId"/>.</summary>
    public static NestedBotEditorSession Open(Guid nestedBotId, ActionRegistry registry, NestedBotLibrary library)
    {
        var entry = library.Get(nestedBotId)
            ?? throw new InvalidOperationException($"Nested bot '{nestedBotId}' is not in the library.");
        var editor = new BotEditorViewModel(registry, library);
        DocumentMapper.Populate(editor, entry, registry, includeLibrary: false);
        editor.MarkSavedClean(); // a freshly-opened entry isn't an unsaved edit
        return new NestedBotEditorSession(nestedBotId, editor, library);
    }

    /// <summary>Writes the child editor's current graph back into its library entry (in place).</summary>
    public void SyncBack() => _library.Replace(DocumentMapper.ToBot(Editor, includeLibrary: false));
}
