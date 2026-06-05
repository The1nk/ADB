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

    [Fact]
    public void NodesInRect_UsesPerNodeHeight_ForGrownRunParallelNode()
    {
        // A 4-branch Run Parallel node is taller than the default card; a marquee that overlaps only
        // its lower half (well below the default 70-tall band) must still select it.
        var node = NodeViewModel.FromDefinition(new RunParallelAction(), Guid.NewGuid(), "", 100, 200);
        node.SetBranchPortCount(4);
        Assert.True(node.Height > NodeLayout.CardHeight_Default);

        var bandY = 200 + NodeLayout.CardHeight_Default + 5; // below where a default-height card would end
        Assert.True(bandY < 200 + node.Height);                // ...but still inside this grown card

        var hit = MarqueeSelection.NodesInRect(new[] { node }, 100, bandY, 50, 20).ToList();

        Assert.Single(hit);
        Assert.Same(node, hit[0]);
    }
}
