using AdbCore.Models;
using BotBuilder.Core.Targets;
using Xunit;

namespace BotBuilder.Core.Tests.Targets;

public class TargetBarViewModelTests
{
    [Fact]
    public void ResolveForNode_NoTargets_ReturnsNull()
    {
        var bar = new TargetBarViewModel();
        Assert.Null(bar.ResolveForNode(null));
        Assert.Null(bar.ResolveForNode(Guid.NewGuid()));
    }

    [Fact]
    public void ResolveForNode_NullId_ReturnsFirstTarget()
    {
        var bar = new TargetBarViewModel();
        var first = bar.AddTarget();
        bar.AddTarget();

        Assert.Same(first, bar.ResolveForNode(null));
    }

    [Fact]
    public void ResolveForNode_KnownId_ReturnsThatTarget()
    {
        var bar = new TargetBarViewModel();
        bar.AddTarget();
        var second = bar.AddTarget();

        Assert.Same(second, bar.ResolveForNode(second.Id));
    }

    [Fact]
    public void ResolveForNode_UnknownId_ReturnsNull()
    {
        var bar = new TargetBarViewModel();
        bar.AddTarget();

        Assert.Null(bar.ResolveForNode(Guid.NewGuid()));
    }
}
