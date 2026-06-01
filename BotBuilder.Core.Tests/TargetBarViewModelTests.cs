using AdbCore.Models;
using BotBuilder.Core.Targets;
using Xunit;

namespace BotBuilder.Core.Tests;

public class TargetBarViewModelTests
{
    [Fact]
    public void AddTarget_AppendsWithDefaultsAndUniqueId()
    {
        var bar = new TargetBarViewModel();

        var a = bar.AddTarget();
        var b = bar.AddTarget();

        Assert.Equal(2, bar.Targets.Count);
        Assert.NotEqual(Guid.Empty, a.Id);
        Assert.NotEqual(a.Id, b.Id);
        Assert.False(string.IsNullOrWhiteSpace(a.Name));
    }

    [Fact]
    public void RemoveTarget_RemovesIt()
    {
        var bar = new TargetBarViewModel();
        var a = bar.AddTarget();

        bar.RemoveTarget(a);

        Assert.Empty(bar.Targets);
    }

    [Fact]
    public void Changed_FiresOnAddAndRemove()
    {
        var bar = new TargetBarViewModel();
        var count = 0;
        bar.Changed += (_, _) => count++;

        var a = bar.AddTarget();
        bar.RemoveTarget(a);

        Assert.True(count >= 2);
    }

    [Fact]
    public void Changed_FiresWhenATargetIsRenamed()
    {
        var bar = new TargetBarViewModel();
        var a = bar.AddTarget();
        var fired = false;
        bar.Changed += (_, _) => fired = true;

        a.Name = "Renamed";

        Assert.True(fired);
    }

    [Fact]
    public void AllTypes_ExposesEveryTargetType()
    {
        Assert.Contains(BotTargetType.Window, TargetViewModel.AllTypes);
        Assert.Contains(BotTargetType.AndroidDevice, TargetViewModel.AllTypes);
        Assert.Contains(BotTargetType.Browser, TargetViewModel.AllTypes);
    }
}
