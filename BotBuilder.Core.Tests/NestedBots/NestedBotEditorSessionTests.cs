using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using BotBuilder.Core.NestedBots;
using Xunit;

namespace BotBuilder.Core.Tests.NestedBots;

public class NestedBotEditorSessionTests
{
    private static ActionRegistry Registry()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return defs;
    }

    [Fact]
    public void Open_BuildsChildEditorPopulatedFromEntry_SharingLibrary()
    {
        var reg = Registry();
        var lib = new NestedBotLibrary();
        var entry = lib.AddNew("Sub");

        var session = NestedBotEditorSession.Open(entry.Id, reg, lib);

        Assert.Equal(entry.Id, session.Editor.BotId);
        Assert.Equal("Sub", session.Editor.BotName);
        Assert.Same(lib, session.Editor.NestedBotLibrary); // shares the root library
    }

    [Fact]
    public void SyncBack_WritesChildGraphIntoLibraryEntry()
    {
        var reg = Registry();
        var lib = new NestedBotLibrary();
        var entry = lib.AddNew("Sub");
        var session = NestedBotEditorSession.Open(entry.Id, reg, lib);

        session.Editor.AddNode("control.start", 10, 10); // edit the child

        session.SyncBack();

        Assert.Single(lib.Get(entry.Id)!.Actions); // the edit landed in the library entry
        Assert.Empty(lib.Get(entry.Id)!.NestedBots); // entry didn't absorb the shared library
    }

    [Fact]
    public void Open_UnknownId_Throws()
    {
        var reg = Registry();
        var lib = new NestedBotLibrary();
        Assert.Throws<InvalidOperationException>(() => NestedBotEditorSession.Open(Guid.NewGuid(), reg, lib));
    }
}
