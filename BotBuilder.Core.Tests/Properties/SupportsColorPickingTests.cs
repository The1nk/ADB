using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests.Properties;

public class SupportsColorPickingTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    [Fact]
    public void True_ForMeasureBar_False_ForFindImage()
    {
        var editor = NewEditor();

        var measureBar = editor.AddNode("screen.measureBar", 0, 0);
        editor.Select(measureBar);
        Assert.True(editor.Properties.SupportsColorPicking);

        var findImage = editor.AddNode("screen.findImage", 0, 0);
        editor.Select(findImage);
        Assert.False(editor.Properties.SupportsColorPicking);
    }
}
