using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class FitViewportToNodesTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    [Fact]
    public void FitViewportToNodes_NoNodes_LeavesViewportUnchanged()
    {
        var editor = NewEditor();
        editor.New();

        editor.FitViewportToNodes(800, 600);

        Assert.Equal(1.0, editor.Viewport.Scale);
        Assert.Equal(0, editor.Viewport.OffsetX);
        Assert.Equal(0, editor.Viewport.OffsetY);
    }

    [Fact]
    public void FitViewportToNodes_CentersTheNodeBoundingBox()
    {
        var editor = NewEditor();
        editor.New();
        var a = editor.AddNode("control.start", 0, 0);
        var b = editor.AddNode("control.end", 300, 200);

        editor.FitViewportToNodes(800, 600);

        var minX = 0d;
        var minY = 0d;
        var maxX = 300 + NodeLayout.CardWidth;
        var maxY = Math.Max(a.Height, 200 + b.Height);
        var expectedCentreX = (minX + maxX) / 2;
        var expectedCentreY = (minY + maxY) / 2;

        var (worldX, worldY) = editor.Viewport.ScreenToWorld(400, 300);
        Assert.Equal(expectedCentreX, worldX, 3);
        Assert.Equal(expectedCentreY, worldY, 3);
    }
}
