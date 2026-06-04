using System.Collections.Generic;
using System.Linq;
using BotBuilder.Core.Picker;
using Xunit;

namespace BotBuilder.Core.Tests.Picker;

public class CoordinatePickerViewModelTests
{
    private static CoordinatePickerViewModel ForSwipe() =>
        new(CoordinateFieldMap.ForTypeKey("android.swipe"));

    private static CoordinatePickerViewModel ForTap() =>
        new(CoordinateFieldMap.ForTypeKey("android.tap"));

    [Fact]
    public void SinglePoint_CompletesAfterOneClick_AndYieldsPair()
    {
        var vm = ForTap();
        Assert.False(vm.IsComplete);
        Assert.Contains("Target", vm.CurrentPrompt);

        vm.RecordClick(120, 240);

        Assert.True(vm.IsComplete);
        var r = Assert.Single(vm.Results());
        Assert.Equal(("x", "y", 120, 240), (r.XKey, r.YKey, r.X, r.Y));
    }

    [Fact]
    public void TwoPoints_PromptsStartThenEnd_AndYieldsBothPairs()
    {
        var vm = ForSwipe();
        Assert.Contains("Start", vm.CurrentPrompt);

        vm.RecordClick(10, 20);
        Assert.False(vm.IsComplete);
        Assert.Contains("End", vm.CurrentPrompt);

        vm.RecordClick(30, 40);
        Assert.True(vm.IsComplete);

        var results = vm.Results().ToList();
        Assert.Equal(("x1", "y1", 10, 20), (results[0].XKey, results[0].YKey, results[0].X, results[0].Y));
        Assert.Equal(("x2", "y2", 30, 40), (results[1].XKey, results[1].YKey, results[1].X, results[1].Y));
    }

    [Fact]
    public void ClicksBeyondCompletion_AreIgnored()
    {
        var vm = ForTap();
        vm.RecordClick(1, 2);
        vm.RecordClick(9, 9); // ignored — already complete

        var r = Assert.Single(vm.Results());
        Assert.Equal((1, 2), (r.X, r.Y));
    }

    [Fact]
    public void ResultsBeforeCompletion_OnlyIncludesCollectedPoints()
    {
        var vm = ForSwipe();
        vm.RecordClick(5, 6);
        var r = Assert.Single(vm.Results()); // only the first point so far
        Assert.Equal(("x1", "y1", 5, 6), (r.XKey, r.YKey, r.X, r.Y));
    }
}
