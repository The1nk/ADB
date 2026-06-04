using System.Linq;
using BotBuilder.Core.Picker;
using Xunit;

namespace BotBuilder.Core.Tests.Picker;

public class CoordinateFieldMapTests
{
    [Theory]
    [InlineData("android.tap")]
    [InlineData("input.click")]
    [InlineData("input.rightClick")]
    [InlineData("input.doubleClick")]
    [InlineData("input.mouseMove")]
    public void SinglePointActions_HaveOnePoint_XY(string typeKey)
    {
        var points = CoordinateFieldMap.ForTypeKey(typeKey);
        Assert.True(CoordinateFieldMap.Supports(typeKey));
        var p = Assert.Single(points);
        Assert.Equal("x", p.XKey);
        Assert.Equal("y", p.YKey);
    }

    [Fact]
    public void Swipe_HasTwoPoints_StartThenEnd()
    {
        var points = CoordinateFieldMap.ForTypeKey("android.swipe");
        Assert.Equal(2, points.Count);
        Assert.Equal(("x1", "y1", "Start"), (points[0].XKey, points[0].YKey, points[0].Label));
        Assert.Equal(("x2", "y2", "End"), (points[1].XKey, points[1].YKey, points[1].Label));
    }

    [Theory]
    [InlineData("screen.findImage")]
    [InlineData("data.log")]
    [InlineData("android.screenshot")]
    public void NonCoordinateActions_AreUnsupported_AndEmpty(string typeKey)
    {
        Assert.False(CoordinateFieldMap.Supports(typeKey));
        Assert.Empty(CoordinateFieldMap.ForTypeKey(typeKey));
    }
}
