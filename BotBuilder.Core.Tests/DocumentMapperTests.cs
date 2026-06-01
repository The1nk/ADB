using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class DocumentMapperTests
{
    private static ActionRegistry SeededRegistry()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return defs;
    }

    [Fact]
    public void ToBot_ProjectsNodesToActions()
    {
        var registry = SeededRegistry();
        var editor = new BotEditorViewModel(registry) { BotName = "Demo" };
        editor.AddNode("control.start", 7, 8);

        var bot = DocumentMapper.ToBot(editor);

        Assert.Equal("Demo", bot.Name);
        Assert.Equal(editor.BotId, bot.Id);
        var action = Assert.Single(bot.Actions);
        Assert.Equal("control.start", action.TypeKey);
        Assert.Equal(7, action.CanvasPosition.X);
        Assert.Equal(8, action.CanvasPosition.Y);
    }

    [Fact]
    public void Populate_BuildsNodesFromBot_UsingRegistryForPorts()
    {
        var registry = SeededRegistry();
        var bot = new Bot { Name = "Loaded" };
        bot.Actions.Add(new BotAction
        {
            Id = Guid.NewGuid(),
            TypeKey = "data.log",
            Label = "Say hi",
            CanvasPosition = new Position { X = 3, Y = 4 },
        });

        var editor = new BotEditorViewModel(registry);
        DocumentMapper.Populate(editor, bot, registry);

        Assert.Equal("Loaded", editor.BotName);
        var node = Assert.Single(editor.Nodes);
        Assert.Equal("data.log", node.TypeKey);
        Assert.Equal("Say hi", node.Label);
        Assert.Equal(3, node.X);
        Assert.Single(node.InputPorts);
        Assert.Single(node.OutputPorts);
    }

    [Fact]
    public void Populate_UnknownTypeKey_CreatesNodeWithNoPortsAndDefaultCategory()
    {
        var registry = SeededRegistry();
        var bot = new Bot();
        bot.Actions.Add(new BotAction { Id = Guid.NewGuid(), TypeKey = "ghost.unknown", Label = "Ghost" });

        var editor = new BotEditorViewModel(registry);
        DocumentMapper.Populate(editor, bot, registry);

        var node = Assert.Single(editor.Nodes);
        Assert.Equal("ghost.unknown", node.TypeKey);
        Assert.Empty(node.InputPorts);
        Assert.Empty(node.OutputPorts);
        Assert.Equal("Unknown", node.Category);
    }
}
