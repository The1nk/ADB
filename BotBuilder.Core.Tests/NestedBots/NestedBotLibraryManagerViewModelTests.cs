using System;
using System.Linq;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using BotBuilder.Core.NestedBots;
using Xunit;

namespace BotBuilder.Core.Tests.NestedBots;

public class NestedBotLibraryManagerViewModelTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    private static void AddTopLevelCard(BotEditorViewModel editor, Guid entryId)
    {
        var node = editor.AddNode(NestedBotAction.NestedBotTypeKey, 0, 0);
        node.Config[NestedBotAction.NestedBotIdKey] = entryId.ToString();
    }

    [Fact]
    public void Rows_ReportNameActionCountAndUsage()
    {
        var editor = NewEditor();
        var used = editor.NestedBotLibrary.AddNew("Used");
        var orphan = editor.NestedBotLibrary.AddNew("Orphan");
        AddTopLevelCard(editor, used.Id);

        var vm = new NestedBotLibraryManagerViewModel(editor);

        var usedRow = vm.Entries.Single(r => r.Id == used.Id);
        var orphanRow = vm.Entries.Single(r => r.Id == orphan.Id);
        Assert.Equal("Used", usedRow.Name);
        Assert.Equal(1, usedRow.UsageCount);
        Assert.Equal(0, orphanRow.UsageCount);
    }

    [Fact]
    public void Remove_DropsEntry_MarksDirty_Refreshes()
    {
        var editor = NewEditor();
        var orphan = editor.NestedBotLibrary.AddNew("Orphan");
        editor.MarkSavedClean();
        var vm = new NestedBotLibraryManagerViewModel(editor);

        var removed = vm.Remove(orphan.Id);

        Assert.True(removed);
        Assert.Empty(editor.NestedBotLibrary.Entries);
        Assert.DoesNotContain(vm.Entries, r => r.Id == orphan.Id);
        Assert.True(editor.IsDirty);
    }

    [Fact]
    public void RemoveUnused_PurgesOnlyUnreachableEntries()
    {
        var editor = NewEditor();
        var used = editor.NestedBotLibrary.AddNew("Used");
        editor.NestedBotLibrary.AddNew("OrphanA");
        editor.NestedBotLibrary.AddNew("OrphanB");
        AddTopLevelCard(editor, used.Id);
        editor.MarkSavedClean();
        var vm = new NestedBotLibraryManagerViewModel(editor);

        var count = vm.RemoveUnused();

        Assert.Equal(2, count);
        Assert.Equal(new[] { used.Id }, editor.NestedBotLibrary.Entries.Select(e => e.Id).ToArray());
        Assert.True(editor.IsDirty);
    }

    [Fact]
    public void RemoveUnused_NothingUnused_NoDirty()
    {
        var editor = NewEditor();
        var used = editor.NestedBotLibrary.AddNew("Used");
        AddTopLevelCard(editor, used.Id);
        editor.MarkSavedClean();
        var vm = new NestedBotLibraryManagerViewModel(editor);

        var count = vm.RemoveUnused();

        Assert.Equal(0, count);
        Assert.False(editor.IsDirty);
    }
}
