using AdbCore.Actions.BuiltIn;
using BotBuilder.Core;
using BotBuilder.Core.Canvas;
using Xunit;

namespace BotBuilder.Core.Tests;

public class MarqueeSelectionTests
{
    private static NodeViewModel NodeAt(double x, double y)
        => NodeViewModel.FromDefinition(new LogAction(), Guid.NewGuid(), "", x, y);

    [Fact]
    public void NodesInRect_IncludesNodesWhoseCardIntersects()
    {
        var inside = NodeAt(50, 50);
        var far = NodeAt(1000, 1000);

        var hit = MarqueeSelection.NodesInRect(new[] { inside, far }, 0, 0, 200, 200).ToList();

        Assert.Contains(inside, hit);
        Assert.DoesNotContain(far, hit);
    }

    [Fact]
    public void NodesInRect_IncludesPartialOverlap()
    {
        var node = NodeAt(40, 40);

        var hit = MarqueeSelection.NodesInRect(new[] { node }, 0, 0, 50, 50).ToList();

        Assert.Single(hit);
    }

    [Fact]
    public void NodesInRect_ExcludesNodeOutsideRect()
    {
        var node = NodeAt(300, 300);

        var hit = MarqueeSelection.NodesInRect(new[] { node }, 0, 0, 100, 100).ToList();

        Assert.Empty(hit);
    }
}
