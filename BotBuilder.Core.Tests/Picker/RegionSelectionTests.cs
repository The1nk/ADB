using BotBuilder.Core.Picker;
using Xunit;

namespace BotBuilder.Core.Tests.Picker;

public class RegionSelectionTests
{
    [Fact]
    public void FromCorners_NormalizesTopLeftAndSize()
    {
        var r = RegionSelection.FromCorners(40, 30, 10, 5, 1000, 1000);
        Assert.Equal((10, 5, 30, 25), r); // left=10, top=5, w=40-10, h=30-5
    }

    [Fact]
    public void FromCorners_AlreadyOrdered_IsUnchanged()
    {
        var r = RegionSelection.FromCorners(10, 20, 60, 80, 1000, 1000);
        Assert.Equal((10, 20, 50, 60), r);
    }

    [Fact]
    public void FromCorners_ClampsToImageBounds()
    {
        var r = RegionSelection.FromCorners(-50, -10, 5000, 5000, 800, 600);
        Assert.Equal((0, 0, 800, 600), r);
    }

    [Fact]
    public void FromCorners_DegeneratePoint_ZeroSize()
    {
        var r = RegionSelection.FromCorners(100, 100, 100, 100, 800, 600);
        Assert.Equal((100, 100, 0, 0), r);
    }
}
