using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using BotBuilder.Core;
using BotBuilder.Core.NestedBots;
using Xunit;

namespace BotBuilder.Core.Tests.Properties;

public class NestedBotCycleGuardTests
{
    private static BotEditorViewModel NestedEditorFor(NestedBotLibrary lib, Guid entryId, out ActionRegistry reg)
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        reg = defs;
        var editor = new BotEditorViewModel(defs, lib);
        DocumentMapper.Populate(editor, lib.Get(entryId)!, defs, includeLibrary: false);
        return editor;
    }

    [Fact]
    public void AssigningACyclicReference_IsBlocked()
    {
        // Library: A and B. B already references A. Editing A, try to point a card at B -> would cycle A->B->A.
        var lib = new NestedBotLibrary();
        var a = lib.AddNew("A");
        var b = lib.AddNew("B");
        b.Actions.Add(new BotAction { Id = Guid.NewGuid(), TypeKey = NestedBotAction.NestedBotTypeKey,
            Config = { [NestedBotAction.NestedBotIdKey] = a.Id.ToString() } });

        var editorA = NestedEditorFor(lib, a.Id, out _);
        var card = editorA.AddNode(NestedBotAction.NestedBotTypeKey, 0, 0);
        editorA.Select(card);

        editorA.Properties.SelectedNestedBotId = b.Id; // would create A->B->A

        Assert.Null(editorA.Properties.SelectedNestedBotId);          // assignment rejected
        Assert.False(card.Config.ContainsKey(NestedBotAction.NestedBotIdKey));
        Assert.NotNull(editorA.Properties.CycleWarning);              // warning surfaced
    }

    [Fact]
    public void AssigningANonCyclicReference_Succeeds_AndClearsWarning()
    {
        var lib = new NestedBotLibrary();
        var a = lib.AddNew("A");
        var b = lib.AddNew("B"); // B does NOT reference A

        var editorA = NestedEditorFor(lib, a.Id, out _);
        var card = editorA.AddNode(NestedBotAction.NestedBotTypeKey, 0, 0);
        editorA.Select(card);

        editorA.Properties.SelectedNestedBotId = b.Id;

        Assert.Equal(b.Id, editorA.Properties.SelectedNestedBotId);
        Assert.Null(editorA.Properties.CycleWarning);
    }
}
