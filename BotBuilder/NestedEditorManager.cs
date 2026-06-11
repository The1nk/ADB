using System.Collections.Generic;
using System.Windows;
using AdbCore.Actions;
using BotBuilder.Core;
using BotBuilder.Core.NestedBots;

namespace BotBuilder;

/// <summary>Owns the modeless child editor windows: one window per nested-bot id (re-open focuses the existing
/// one). Because the library is flat at the root, a single manager serves every nesting depth.</summary>
public sealed class NestedEditorManager
{
    private readonly Dictionary<Guid, MainWindow> _open = new();
    private readonly ActionRegistry _registry;
    private readonly BotEditorViewModel _rootEditor;
    private readonly Window _rootWindow;
    private readonly Action _saveRoot;

    public NestedEditorManager(ActionRegistry registry, BotEditorViewModel rootEditor, Window rootWindow, Action saveRoot)
    {
        _registry = registry;
        _rootEditor = rootEditor;
        _rootWindow = rootWindow;
        _saveRoot = saveRoot;
    }

    public void OpenOrFocus(Guid nestedBotId)
    {
        if (_open.TryGetValue(nestedBotId, out var existing))
        {
            existing.Activate();
            return;
        }

        var session = NestedBotEditorSession.Open(nestedBotId, _registry, _rootEditor.NestedBotLibrary);
        var child = new MainWindow(session, _rootEditor.BotName, this, _saveRoot, () => OnChildClosed(nestedBotId, session));
        child.Owner = _rootWindow;
        _open[nestedBotId] = child;
        child.Show();
    }

    private void OnChildClosed(Guid id, NestedBotEditorSession session)
    {
        session.SyncBack();                        // persist edits into the in-memory library
        _rootEditor.MarkDirty();                   // the parent document now has unsaved changes
        _rootEditor.RefreshNestedBotSubtitles();
        _open.Remove(id);
    }
}
