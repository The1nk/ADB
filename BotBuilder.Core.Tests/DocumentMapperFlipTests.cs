using System.Linq;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class DocumentMapperFlipTests
{
    private static (BotEditorViewModel editor, ActionRegistry defs) NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return (new BotEditorViewModel(defs), defs);
    }

    [Fact]
    public void ToBot_Then_Populate_RoundTripsPortsFlipped()
    {
        var (editor, defs) = NewEditor();
        var node = editor.AddNode("control.delay", 0, 0);
        node.SetPortsFlipped(true);

        var bot = DocumentMapper.ToBot(editor);
        Assert.True(bot.Actions.Single().PortsFlipped);

        var (editor2, defs2) = NewEditor();
        DocumentMapper.Populate(editor2, bot, defs2);
        Assert.True(editor2.Nodes.Single().PortsFlipped);
        Assert.Equal(PortEdge.Left, editor2.Nodes.Single().OutputPorts[0].Edge);
    }
}
